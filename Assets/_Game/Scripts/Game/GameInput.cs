using UnityEngine;
using UnityEngine.InputSystem;

namespace ExpandPuzzle
{
    /// <summary>
    /// Turns touch (and the keyboard, for testing in the editor) into grid steps.
    /// Two touch schemes, switched with <see cref="SwipeMode"/>:
    ///  - Joystick: a push gives one step; holding repeats at an even, countable pace.
    ///  - Swipe: a swipe anywhere gives one step; keep dragging and every further cell of
    ///    finger travel gives one more, so the number of steps is set by distance, not timing.
    /// Steps that continue an earlier one are flagged as repeats so the game can require a
    /// fresh push for pumping.
    /// </summary>
    public class GameInput : MonoBehaviour
    {
        const string ModeKey = "ExpandPuzzle.SwipeControls";
        static int s_Swipe = -1;

        /// <summary>The player's control scheme (remembered between sessions). Swipe is the default.</summary>
        public static bool SwipeMode
        {
            get
            {
                if (s_Swipe < 0) s_Swipe = PlayerPrefs.GetInt(ModeKey, 1);
                return s_Swipe == 1;
            }
            set
            {
                s_Swipe = value ? 1 : 0;
                PlayerPrefs.SetInt(ModeKey, s_Swipe);
                PlayerPrefs.Save();
            }
        }

        public Joystick joystick;

        [Header("Joystick")]
        [Tooltip("How far the stick must be pushed (0-1) to count as a step.")]
        public float pushThreshold = 0.35f;
        [Tooltip("The stick must come back under this before the same direction counts as a new push.")]
        public float letGoThreshold = 0.2f;
        [Tooltip("Holding: wait before the second step. Close to Hold Interval so the rhythm is even and easy to count.")]
        public float holdDelay = 0.24f;
        [Tooltip("Holding: time between the following steps.")]
        public float holdInterval = 0.2f;
        [Tooltip("Holding: after this many steps the pace picks up for long runs (0 = never).")]
        public int fastAfterSteps = 4;
        public float fastInterval = 0.12f;

        [Header("Swipe")]
        [Tooltip("Finger travel (cm) for the first step of a touch. Short, so a flick is enough.")]
        public float swipeFirstStepCm = 0.4f;
        [Tooltip("Each further step takes one cell of finger travel as seen on screen, kept between these (cm).")]
        public float swipeMinStepCm = 0.6f;
        public float swipeMaxStepCm = 1.1f;
        [Tooltip("Shortest time between two steps of one drag. A quick flick is over before this, so it stays one step.")]
        public float swipeStepInterval = 0.11f;

        /// <summary>Size of one board cell on screen in pixels (x = sideways, y = up). Set by the game every frame.</summary>
        [System.NonSerialized] public Vector2 cellPixels;

        // keyboard / joystick
        Vector2Int m_Held;
        int m_HeldSteps;
        float m_NextRepeat;

        // swipe
        SwipePad m_Pad;
        int m_TouchSerial;
        bool m_SwipeLive;
        Vector2 m_Anchor;
        Vector2Int m_SwipeDir;
        int m_SwipeSteps;
        float m_NextSwipe;

        bool m_ModeApplied, m_AppliedSwipe;

        public bool UndoPressed { get; private set; }
        public bool RestartPressed { get; private set; }
        public bool SkipPressed { get; private set; }
        public bool BackPressed { get; private set; }
        public bool ToggleFxPressed { get; private set; }

        /// <summary>Call once per frame. Returns true when the blob should take a step.</summary>
        public bool TryGetStep(out Vector2Int dir, out bool isRepeat)
        {
            dir = Vector2Int.zero;
            isRepeat = false;

            var kb = Keyboard.current;
            UndoPressed = kb != null && (kb.zKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame);
            RestartPressed = kb != null && kb.rKey.wasPressedThisFrame;
            SkipPressed = kb != null && kb.nKey.wasPressedThisFrame;
            BackPressed = kb != null && kb.bKey.wasPressedThisFrame;
            ToggleFxPressed = kb != null && kb.tKey.wasPressedThisFrame;

            ApplyMode();

            // The keyboard works in both modes.
            Vector2Int current = ReadKeyboard(kb);
            bool swipe = m_AppliedSwipe && m_Pad != null;
            if (current == Vector2Int.zero && !swipe) current = ReadJoystick();

            if (current != Vector2Int.zero || !swipe) return HoldStep(current, out dir, out isRepeat);

            m_Held = Vector2Int.zero;
            return SwipeStep(out dir, out isRepeat);
        }

        // ---------------------------------------------------------------- mode

        void ApplyMode()
        {
            bool swipe = SwipeMode;
            if (m_ModeApplied && swipe == m_AppliedSwipe) return;
            m_ModeApplied = true;
            m_AppliedSwipe = swipe;

            if (swipe && m_Pad == null) m_Pad = CreatePad();
            if (m_Pad != null) m_Pad.gameObject.SetActive(swipe);
            // Without a pad (no canvas) the joystick stays, so the game is never left without touch controls.
            if (joystick != null) joystick.gameObject.SetActive(!swipe || m_Pad == null);

            m_Held = Vector2Int.zero;
            m_SwipeLive = false;
            if (m_Pad != null) m_TouchSerial = m_Pad.TouchSerial;
        }

        /// <summary>The swipe surface sits where the joystick sits: under the HUD, so buttons stay clickable.</summary>
        SwipePad CreatePad()
        {
            Transform parent;
            int index = 0;
            if (joystick != null && joystick.transform.parent != null)
            {
                parent = joystick.transform.parent;
                index = joystick.transform.GetSiblingIndex() + 1;
            }
            else
            {
                var canvas = FindFirstObjectByType<Canvas>();
                if (canvas == null) return null;
                parent = canvas.transform;
            }

            var go = new GameObject("Swipe Pad", typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            go.transform.SetSiblingIndex(index);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return go.AddComponent<SwipePad>();
        }

        // ---------------------------------------------------------------- keyboard / joystick

        bool HoldStep(Vector2Int current, out Vector2Int dir, out bool isRepeat)
        {
            dir = Vector2Int.zero;
            isRepeat = false;

            if (current == Vector2Int.zero)
            {
                m_Held = Vector2Int.zero;
                return false;
            }

            if (current != m_Held)
            {
                m_Held = current;
                m_HeldSteps = 1;
                m_NextRepeat = Time.unscaledTime + holdDelay;
                dir = current;
                return true;
            }

            if (Time.unscaledTime >= m_NextRepeat)
            {
                m_HeldSteps++;
                bool fast = fastAfterSteps > 0 && m_HeldSteps >= fastAfterSteps;
                m_NextRepeat = Time.unscaledTime + (fast ? fastInterval : holdInterval);
                dir = current;
                isRepeat = true;
                return true;
            }

            return false;
        }

        static Vector2Int ReadKeyboard(Keyboard kb)
        {
            if (kb == null) return Vector2Int.zero;
            if (kb.upArrowKey.isPressed || kb.wKey.isPressed) return Vector2Int.up;
            if (kb.downArrowKey.isPressed || kb.sKey.isPressed) return Vector2Int.down;
            if (kb.leftArrowKey.isPressed || kb.aKey.isPressed) return Vector2Int.left;
            if (kb.rightArrowKey.isPressed || kb.dKey.isPressed) return Vector2Int.right;
            return Vector2Int.zero;
        }

        Vector2Int ReadJoystick()
        {
            if (joystick == null || !joystick.isActiveAndEnabled) return Vector2Int.zero;

            Vector2 v = joystick.Direction;
            // Hysteresis: easy to keep holding, needs a clear release to push again.
            float limit = m_Held == Vector2Int.zero ? pushThreshold : letGoThreshold;
            if (v.magnitude < limit) return Vector2Int.zero;

            Vector2Int d;
            if (Mathf.Abs(v.x) > Mathf.Abs(v.y)) d = v.x > 0f ? Vector2Int.right : Vector2Int.left;
            else d = v.y > 0f ? Vector2Int.up : Vector2Int.down;

            // Do not flip direction on tiny diagonal jitter while holding.
            if (m_Held != Vector2Int.zero && d != m_Held)
            {
                float heldAmount = v.x * m_Held.x + v.y * m_Held.y;
                float newAmount = v.x * d.x + v.y * d.y;
                if (newAmount < pushThreshold || newAmount < heldAmount + 0.15f) return m_Held;
            }
            return d;
        }

        // ---------------------------------------------------------------- swipe

        bool SwipeStep(out Vector2Int dir, out bool isRepeat)
        {
            dir = Vector2Int.zero;
            isRepeat = false;

            if (m_Pad.TouchSerial != m_TouchSerial)
            {
                // A new finger went down.
                m_TouchSerial = m_Pad.TouchSerial;
                m_Anchor = m_Pad.StartPosition;
                m_SwipeDir = Vector2Int.zero;
                m_SwipeSteps = 0;
                m_SwipeLive = true;
            }
            if (!m_SwipeLive) return false;

            bool touching = m_Pad.Touching;
            // Finger lifted: whatever distance was still left over is dropped, so a long quick
            // flick is one step and the blob never keeps walking after the finger is gone.
            // (A flick that was down and up within one frame still gets its first step below.)
            if (!touching && m_SwipeSteps > 0)
            {
                m_SwipeLive = false;
                return false;
            }
            if (m_SwipeSteps > 0 && Time.unscaledTime < m_NextSwipe) return false;

            Vector2 pos = m_Pad.Position;
            Vector2 delta = pos - m_Anchor;
            float needX = StepPixels(cellPixels.x);
            float needY = StepPixels(cellPixels.y);
            float px = Mathf.Abs(delta.x) / needX;
            float py = Mathf.Abs(delta.y) / needY;
            if (Mathf.Max(px, py) < 1f)
            {
                if (!touching) m_SwipeLive = false;
                return false;
            }

            // The finger is measured from where the last step was taken, so a drag can turn a
            // corner. Sideways drift is forgotten at every step, so a slanted drag stays straight.
            Vector2Int d;
            if (px >= py)
            {
                d = delta.x > 0f ? Vector2Int.right : Vector2Int.left;
                m_Anchor.x += d.x * needX;
                m_Anchor.y = pos.y;
            }
            else
            {
                d = delta.y > 0f ? Vector2Int.up : Vector2Int.down;
                m_Anchor.y += d.y * needY;
                m_Anchor.x = pos.x;
            }

            isRepeat = m_SwipeSteps > 0 && d == m_SwipeDir;
            m_SwipeDir = d;
            m_SwipeSteps++;
            m_NextSwipe = Time.unscaledTime + swipeStepInterval;
            if (!touching) m_SwipeLive = false;

            dir = d;
            return true;
        }

        /// <summary>Finger travel in pixels for the next step of the current touch.</summary>
        float StepPixels(float cell)
        {
            float cm = PixelsPerCm();
            if (m_SwipeSteps == 0) return Mathf.Max(1f, swipeFirstStepCm * cm);
            float lo = swipeMinStepCm * cm;
            float hi = Mathf.Max(lo, swipeMaxStepCm * cm);
            if (cell <= 0f) cell = 0.5f * (lo + hi);
            return Mathf.Max(1f, Mathf.Clamp(cell, lo, hi));
        }

        static float PixelsPerCm()
        {
            // Phones report their real density. In the editor and on desktop the window can be any
            // size, so there the screen is treated as a phone about 6.5 cm wide.
            if (Application.isMobilePlatform && Screen.dpi > 1f) return Screen.dpi / 2.54f;
            return Mathf.Min(Screen.width, Screen.height) / 6.5f;
        }
    }
}
