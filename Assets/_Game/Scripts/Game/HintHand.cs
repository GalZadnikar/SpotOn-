using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ExpandPuzzle
{
    /// <summary>
    /// A tutorial hand that acts out the next move of a hint, over and over, until the player does it:
    /// a swipe (press, slide one cell, lift) or dragging a movable wall to its spot.
    /// Lives on the game canvas just above the touch surface, so the HUD and menus stay on top.
    /// It never takes touches, and it steps aside while a finger is on the screen.
    /// </summary>
    public class HintHand : MonoBehaviour
    {
        /// <summary>Where on the sprite the fingertip is (0..1, from the bottom left).</summary>
        public Vector2 fingertip = new Vector2(0.15f, 0.85f);

        enum Gesture { None, Swipe, Drag }

        RectTransform m_Rect, m_Hand, m_Ring;
        Image m_HandImage, m_RingImage;
        CanvasGroup m_Group;
        Canvas m_Canvas;

        Gesture m_Gesture;
        Camera m_Camera;
        Vector3 m_WorldFrom, m_WorldTo, m_WorldStep;   // projected every frame, so camera moves and resizes never leave it behind
        Vector2 m_From, m_To;     // screen pixels, this frame
        float m_Size;             // hand height in screen pixels, this frame
        float m_Time;
        float m_Alpha;
        bool m_WasPressed;

        /// <summary>Creates the hand under the canvas, right above <paramref name="below"/> (the touch surface).</summary>
        public static HintHand Create(Canvas canvas, Transform below, Sprite hand, Sprite ring)
        {
            if (canvas == null || hand == null) return null;
            var go = new GameObject("Hint Hand", typeof(RectTransform));
            go.layer = canvas.gameObject.layer;
            go.transform.SetParent(canvas.transform, false);
            if (below != null && below.parent == canvas.transform) go.transform.SetSiblingIndex(below.GetSiblingIndex() + 1);
            var h = go.AddComponent<HintHand>();
            h.Build(canvas, hand, ring);
            return h;
        }

        void Build(Canvas canvas, Sprite hand, Sprite ring)
        {
            m_Canvas = canvas;
            m_Rect = (RectTransform)transform;
            m_Rect.anchorMin = Vector2.zero;
            m_Rect.anchorMax = Vector2.one;
            m_Rect.offsetMin = m_Rect.offsetMax = Vector2.zero;
            m_Group = gameObject.AddComponent<CanvasGroup>();
            m_Group.blocksRaycasts = false;
            m_Group.interactable = false;
            m_Group.alpha = 0f;

            // A ring that spreads out where the finger presses down.
            if (ring != null)
            {
                m_RingImage = NewImage("Press Ring", ring);
                m_Ring = m_RingImage.rectTransform;
                m_Ring.pivot = new Vector2(0.5f, 0.5f);
            }

            m_HandImage = NewImage("Hand", hand);
            m_Hand = m_HandImage.rectTransform;
            m_Hand.pivot = fingertip;
            float aspect = hand.rect.width / Mathf.Max(1f, hand.rect.height);
            m_Hand.sizeDelta = new Vector2(aspect, 1f);   // scaled to the real size in Update
            gameObject.SetActive(false);
        }

        Image NewImage(string name, Sprite sprite)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = gameObject.layer;
            go.transform.SetParent(transform, false);
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.raycastTarget = false;
            img.preserveAspect = true;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            return img;
        }

        /// <summary>
        /// Shows a swipe that starts on <paramref name="from"/> and heads towards <paramref name="towards"/> (world points;
        /// the slide is made a little longer than that, so it reads as a real swipe). <paramref name="cellStep"/> is one
        /// cell in world space: it sets the size of the hand.
        /// </summary>
        public void ShowSwipe(Camera cam, Vector3 from, Vector3 towards, Vector3 cellStep)
        {
            Show(Gesture.Swipe, cam, from, towards, cellStep);
        }

        /// <summary>Shows pressing on something at <paramref name="from"/> and dragging it to <paramref name="to"/> (world points).</summary>
        public void ShowDrag(Camera cam, Vector3 from, Vector3 to, Vector3 cellStep)
        {
            Show(Gesture.Drag, cam, from, to, cellStep);
        }

        void Show(Gesture gesture, Camera cam, Vector3 from, Vector3 to, Vector3 cellStep)
        {
            bool same = gameObject.activeSelf && gesture == m_Gesture && (from - m_WorldFrom).sqrMagnitude < 0.0001f && (to - m_WorldTo).sqrMagnitude < 0.0001f;
            m_Gesture = gesture;
            m_Camera = cam;
            m_WorldFrom = from;
            m_WorldTo = to;
            m_WorldStep = cellStep;
            if (same) return;
            m_Time = 0f;
            gameObject.SetActive(true);
        }

        /// <summary>Turns the world points into this frame's screen points. False when they are not on screen.</summary>
        bool Project()
        {
            if (m_Camera == null) return false;
            Vector3 a = m_Camera.WorldToScreenPoint(m_WorldFrom);
            Vector3 b = m_Camera.WorldToScreenPoint(m_WorldTo);
            Vector3 c = m_Camera.WorldToScreenPoint(m_WorldFrom + m_WorldStep);
            if (a.z <= 0f || b.z <= 0f || c.z <= 0f) return false;
            float cell = ((Vector2)(c - a)).magnitude;
            float shortSide = Mathf.Min(m_Camera.pixelWidth, m_Camera.pixelHeight);
            m_From = a;
            m_To = b;
            if (m_Gesture == Gesture.Swipe)
            {
                Vector2 along = (Vector2)(b - a);
                if (along.sqrMagnitude < 1f) return false;
                m_To = m_From + along.normalized * Mathf.Max(along.magnitude * 1.4f, shortSide * 0.12f);
            }
            m_Size = Mathf.Clamp(cell * 1.35f, shortSide * 0.1f, shortSide * 0.2f);
            return true;
        }

        public void Hide()
        {
            m_Gesture = Gesture.None;
            m_Alpha = 0f;
            if (m_Group != null) m_Group.alpha = 0f;
            gameObject.SetActive(false);
        }

        public bool Showing { get { return gameObject.activeSelf && m_Gesture != Gesture.None; } }

        // ---------------------------------------------------------------- animation

        void Update()
        {
            if (m_Gesture == Gesture.None) return;
            float dt = Time.unscaledDeltaTime;
            if (!Project())
            {
                m_Group.alpha = 0f;
                return;
            }

            // A finger on the screen: get out of the way, and start the gesture over once it lifts.
            Pointer pointer = Pointer.current;
            bool pressed = pointer != null && pointer.press.isPressed;
            if (pressed) m_Time = 0f;
            else if (m_WasPressed) m_Time = 0f;
            m_WasPressed = pressed;

            bool drag = m_Gesture == Gesture.Drag;
            float appear = 0.22f, press = 0.12f, move = drag ? 0.75f : 0.42f, lift = 0.12f, fade = 0.25f, rest = drag ? 0.45f : 0.4f;
            float cycle = appear + press + move + lift + fade + rest;
            m_Time += dt;
            float t = m_Time % cycle;

            // Phases: glide in above the start, press down, slide, lift, fade, wait.
            Vector2 pos;
            float down = 0f;        // 0 = hovering, 1 = pressed against the screen
            float visible;
            float tilt = 0f;
            if (t < appear)
            {
                float k = Smooth(t / appear);
                pos = m_From + new Vector2(m_Size * 0.18f, -m_Size * 0.22f) * (1f - k);
                visible = k;
            }
            else if (t < appear + press)
            {
                pos = m_From;
                down = Smooth((t - appear) / press);
                visible = 1f;
            }
            else if (t < appear + press + move)
            {
                float k = (t - appear - press) / move;
                pos = Vector2.Lerp(m_From, m_To, drag ? Smooth(k) : EaseOut(k));
                down = 1f;
                visible = 1f;
                tilt = Mathf.Sin(k * Mathf.PI) * 6f * Mathf.Sign(m_To.x - m_From.x + 0.001f);
            }
            else if (t < appear + press + move + lift)
            {
                pos = m_To;
                down = 1f - Smooth((t - appear - press - move) / lift);
                visible = 1f;
            }
            else
            {
                float k = Mathf.Clamp01((t - appear - press - move - lift) / fade);
                pos = m_To + new Vector2(m_Size * 0.1f, -m_Size * 0.12f) * Smooth(k);
                visible = 1f - Smooth(k);
            }

            m_Alpha = Mathf.MoveTowards(m_Alpha, pressed ? 0f : 1f, dt * (pressed ? 8f : 4f));
            m_Group.alpha = m_Alpha * visible;

            float scale = m_Canvas != null ? Mathf.Max(0.0001f, m_Canvas.scaleFactor) : 1f;
            float h = m_Size / scale;
            m_Hand.sizeDelta = new Vector2(h * m_Hand.sizeDelta.x / Mathf.Max(0.0001f, m_Hand.sizeDelta.y), h);
            m_Hand.anchoredPosition = pos / scale;
            float squash = 1f - 0.14f * down;
            m_Hand.localScale = new Vector3(squash, squash, 1f);
            m_Hand.localRotation = Quaternion.Euler(0f, 0f, tilt);

            if (m_Ring != null)
            {
                // The ring spreads from the press point; a drag leaves one where the wall is dropped too.
                float since = t - appear - press * 0.5f;
                float since2 = t - appear - press - move;
                Vector2 at = m_From;
                if (drag && since2 > 0f) { since = since2; at = m_To; }
                float k = Mathf.Clamp01(since / 0.45f);
                bool on = since > 0f && k < 1f;
                m_RingImage.enabled = on;
                if (on)
                {
                    float d = h * (0.25f + 0.55f * EaseOut(k));
                    m_Ring.sizeDelta = new Vector2(d, d);
                    m_Ring.anchoredPosition = at / scale;
                    m_RingImage.color = new Color(1f, 1f, 1f, 0.75f * (1f - k));
                }
            }
        }

        static float Smooth(float k)
        {
            k = Mathf.Clamp01(k);
            return k * k * (3f - 2f * k);
        }

        static float EaseOut(float k)
        {
            k = Mathf.Clamp01(k);
            return 1f - (1f - k) * (1f - k);
        }
    }
}
