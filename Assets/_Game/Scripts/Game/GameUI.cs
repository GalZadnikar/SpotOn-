using System;
using UnityEngine;
using UnityEngine.UI;

namespace ExpandPuzzle
{
    /// <summary>The HUD: level name, live star rating, undo / restart, the win card and the level overview.</summary>
    public class GameUI : MonoBehaviour
    {
        [Header("Top bar")]
        public Text levelLabel;
        [Tooltip("Tapping the level name opens the level picker (debug).")]
        public Button levelButton;
        public Text movesLabel;
        public Image[] hudStars;
        public Button undoButton;
        public Button restartButton;

        [Header("Win card")]
        public RectTransform winBanner;
        public Text winLabel;
        public Text winMovesLabel;
        public Image[] winStars;

        [Header("Win card star effects")]
        [Tooltip("Optional: a three-star prefab from Epic Victory Effects (Prefabs/Star_0X/Star_0X_Anim_YY). Empty = plain stars.")]
        public GameObject starEffectPrefab;
        [Tooltip("Where the effect is created: a child of the win card. The card must be on a camera canvas for particles to show.")]
        public RectTransform starEffectAnchor;
        [Tooltip("Size of the effect: canvas units per unit of the prefab (its stars are 1.1 apart).")]
        public float starEffectScale = 136f;
        [Tooltip("Seconds between one star and the next.")]
        public float starEffectGap = 0.3f;
        [Tooltip("Sorting order of the particles; must be above the win card's canvas.")]
        public int starEffectSortingOrder = 10;

        [Header("Level overview (tap the level name)")]
        public bool debugLevelPicker = true;
        [Tooltip("How many levels one page of the overview shows (5 columns).")]
        public int levelsPerPage = 25;
        [Tooltip("Star used on the level buttons. Empty = same as the HUD stars.")]
        public Sprite starSprite;
        public GameObject pickerRoot;
        public RectTransform pickerGrid;
        public Button pickerCloseButton;
        public Sprite pickerButtonSprite;
        public Font font;

        [Header("Misc")]
        [Tooltip("Full-screen image used to wipe between levels.")]
        public Image fader;
        public Color starOn = new Color(1f, 0.78f, 0.18f, 1f);
        public Color starOff = new Color(0f, 0f, 0f, 0.14f);

        public event Action UndoClicked;
        public event Action RestartClicked;
        public event Action<int> LevelPicked;
        /// <summary>A star just landed on the win card (0, 1, 2).</summary>
        public event Action<int> WinStarShown;

        static readonly string[] Cheers = { "Nice!", "Snug fit!", "Perfect!", "Well pumped!", "Squish!" };

        float m_BannerTime = -1f;
        int m_WinStars;
        int m_HudStars = 3;
        readonly float[] m_HudPunch = new float[3];
        Color m_Ink = new Color(0.27f, 0.34f, 0.31f, 1f);
        bool m_PickerBuilt;

        void Awake()
        {
            if (undoButton != null) undoButton.onClick.AddListener(() => { if (UndoClicked != null) UndoClicked(); });
            if (restartButton != null) restartButton.onClick.AddListener(() => { if (RestartClicked != null) RestartClicked(); });
            if (levelButton != null) levelButton.onClick.AddListener(TogglePicker);
            if (pickerCloseButton != null) pickerCloseButton.onClick.AddListener(ClosePicker);
            // Buttons dip under the finger.
            PressScale.AddTo(undoButton);
            PressScale.AddTo(restartButton);
            if (winBanner != null) winBanner.gameObject.SetActive(false);
            if (pickerRoot != null) pickerRoot.SetActive(false);
            if (fader != null) fader.enabled = false;
        }

        // ---------------------------------------------------------------- top bar

        /// <param name="prefix">Word in front of the number: "LEVEL" for the campaign, the section's name otherwise.</param>
        /// <param name="index">Position of the level in the whole list (for the overview). -1 = number - 1.</param>
        public void SetLevel(int number, int total, string prefix = "LEVEL", int index = -1)
        {
            if (levelLabel != null)
            {
                // A new level number pops in: the old one stays for the first half of the pop, then flips.
                string text = prefix + " " + number;
                if (m_LevelShown > 0 && m_LevelShown != number)
                {
                    m_LevelText = text;
                    m_LevelPop = 1f;
                }
                else
                {
                    levelLabel.text = text;
                    m_LevelText = null;
                }
                m_LevelShown = number;
            }
            m_CurrentLevel = index >= 0 ? index : number - 1;
            HideWin();
            ClosePicker();
        }

        /// <param name="limit">Most moves that still keep the current star count (int.MaxValue = no limit).</param>
        public void SetMoves(int moves, int limit, int stars)
        {
            if (movesLabel != null)
                movesLabel.text = limit == int.MaxValue ? moves + (moves == 1 ? " move" : " moves") : moves + " / " + limit;

            if (hudStars != null)
            {
                for (int i = 0; i < hudStars.Length; i++)
                {
                    if (hudStars[i] == null) continue;
                    bool on = i < stars;
                    // Losing a star during play: the gold star drops off, the empty slot stays.
                    if (!on && i < m_HudStars && moves > 0) DropStar(hudStars[i]);
                    hudStars[i].color = on ? starOn : starOff;
                    // A star that was just lost (or won back) gives a little kick.
                    if ((i < m_HudStars) != on && i < m_HudPunch.Length) m_HudPunch[i] = 1f;
                }
            }
            m_HudStars = stars;
        }

        // ---------------------------------------------------------------- juice

        int m_LevelShown;
        string m_LevelText;
        float m_LevelPop;

        class FallingStar
        {
            public RectTransform rect;
            public Image image;
            public Vector2 velocity;
            public float spin;
            public float age;
        }

        readonly System.Collections.Generic.List<FallingStar> m_Falling = new System.Collections.Generic.List<FallingStar>();

        /// <summary>A gold copy of a HUD star pops up, tumbles down and fades.</summary>
        void DropStar(Image source)
        {
            if (source == null) return;
            var go = new GameObject("Lost Star", typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(source.rectTransform.parent, false);
            rect.anchorMin = source.rectTransform.anchorMin;
            rect.anchorMax = source.rectTransform.anchorMax;
            rect.pivot = source.rectTransform.pivot;
            rect.anchoredPosition = source.rectTransform.anchoredPosition;
            rect.sizeDelta = source.rectTransform.sizeDelta;
            var image = go.AddComponent<Image>();
            image.sprite = source.sprite;
            image.color = starOn;
            image.raycastTarget = false;
            m_Falling.Add(new FallingStar
            {
                rect = rect,
                image = image,
                velocity = new Vector2(UnityEngine.Random.Range(-140f, 140f), 520f),
                spin = UnityEngine.Random.Range(-420f, 420f)
            });
        }

        void UpdateJuice(float dt)
        {
            for (int i = m_Falling.Count - 1; i >= 0; i--)
            {
                FallingStar f = m_Falling[i];
                f.age += dt;
                f.velocity.y -= 2600f * dt;
                f.rect.anchoredPosition += f.velocity * dt;
                f.rect.localRotation = Quaternion.Euler(0f, 0f, f.spin * f.age);
                Color c = starOn;
                c.a = Mathf.Clamp01(1f - (f.age - 0.35f) / 0.4f);
                f.image.color = c;
                if (f.age > 0.8f)
                {
                    Destroy(f.rect.gameObject);
                    m_Falling.RemoveAt(i);
                }
            }

            if (levelLabel != null && m_LevelPop > 0f)
            {
                m_LevelPop = Mathf.MoveTowards(m_LevelPop, 0f, dt * 2.6f);
                if (m_LevelText != null && m_LevelPop <= 0.55f)
                {
                    levelLabel.text = m_LevelText;
                    m_LevelText = null;
                }
                float k = Mathf.Sin((1f - m_LevelPop) * Mathf.PI);
                levelLabel.rectTransform.localScale = Vector3.one * (1f + 0.28f * k);
                levelLabel.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(m_LevelPop * 16f) * 4f * m_LevelPop);
                if (m_LevelPop <= 0f)
                {
                    levelLabel.rectTransform.localScale = Vector3.one;
                    levelLabel.rectTransform.localRotation = Quaternion.identity;
                }
            }
        }

        /// <summary>Text colour that sits well on the current board colour.</summary>
        public void SetInk(Color surface)
        {
            float h, s, v;
            Color.RGBToHSV(surface, out h, out s, out v);
            m_Ink = Color.HSVToRGB(h, Mathf.Clamp01(s + 0.25f), 0.36f);
            if (levelLabel != null) levelLabel.color = m_Ink;
            if (movesLabel != null) movesLabel.color = new Color(m_Ink.r, m_Ink.g, m_Ink.b, 0.65f);
            starOff = new Color(m_Ink.r, m_Ink.g, m_Ink.b, 0.18f);
            TintIcon(undoButton);
            TintIcon(restartButton);
        }

        void TintIcon(Button button)
        {
            if (button == null) return;
            foreach (var img in button.GetComponentsInChildren<Image>(true))
                if (img.gameObject != button.gameObject) img.color = m_Ink;
        }

        // ---------------------------------------------------------------- win card

        public void ShowWin(int levelNumber, int stars, int moves)
        {
            if (winBanner == null) return;
            if (winLabel != null)
            {
                winLabel.text = stars >= 3 ? Cheers[(levelNumber - 1) % Cheers.Length] : stars == 2 ? "Solved!" : "Made it!";
            }
            if (winMovesLabel != null) winMovesLabel.text = moves + (moves == 1 ? " move" : " moves");
            m_WinStars = stars;
            m_PlainStarsShown = 0;
            SpawnStarEffect();
            if (winStars != null && m_StarEffect == null)
            {
                for (int i = 0; i < winStars.Length; i++)
                {
                    if (winStars[i] == null) continue;
                    winStars[i].color = new Color(0f, 0f, 0f, 0.1f);
                    winStars[i].rectTransform.localScale = Vector3.one * 0.8f;
                }
            }
            winBanner.gameObject.SetActive(true);
            winBanner.localScale = Vector3.zero;
            m_BannerTime = 0f;
        }

        public void HideWin()
        {
            m_BannerTime = -1f;
            if (m_StarEffect != null) Destroy(m_StarEffect);
            m_StarEffect = null;
            if (winBanner != null) winBanner.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------- win card star effects

        GameObject m_StarEffect;
        int m_PlainStarsShown;   // bit per plain star that already announced itself
        readonly Transform[] m_EffectStars = new Transform[3];

        /// <summary>Extra time the win card needs on screen so the last star effect can finish.</summary>
        public float WinExtraTime { get { return starEffectPrefab != null && starEffectAnchor != null ? 0.9f : 0f; } }

        /// <summary>
        /// Puts the three-star effect on the win card with all its stars switched off; Update switches the
        /// earned ones on one by one. The plain star images stay underneath as empty slots.
        /// </summary>
        void SpawnStarEffect()
        {
            if (m_StarEffect != null) Destroy(m_StarEffect);
            m_StarEffect = null;
            if (starEffectPrefab == null || starEffectAnchor == null) return;

            m_StarEffect = Instantiate(starEffectPrefab, starEffectAnchor, false);
            Transform root = m_StarEffect.transform;
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one * starEffectScale;

            // The pack's own controller polls the old Input class; we drive the stars ourselves.
            foreach (var script in m_StarEffect.GetComponents<MonoBehaviour>())
            {
                script.enabled = false;
                Destroy(script);
            }

            foreach (var t in m_StarEffect.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = starEffectAnchor.gameObject.layer;
            foreach (var ps in m_StarEffect.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                var r = ps.GetComponent<ParticleSystemRenderer>();
                if (r != null) r.sortingOrder = starEffectSortingOrder;
            }

            for (int i = 0; i < m_EffectStars.Length; i++)
            {
                m_EffectStars[i] = i < root.childCount ? root.GetChild(i) : null;
                if (m_EffectStars[i] == null) continue;
                m_EffectStars[i].gameObject.SetActive(false);

                // Empty slot under each star, exactly where the effect will put it.
                if (winStars == null || i >= winStars.Length || winStars[i] == null) continue;
                RectTransform slot = winStars[i].rectTransform;
                Vector3 p = m_EffectStars[i].localPosition * starEffectScale;
                slot.anchoredPosition = starEffectAnchor.anchoredPosition + new Vector2(p.x, p.y);
                slot.sizeDelta = Vector2.one * starEffectScale * 0.8f;
                slot.localScale = Vector3.one;
                slot.localRotation = Quaternion.identity;
                winStars[i].color = new Color(0f, 0f, 0f, 0.1f);
            }
        }

        /// <summary>0 = clear, 1 = screen fully covered with <paramref name="color"/>.</summary>
        public void SetFade(Color color, float amount)
        {
            if (fader == null) return;
            color.a = Mathf.Clamp01(amount);
            fader.color = color;
            fader.enabled = amount > 0.001f;
        }

        static float OutBack(float t)
        {
            t = Mathf.Clamp01(t) - 1f;
            return 1f + 2.7f * t * t * t + 1.7f * t * t;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            UpdateJuice(dt);

            if (hudStars != null)
            {
                for (int i = 0; i < hudStars.Length && i < m_HudPunch.Length; i++)
                {
                    if (hudStars[i] == null) continue;
                    m_HudPunch[i] = Mathf.MoveTowards(m_HudPunch[i], 0f, dt * 3.5f);
                    float k = m_HudPunch[i];
                    hudStars[i].rectTransform.localScale = Vector3.one * (1f + Mathf.Sin(k * Mathf.PI) * 0.45f);
                    hudStars[i].rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(k * 18f) * 14f * k);
                }
            }

            if (m_BannerTime < 0f || winBanner == null) return;
            m_BannerTime += dt;
            winBanner.localScale = Vector3.one * OutBack(m_BannerTime / 0.35f);

            // Stars land one after another.
            if (m_StarEffect != null)
            {
                for (int i = 0; i < m_EffectStars.Length && i < m_WinStars; i++)
                {
                    if (m_EffectStars[i] == null || m_EffectStars[i].gameObject.activeSelf) continue;
                    if (m_BannerTime >= 0.15f + i * starEffectGap)
                    {
                        m_EffectStars[i].gameObject.SetActive(true);
                        if (WinStarShown != null) WinStarShown(i);
                    }
                }
            }
            else if (winStars != null)
            {
                for (int i = 0; i < winStars.Length; i++)
                {
                    if (winStars[i] == null) continue;
                    float local = m_BannerTime - (0.3f + i * 0.2f);
                    if (local < 0f) continue;
                    bool earned = i < m_WinStars;
                    if (earned && (m_PlainStarsShown & (1 << i)) == 0)
                    {
                        m_PlainStarsShown |= 1 << i;
                        if (WinStarShown != null) WinStarShown(i);
                    }
                    winStars[i].color = earned ? starOn : new Color(0f, 0f, 0f, 0.1f);
                    float s = earned ? OutBack(local / 0.3f) * (1f + 0.25f * Mathf.Max(0f, 1f - local / 0.3f)) : 0.8f;
                    winStars[i].rectTransform.localScale = Vector3.one * s;
                    winStars[i].rectTransform.localRotation = Quaternion.Euler(0f, 0f, earned ? (1f - Mathf.Clamp01(local / 0.3f)) * -40f : 0f);
                }
            }
        }

        // ---------------------------------------------------------------- level overview

        class PickerEntry
        {
            public GameObject go;
            public Image background;
            public Button button;
            public Text label;
            public Image[] stars;
            public bool colored;
        }

        readonly System.Collections.Generic.List<PickerEntry> m_Entries = new System.Collections.Generic.List<PickerEntry>();
        readonly System.Collections.Generic.List<Image> m_Tabs = new System.Collections.Generic.List<Image>();
        Func<int, int> m_StarsOf;
        Func<int, bool> m_Unlocked;
        int m_Page;
        // Pages follow the sections (Base, Test, ...): every section starts a new page.
        readonly System.Collections.Generic.List<int> m_EntryPage = new System.Collections.Generic.List<int>();
        readonly System.Collections.Generic.List<string> m_PageSection = new System.Collections.Generic.List<string>();
        Text m_PickerTitle;
        int m_CurrentLevel;

        static readonly Color PickerInk = new Color(0.2f, 0.26f, 0.3f, 1f);
        static readonly Color PickerGold = new Color(1f, 0.78f, 0.18f, 1f);

        /// <summary>
        /// Creates the level overview: one button per level with its best star rating, shown in pages.
        /// <paramref name="starsOf"/> gives the best stars so far (0 = not solved yet),
        /// <paramref name="unlocked"/> says whether a level may be played (null = all open).
        /// </summary>
        /// <param name="sectionOf">Name of the section a level belongs to (null = one list).</param>
        /// <param name="numberOf">Number shown on a level's button (null = its place in the whole list).</param>
        public void BuildPicker(int levelCount, Func<int, bool> usesColor, Func<int, int> starsOf = null, Func<int, bool> unlocked = null,
            Func<int, string> sectionOf = null, Func<int, int> numberOf = null)
        {
            if (m_PickerBuilt || pickerGrid == null) return;
            m_PickerBuilt = true;
            m_StarsOf = starsOf;
            m_Unlocked = unlocked;

            Transform panel = pickerGrid.parent;
            Image panelImage = panel != null ? panel.GetComponent<Image>() : null;
            Sprite square = panelImage != null && panelImage.sprite != null ? panelImage.sprite : pickerButtonSprite;
            Sprite star = starSprite != null ? starSprite : (hudStars != null && hudStars.Length > 0 && hudStars[0] != null ? hudStars[0].sprite : null);
            Font useFont = font != null ? font : (levelLabel != null ? levelLabel.font : null);

            Transform title = panel != null ? panel.Find("Title") : null;
            m_PickerTitle = title != null ? title.GetComponent<Text>() : null;
            if (m_PickerTitle != null) m_PickerTitle.text = "SELECT LEVEL";

            // Room under each button for the stars that hang over its bottom edge.
            var grid = pickerGrid.GetComponent<GridLayoutGroup>();
            if (grid != null) grid.spacing = new Vector2(grid.spacing.x, 46f);

            string lastSection = null;
            int onThisPage = 0;
            var pageFirst = new System.Collections.Generic.List<int>();
            var pageLast = new System.Collections.Generic.List<int>();
            for (int i = 0; i < levelCount; i++)
            {
                int index = i;
                string section = sectionOf != null ? sectionOf(i) : "";
                int number = numberOf != null ? numberOf(i) : i + 1;
                if (i == 0 || section != lastSection || onThisPage >= PerPage)
                {
                    m_PageSection.Add(section);
                    pageFirst.Add(number);
                    pageLast.Add(number);
                    onThisPage = 0;
                }
                lastSection = section;
                onThisPage++;
                pageLast[pageLast.Count - 1] = number;
                m_EntryPage.Add(m_PageSection.Count - 1);

                var entry = new PickerEntry();
                entry.colored = usesColor != null && usesColor(i);

                entry.go = new GameObject("Level " + (i + 1), typeof(RectTransform));
                entry.go.transform.SetParent(pickerGrid, false);
                entry.background = entry.go.AddComponent<Image>();
                entry.background.sprite = square;
                entry.background.type = Image.Type.Sliced;
                entry.button = entry.go.AddComponent<Button>();
                entry.button.targetGraphic = entry.background;
                PressScale.AddTo(entry.button);
                entry.button.onClick.AddListener(() =>
                {
                    ClosePicker();
                    if (LevelPicked != null) LevelPicked(index);
                });

                entry.label = PickerText(entry.go.transform, useFont, number.ToString(), 52);
                entry.label.rectTransform.offsetMin = new Vector2(0f, 18f);

                entry.stars = new Image[3];
                for (int s = 0; s < 3; s++)
                {
                    var starGo = new GameObject("Star " + (s + 1), typeof(RectTransform));
                    starGo.transform.SetParent(entry.go.transform, false);
                    var rt = starGo.GetComponent<RectTransform>();
                    rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
                    rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = new Vector2((s - 1) * 42f, s == 1 ? -4f : 8f);
                    rt.sizeDelta = new Vector2(44f, 44f);
                    var img = starGo.AddComponent<Image>();
                    img.sprite = star;
                    img.raycastTarget = false;
                    entry.stars[s] = img;
                }
                m_Entries.Add(entry);
            }

            // Page tabs ("1-25", "26-50", ...) right under the grid; only needed with more than one page.
            int pages = PageCount;
            if (pages > 1 && panel != null)
            {
                var row = new GameObject("Pages", typeof(RectTransform));
                row.transform.SetParent(panel, false);
                row.transform.SetSiblingIndex(pickerGrid.GetSiblingIndex() + 1);
                var hlg = row.AddComponent<HorizontalLayoutGroup>();
                hlg.spacing = 20f;
                hlg.childAlignment = TextAnchor.MiddleCenter;
                hlg.childControlWidth = true;
                hlg.childControlHeight = true;
                hlg.childForceExpandWidth = true;
                hlg.childForceExpandHeight = true;
                row.AddComponent<LayoutElement>().preferredHeight = 100f;

                for (int p = 0; p < pages; p++)
                {
                    int page = p;
                    var tabGo = new GameObject("Page " + (p + 1), typeof(RectTransform));
                    tabGo.transform.SetParent(row.transform, false);
                    var img = tabGo.AddComponent<Image>();
                    img.sprite = square;
                    img.type = Image.Type.Sliced;
                    var button = tabGo.AddComponent<Button>();
                    button.targetGraphic = img;
                    PressScale.AddTo(button);
                    button.onClick.AddListener(() => { m_Page = page; RefreshPicker(); });
                    int first = pageFirst[p];
                    int last = pageLast[p];
                    PickerText(tabGo.transform, useFont, TabLabel(p, first, last), 36);
                    m_Tabs.Add(img);
                }
            }

            RefreshPicker();
        }

        int PerPage { get { return Mathf.Max(1, levelsPerPage); } }
        int PageCount { get { return Mathf.Max(1, m_PageSection.Count); } }

        /// <summary>Text on a page tab: the number range for the first section, the section's name for the others.</summary>
        string TabLabel(int page, int first, int last)
        {
            string section = m_PageSection[page];
            if (string.IsNullOrEmpty(section) || section == m_PageSection[0]) return first + " - " + last;
            bool alone = (page == 0 || m_PageSection[page - 1] != section) &&
                         (page == m_PageSection.Count - 1 || m_PageSection[page + 1] != section);
            return alone ? section.ToUpper() : section.ToUpper() + " " + first + "-" + last;
        }

        static Text PickerText(Transform parent, Font useFont, string value, int size)
        {
            var textGo = new GameObject("Label", typeof(RectTransform));
            textGo.transform.SetParent(parent, false);
            var rt = textGo.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var text = textGo.AddComponent<Text>();
            text.font = useFont;
            text.text = value;
            text.fontSize = size;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = PickerInk;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>Shows the current page and brings stars, locks and the "you are here" mark up to date.</summary>
        public void RefreshPicker()
        {
            m_Page = Mathf.Clamp(m_Page, 0, PageCount - 1);
            if (m_PickerTitle != null && m_PageSection.Count > 0)
            {
                string shown = m_PageSection[m_Page];
                m_PickerTitle.text = string.IsNullOrEmpty(shown) ? "SELECT LEVEL" : shown.ToUpper() + " LEVELS";
            }
            for (int i = 0; i < m_Entries.Count; i++)
            {
                PickerEntry e = m_Entries[i];
                bool onPage = m_EntryPage[i] == m_Page;
                if (e.go.activeSelf != onPage) e.go.SetActive(onPage);
                if (!onPage) continue;

                int stars = m_StarsOf != null ? m_StarsOf(i) : 0;
                bool open = m_Unlocked == null || m_Unlocked(i);
                bool current = i == m_CurrentLevel;

                e.button.interactable = open;
                Color back = e.colored ? new Color(0.78f, 0.95f, 0.8f, 1f) : new Color(0.93f, 0.95f, 0.97f, 1f);
                if (current) back = PickerInk;
                e.background.color = back;
                Color ink = current ? Color.white : PickerInk;
                ink.a = open ? 1f : 0.3f;
                e.label.color = ink;
                for (int s = 0; s < e.stars.Length; s++)
                {
                    e.stars[s].enabled = open;
                    e.stars[s].color = s < stars ? PickerGold : new Color(0.72f, 0.75f, 0.78f, 1f);
                }
            }
            for (int p = 0; p < m_Tabs.Count; p++)
            {
                bool on = p == m_Page;
                m_Tabs[p].color = on ? PickerInk : new Color(0.93f, 0.95f, 0.97f, 1f);
                Text label = m_Tabs[p].GetComponentInChildren<Text>();
                if (label != null) label.color = on ? Color.white : PickerInk;
            }
        }

        /// <summary>Adds an on/off row under the level buttons (debug options).</summary>
        public void AddPickerToggle(string label, Func<bool> isOn, Action toggle)
        {
            if (pickerGrid == null || pickerGrid.parent == null) return;

            var go = new GameObject("Toggle - " + label, typeof(RectTransform));
            go.transform.SetParent(pickerGrid.parent, false);
            var img = go.AddComponent<Image>();
            var layout = go.AddComponent<LayoutElement>();
            layout.preferredHeight = 96f;
            var button = go.AddComponent<Button>();
            button.targetGraphic = img;

            var textGo = new GameObject("Label", typeof(RectTransform));
            textGo.transform.SetParent(go.transform, false);
            var rt = textGo.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var text = textGo.AddComponent<Text>();
            text.font = font != null ? font : (levelLabel != null ? levelLabel.font : null);
            text.fontSize = 34;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = new Color(0.2f, 0.26f, 0.3f, 1f);
            text.raycastTarget = false;

            Action refresh = () =>
            {
                bool on = isOn();
                text.text = label + ":  " + (on ? "ON" : "OFF");
                img.color = on ? new Color(1f, 0.9f, 0.55f, 1f) : new Color(0.9f, 0.92f, 0.94f, 1f);
            };
            button.onClick.AddListener(() => { toggle(); refresh(); });
            m_ToggleRefresh += refresh;
            refresh();
        }

        Action m_ToggleRefresh;

        public void TogglePicker()
        {
            if (!debugLevelPicker || pickerRoot == null) return;
            pickerRoot.SetActive(!pickerRoot.activeSelf);
            if (!pickerRoot.activeSelf) return;
            m_Page = m_CurrentLevel >= 0 && m_CurrentLevel < m_EntryPage.Count ? m_EntryPage[m_CurrentLevel] : 0;   // open on the page of the level being played
            RefreshPicker();
            if (m_ToggleRefresh != null) m_ToggleRefresh();
        }

        public void ClosePicker()
        {
            if (pickerRoot != null) pickerRoot.SetActive(false);
        }

        public bool PickerOpen { get { return pickerRoot != null && pickerRoot.activeSelf; } }
    }
}
