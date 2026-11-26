using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ExpandPuzzle
{
    /// <summary>
    /// The row of booster buttons at the bottom of the screen, each with a count badge, plus the
    /// banner that slides in at the top while the player picks a target ("Tap a block to destroy it").
    /// Built in code at start, so the scene needs no changes. It only shows and reports;
    /// <see cref="GameManager"/> decides what a booster does.
    /// </summary>
    public class BoosterBar : MonoBehaviour
    {
        /// <summary>A booster button was tapped.</summary>
        public event Action<BoosterType> Pressed;
        /// <summary>While picking a target: the player tapped the board (screen position).</summary>
        public event Action<Vector2> Tapped;
        /// <summary>While picking a target: the player closed the banner.</summary>
        public event Action Cancelled;

        const float ButtonSize = 132f;
        const float ButtonPitch = 162f;
        const float BottomMargin = 40f;

        static readonly Color Ink = new Color(0.2f, 0.26f, 0.3f, 1f);
        static readonly Color Gold = new Color(1f, 0.78f, 0.18f, 1f);
        static readonly Color Plus = new Color(0.24f, 0.74f, 0.42f, 1f);

        class Slot
        {
            public BoosterType type;
            public RectTransform rect;
            public RectTransform body;
            public Image glow, icon, badge;
            public Text count;
            public float punch, shake, glowBlend;
            public bool available = true;
        }

        /// <summary>Invisible full-screen surface that catches the tap on the board while picking a target.</summary>
        class TapCatcher : MonoBehaviour, IPointerClickHandler
        {
            public Action<Vector2> clicked;
            public void OnPointerClick(PointerEventData eventData)
            {
                if (clicked != null) clicked(eventData.position);
            }
        }

        Slot[] m_Slots;
        Image m_Dim;
        RectTransform m_Banner;
        Image m_BannerIcon;
        Text m_BannerTitle, m_BannerText;
        GameObject m_BannerClose;
        Sprite[] m_Icons;
        bool m_BannerOn;
        float m_BannerShow, m_BannerHideAt, m_BannerShake;
        int m_Selected = -1;
        float m_Visible = 1f;
        bool m_WantVisible = true;
        CanvasGroup m_BarGroup;

        public bool Targeting { get { return m_Selected >= 0; } }

        // ------------------------------------------------------------ building

        public static BoosterBar Create(GameUI ui)
        {
            if (ui == null) return null;
            Transform parent = ui.undoButton != null ? ui.undoButton.transform.parent : ui.transform;

            var go = new GameObject("Boosters", typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            Stretch(rt);
            var bar = go.AddComponent<BoosterBar>();
            bar.Build(ui);
            return bar;
        }

        void Build(GameUI ui)
        {
            Image undoImage = ui.undoButton != null ? ui.undoButton.GetComponent<Image>() : null;
            Sprite circle = undoImage != null ? undoImage.sprite : ui.pickerButtonSprite;
            Image card = ui.winBanner != null ? ui.winBanner.GetComponent<Image>() : null;
            Sprite rounded = card != null && card.sprite != null ? card.sprite : circle;
            Font font = ui.font != null ? ui.font : (ui.levelLabel != null ? ui.levelLabel.font : null);

            m_Icons = new Sprite[Boosters.All.Length];
            for (int i = 0; i < m_Icons.Length; i++) m_Icons[i] = BoosterIcons.Make(Boosters.All[i]);

            // ---- tap catcher (also dims the scene a little while picking a target)
            var dimGo = NewChild("Target Catcher", transform);
            Stretch((RectTransform)dimGo.transform);
            ((RectTransform)dimGo.transform).offsetMin = new Vector2(-400f, -400f);   // cover the notch margins too
            ((RectTransform)dimGo.transform).offsetMax = new Vector2(400f, 400f);
            m_Dim = dimGo.AddComponent<Image>();
            m_Dim.color = new Color(0.05f, 0.06f, 0.1f, 0f);
            dimGo.AddComponent<TapCatcher>().clicked = pos => { if (Tapped != null) Tapped(pos); };
            dimGo.SetActive(false);

            // ---- the buttons
            var row = NewChild("Bar", transform);
            var rowRt = (RectTransform)row.transform;
            rowRt.anchorMin = rowRt.anchorMax = new Vector2(0.5f, 0f);
            rowRt.pivot = new Vector2(0.5f, 0f);
            rowRt.anchoredPosition = new Vector2(0f, BottomMargin);
            rowRt.sizeDelta = new Vector2(ButtonPitch * Boosters.All.Length, ButtonSize);
            m_BarGroup = row.AddComponent<CanvasGroup>();

            m_Slots = new Slot[Boosters.All.Length];
            for (int i = 0; i < m_Slots.Length; i++)
            {
                BoosterType type = Boosters.All[i];
                var slot = new Slot { type = type };

                var slotGo = NewChild(type.ToString(), row.transform);
                slot.rect = (RectTransform)slotGo.transform;
                slot.rect.anchorMin = slot.rect.anchorMax = new Vector2(0.5f, 0.5f);
                slot.rect.anchoredPosition = new Vector2((i - (m_Slots.Length - 1) * 0.5f) * ButtonPitch, 0f);
                slot.rect.sizeDelta = new Vector2(ButtonSize, ButtonSize);

                slot.glow = NewImage("Glow", slot.rect, circle, Gold, ButtonSize);
                slot.glow.raycastTarget = false;
                slot.glow.rectTransform.localScale = Vector3.zero;

                Image body = NewImage("Body", slot.rect, circle, new Color(1f, 1f, 1f, 0.96f), ButtonSize);
                slot.body = body.rectTransform;
                var button = body.gameObject.AddComponent<Button>();
                button.targetGraphic = body;
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(() => { if (Pressed != null) Pressed(type); });
                PressScale.AddTo(button);

                Sprite iconSprite = m_Icons[i];
                if (type == BoosterType.StepBack && ui.undoButton != null)
                {
                    // The undo arrow the game already has.
                    foreach (Transform child in ui.undoButton.transform)
                    {
                        Image img = child.GetComponent<Image>();
                        if (img != null && img.sprite != null) { iconSprite = img.sprite; break; }
                    }
                    m_Icons[i] = iconSprite;
                }
                slot.icon = NewImage("Icon", slot.body, iconSprite, Ink, ButtonSize * 0.6f);
                slot.icon.raycastTarget = false;

                slot.badge = NewImage("Badge", slot.body, circle, Ink, 50f);
                slot.badge.raycastTarget = false;
                slot.badge.rectTransform.anchoredPosition = new Vector2(ButtonSize * 0.36f, ButtonSize * 0.36f);
                slot.count = NewText("Count", slot.badge.rectTransform, font, "", 32, Color.white);

                m_Slots[i] = slot;
            }

            // ---- banner
            var bannerGo = NewChild("Banner", transform);
            m_Banner = (RectTransform)bannerGo.transform;
            m_Banner.anchorMin = m_Banner.anchorMax = new Vector2(0.5f, 1f);
            m_Banner.pivot = new Vector2(0.5f, 1f);
            m_Banner.sizeDelta = new Vector2(820f, 176f);
            var bannerImage = bannerGo.AddComponent<Image>();
            bannerImage.sprite = rounded;
            bannerImage.type = Image.Type.Sliced;
            bannerImage.color = new Color(1f, 1f, 1f, 0.98f);

            Image iconBack = NewImage("Icon Back", m_Banner, circle, Gold, 124f);
            iconBack.raycastTarget = false;
            iconBack.rectTransform.anchorMin = iconBack.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            iconBack.rectTransform.anchoredPosition = new Vector2(96f, 0f);
            m_BannerIcon = NewImage("Icon", iconBack.rectTransform, null, Ink, 80f);
            m_BannerIcon.raycastTarget = false;

            m_BannerTitle = NewText("Title", m_Banner, font, "", 50, Ink);
            m_BannerTitle.alignment = TextAnchor.LowerLeft;
            m_BannerTitle.rectTransform.offsetMin = new Vector2(184f, 88f);
            m_BannerTitle.rectTransform.offsetMax = new Vector2(-110f, -14f);

            m_BannerText = NewText("Text", m_Banner, font, "", 32, new Color(Ink.r, Ink.g, Ink.b, 0.82f));
            m_BannerText.fontStyle = FontStyle.Normal;
            m_BannerText.alignment = TextAnchor.UpperLeft;
            m_BannerText.horizontalOverflow = HorizontalWrapMode.Wrap;
            m_BannerText.rectTransform.offsetMin = new Vector2(184f, 10f);
            m_BannerText.rectTransform.offsetMax = new Vector2(-110f, -92f);

            Image close = NewImage("Close", m_Banner, circle, new Color(0f, 0f, 0f, 0.08f), 84f);
            close.rectTransform.anchorMin = close.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            close.rectTransform.anchoredPosition = new Vector2(-62f, 0f);
            NewText("X", close.rectTransform, font, "X", 40, Ink);
            var closeButton = close.gameObject.AddComponent<Button>();
            closeButton.targetGraphic = close;
            closeButton.onClick.AddListener(() => { if (Cancelled != null) Cancelled(); });
            PressScale.AddTo(closeButton);
            m_BannerClose = close.gameObject;

            m_Banner.anchoredPosition = new Vector2(0f, 300f);
            bannerGo.SetActive(false);

            Boosters.Changed += Refresh;
            Refresh();
        }

        void OnDestroy()
        {
            Boosters.Changed -= Refresh;
        }

        static GameObject NewChild(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            return go;
        }

        static Image NewImage(string name, Transform parent, Sprite sprite, Color color, float size)
        {
            var go = NewChild(name, parent);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            return image;
        }

        static Text NewText(string name, Transform parent, Font font, string value, int size, Color color)
        {
            var go = NewChild(name, parent);
            Stretch((RectTransform)go.transform);
            var text = go.AddComponent<Text>();
            text.font = font;
            text.text = value;
            text.fontSize = size;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = color;
            text.raycastTarget = false;
            return text;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        Slot Find(BoosterType type)
        {
            for (int i = 0; i < m_Slots.Length; i++)
                if (m_Slots[i].type == type) return m_Slots[i];
            return m_Slots[0];
        }

        // ------------------------------------------------------------ what the game calls

        /// <summary>Updates the count badges. An empty booster shows a green "+".</summary>
        public void Refresh()
        {
            if (m_Slots == null) return;
            for (int i = 0; i < m_Slots.Length; i++)
            {
                int n = Boosters.Count(m_Slots[i].type);
                m_Slots[i].count.text = n > 0 ? (n > 99 ? "99" : n.ToString()) : "+";
                m_Slots[i].badge.color = n > 0 ? Ink : Plus;
            }
        }

        /// <summary>Greys a booster out when using it would do nothing right now (it can still be tapped, to say why).</summary>
        public void SetAvailable(BoosterType type, bool available)
        {
            Find(type).available = available;
        }

        /// <summary>Hides the whole bar (during the win celebration) or brings it back.</summary>
        public void SetVisible(bool visible)
        {
            m_WantVisible = visible;
        }

        /// <summary>The button and its badge jump: the booster was used.</summary>
        public void Punch(BoosterType type)
        {
            Find(type).punch = 1f;
        }

        /// <summary>The button shakes its head: not possible right now.</summary>
        public void Deny(BoosterType type)
        {
            Find(type).shake = 1f;
        }

        /// <summary>The banner shakes: that tap was not a valid target.</summary>
        public void DenyTap()
        {
            m_BannerShake = 1f;
        }

        /// <summary>Where a booster's button is on screen (pixels): effects start from there.</summary>
        public Vector2 ScreenPosition(BoosterType type)
        {
            return RectTransformUtility.WorldToScreenPoint(null, Find(type).rect.position);
        }

        /// <summary>Picking mode: the button glows, the banner explains what to tap, taps on the board are reported.</summary>
        public void BeginTargeting(BoosterType type, string instruction)
        {
            m_Selected = Array.IndexOf(Boosters.All, type);
            ShowBanner(type, Boosters.Title(type), instruction, true, 0f);
            m_Dim.gameObject.SetActive(true);
        }

        public void EndTargeting()
        {
            m_Selected = -1;
            m_BannerOn = false;
            m_Dim.gameObject.SetActive(false);
        }

        /// <summary>A short message in the banner that goes away by itself.</summary>
        public void Toast(BoosterType type, string title, string text, float seconds = 2.6f)
        {
            if (Targeting) return;
            ShowBanner(type, title, text, false, seconds);
        }

        void ShowBanner(BoosterType type, string title, string text, bool closable, float seconds)
        {
            m_Banner.gameObject.SetActive(true);
            m_BannerIcon.sprite = m_Icons[Array.IndexOf(Boosters.All, type)];
            m_BannerTitle.text = title;
            m_BannerText.text = text;
            m_BannerClose.SetActive(closable);
            m_BannerOn = true;
            m_BannerHideAt = seconds > 0f ? Time.unscaledTime + seconds : 0f;
        }

        // ------------------------------------------------------------ animation

        void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 1f / 20f);

            m_Visible = Mathf.MoveTowards(m_Visible, m_WantVisible ? 1f : 0f, dt * 5f);
            m_BarGroup.alpha = m_Visible;
            m_BarGroup.blocksRaycasts = m_Visible > 0.9f;
            ((RectTransform)m_BarGroup.transform).anchoredPosition = new Vector2(0f, BottomMargin - (1f - m_Visible) * 220f);

            for (int i = 0; i < m_Slots.Length; i++)
            {
                Slot s = m_Slots[i];
                bool selected = i == m_Selected;

                s.punch = Mathf.MoveTowards(s.punch, 0f, dt * 2.8f);
                s.shake = Mathf.MoveTowards(s.shake, 0f, dt * 2.6f);
                s.glowBlend = Mathf.MoveTowards(s.glowBlend, selected ? 1f : 0f, dt * 7f);

                float pop = Mathf.Sin((1f - s.punch) * Mathf.PI) * (s.punch > 0f ? 1f : 0f);
                float lift = selected ? 14f + 5f * Mathf.Sin(Time.unscaledTime * 6f) : 0f;
                s.rect.anchoredPosition = new Vector2(
                    (i - (m_Slots.Length - 1) * 0.5f) * ButtonPitch + Mathf.Sin(s.shake * 26f) * 12f * s.shake,
                    Mathf.Lerp(s.rect.anchoredPosition.y, lift + pop * 26f, dt * 18f));

                s.icon.rectTransform.localScale = Vector3.one * (1f + pop * 0.35f);
                s.icon.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(s.punch * 14f) * 16f * s.punch);
                s.badge.rectTransform.localScale = Vector3.one * (1f + pop * 0.7f);

                float pulse = 1.2f + 0.08f * Mathf.Sin(Time.unscaledTime * 6f);
                s.glow.rectTransform.localScale = Vector3.one * (s.glowBlend * pulse);

                // Others step back while one is being aimed; an unavailable one is faint.
                float alpha = (m_Selected >= 0 && !selected) ? 0.35f : (s.available ? 1f : 0.4f);
                Color c = s.icon.color;
                c.a = Mathf.MoveTowards(c.a, alpha, dt * 6f);
                s.icon.color = c;
            }

            // Dim while picking a target.
            if (m_Dim.gameObject.activeSelf)
            {
                Color d = m_Dim.color;
                d.a = Mathf.MoveTowards(d.a, 0.22f, dt * 1.6f);
                m_Dim.color = d;
            }
            else if (m_Dim.color.a > 0f)
            {
                Color d = m_Dim.color;
                d.a = 0f;
                m_Dim.color = d;
            }

            // Banner slides in from the top and out again.
            if (m_BannerOn && m_BannerHideAt > 0f && Time.unscaledTime >= m_BannerHideAt) m_BannerOn = false;
            m_BannerShow = Mathf.MoveTowards(m_BannerShow, m_BannerOn ? 1f : 0f, dt * (m_BannerOn ? 4.5f : 6f));
            m_BannerShake = Mathf.MoveTowards(m_BannerShake, 0f, dt * 2.6f);
            if (m_Banner.gameObject.activeSelf)
            {
                float k = OutBack(m_BannerShow);
                m_Banner.anchoredPosition = new Vector2(Mathf.Sin(m_BannerShake * 26f) * 16f * m_BannerShake, Mathf.LerpUnclamped(300f, -24f, k));
                if (!m_BannerOn && m_BannerShow <= 0f) m_Banner.gameObject.SetActive(false);
            }
        }

        static float OutBack(float t)
        {
            t = Mathf.Clamp01(t) - 1f;
            return 1f + t * t * (2.2f * t + 1.2f);
        }
    }

    /// <summary>Draws the booster icons as small white textures, so no art files are needed. Swap them for real art any time.</summary>
    public static class BoosterIcons
    {
        const int Size = 128;

        public static Sprite Make(BoosterType type)
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            tex.name = "Booster " + type;
            tex.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    // -1..1, y up
                    var p = new Vector2((x + 0.5f) / Size * 2f - 1f, (y + 0.5f) / Size * 2f - 1f);
                    float d = Distance(type, p);
                    float a = Mathf.Clamp01(0.5f - d * (Size * 0.5f));
                    pixels[y * Size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>Signed distance to the icon's shape (negative inside).</summary>
        static float Distance(BoosterType type, Vector2 p)
        {
            switch (type)
            {
                case BoosterType.Hint:
                {
                    // Light bulb: glass, neck, a separate base, and a "V" filament cut out.
                    float d = Mathf.Min(Circle(p, new Vector2(0f, 0.22f), 0.52f), Box(p, new Vector2(0f, -0.36f), new Vector2(0.24f, 0.2f), 0f));
                    d = Mathf.Max(d, -Mathf.Min(Segment(p, new Vector2(-0.16f, 0.2f), new Vector2(0f, -0.1f), 0.05f),
                                               Segment(p, new Vector2(0.16f, 0.2f), new Vector2(0f, -0.1f), 0.05f)));
                    return Mathf.Min(d, Box(p, new Vector2(0f, -0.74f), new Vector2(0.2f, 0.07f), 0f) - 0.03f);
                }
                case BoosterType.Pop:
                {
                    // Bomb: round body with a shine, a cap, a fuse and a spark.
                    float d = Circle(p, new Vector2(-0.1f, -0.16f), 0.58f);
                    d = Mathf.Max(d, -Segment(p, new Vector2(-0.42f, -0.02f), new Vector2(-0.28f, 0.2f), 0.06f));
                    d = Mathf.Min(d, Box(p, new Vector2(0.3f, 0.36f), new Vector2(0.17f, 0.11f), -45f));
                    d = Mathf.Min(d, Segment(p, new Vector2(0.42f, 0.5f), new Vector2(0.56f, 0.66f), 0.05f));
                    return Mathf.Min(d, Star(p, new Vector2(0.7f, 0.78f), 0.2f));
                }
                case BoosterType.Clear:
                {
                    // One big four-point star and two small ones.
                    float d = Star(p, new Vector2(-0.08f, -0.06f), 0.78f);
                    d = Mathf.Min(d, Star(p, new Vector2(0.62f, 0.6f), 0.3f));
                    return Mathf.Min(d, Star(p, new Vector2(0.6f, -0.62f), 0.2f));
                }
                default:
                {
                    // Fallback: a ring (the step-back button normally uses the game's own undo arrow).
                    return Mathf.Abs(Circle(p, Vector2.zero, 0.5f)) - 0.12f;
                }
            }
        }

        static float Circle(Vector2 p, Vector2 c, float r)
        {
            return (p - c).magnitude - r;
        }

        static float Box(Vector2 p, Vector2 c, Vector2 half, float degrees)
        {
            Vector2 q = p - c;
            if (degrees != 0f)
            {
                float a = -degrees * Mathf.Deg2Rad, s = Mathf.Sin(a), co = Mathf.Cos(a);
                q = new Vector2(q.x * co - q.y * s, q.x * s + q.y * co);
            }
            var d = new Vector2(Mathf.Abs(q.x) - half.x, Mathf.Abs(q.y) - half.y);
            return new Vector2(Mathf.Max(d.x, 0f), Mathf.Max(d.y, 0f)).magnitude + Mathf.Min(Mathf.Max(d.x, d.y), 0f);
        }

        static float Segment(Vector2 p, Vector2 a, Vector2 b, float r)
        {
            Vector2 pa = p - a, ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / ba.sqrMagnitude);
            return (pa - ba * h).magnitude - r;
        }

        /// <summary>A four-point star with curved-in sides (not an exact distance, but close enough for a clean edge).</summary>
        static float Star(Vector2 p, Vector2 c, float r)
        {
            Vector2 q = p - c;
            const float e = 0.6f;
            float f = Mathf.Pow(Mathf.Abs(q.x), e) + Mathf.Pow(Mathf.Abs(q.y), e) - Mathf.Pow(r, e);
            return f * r * 0.9f;
        }
    }
}
