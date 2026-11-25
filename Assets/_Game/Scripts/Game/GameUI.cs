using System;
using UnityEngine;
using UnityEngine.UI;

namespace ExpandPuzzle
{
    /// <summary>The HUD: level name, live star rating, undo / restart, the win card and the debug level picker.</summary>
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

        [Header("Level picker (debug)")]
        public bool debugLevelPicker = true;
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
            if (winBanner != null) winBanner.gameObject.SetActive(false);
            if (pickerRoot != null) pickerRoot.SetActive(false);
            if (fader != null) fader.enabled = false;
        }

        // ---------------------------------------------------------------- top bar

        public void SetLevel(int number, int total)
        {
            if (levelLabel != null) levelLabel.text = "LEVEL " + number;
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
                    hudStars[i].color = on ? starOn : starOff;
                    // A star that was just lost (or won back) gives a little kick.
                    if ((i < m_HudStars) != on && i < m_HudPunch.Length) m_HudPunch[i] = 1f;
                }
            }
            m_HudStars = stars;
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
            if (winStars != null)
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
            if (winBanner != null) winBanner.gameObject.SetActive(false);
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
            if (winStars != null)
            {
                for (int i = 0; i < winStars.Length; i++)
                {
                    if (winStars[i] == null) continue;
                    float local = m_BannerTime - (0.3f + i * 0.2f);
                    if (local < 0f) continue;
                    bool earned = i < m_WinStars;
                    winStars[i].color = earned ? starOn : new Color(0f, 0f, 0f, 0.1f);
                    float s = earned ? OutBack(local / 0.3f) * (1f + 0.25f * Mathf.Max(0f, 1f - local / 0.3f)) : 0.8f;
                    winStars[i].rectTransform.localScale = Vector3.one * s;
                    winStars[i].rectTransform.localRotation = Quaternion.Euler(0f, 0f, earned ? (1f - Mathf.Clamp01(local / 0.3f)) * -40f : 0f);
                }
            }
        }

        // ---------------------------------------------------------------- level picker

        /// <summary>Creates one button per level. <paramref name="usesColor"/> marks the coloured-pump levels.</summary>
        public void BuildPicker(int levelCount, Func<int, bool> usesColor)
        {
            if (m_PickerBuilt || pickerGrid == null) return;
            m_PickerBuilt = true;

            for (int i = 0; i < levelCount; i++)
            {
                int index = i;
                var go = new GameObject("Level " + (i + 1), typeof(RectTransform));
                go.transform.SetParent(pickerGrid, false);
                var img = go.AddComponent<Image>();
                img.sprite = pickerButtonSprite;
                bool colored = usesColor != null && usesColor(i);
                img.color = colored ? new Color(0.78f, 0.95f, 0.8f, 1f) : new Color(0.93f, 0.95f, 0.97f, 1f);
                var button = go.AddComponent<Button>();
                button.targetGraphic = img;
                button.onClick.AddListener(() =>
                {
                    ClosePicker();
                    if (LevelPicked != null) LevelPicked(index);
                });

                var textGo = new GameObject("Label", typeof(RectTransform));
                textGo.transform.SetParent(go.transform, false);
                var rt = textGo.GetComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = rt.offsetMax = Vector2.zero;
                var text = textGo.AddComponent<Text>();
                text.font = font != null ? font : (levelLabel != null ? levelLabel.font : null);
                text.text = (i + 1).ToString();
                text.fontSize = 52;
                text.fontStyle = FontStyle.Bold;
                text.alignment = TextAnchor.MiddleCenter;
                text.color = new Color(0.2f, 0.26f, 0.3f, 1f);
                text.raycastTarget = false;
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
            if (pickerRoot.activeSelf && m_ToggleRefresh != null) m_ToggleRefresh();
        }

        public void ClosePicker()
        {
            if (pickerRoot != null) pickerRoot.SetActive(false);
        }

        public bool PickerOpen { get { return pickerRoot != null && pickerRoot.activeSelf; } }
    }
}
