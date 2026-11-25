using UnityEngine;
using UnityEngine.InputSystem;

namespace ExpandPuzzle
{
    /// <summary>
    /// Turns the on-screen joystick (and the keyboard, for testing in the editor) into grid steps.
    /// A push gives one step; holding repeats. Repeated steps are flagged so the game can
    /// require a fresh push for pumping.
    /// </summary>
    public class GameInput : MonoBehaviour
    {
        public Joystick joystick;

        [Tooltip("How far the stick must be pushed (0-1) to count as a step.")]
        public float threshold = 0.45f;
        [Tooltip("The stick must come back under this before the same direction counts as a new push.")]
        public float releaseThreshold = 0.25f;
        public float repeatDelay = 0.3f;
        public float repeatInterval = 0.14f;

        Vector2Int m_Held;
        float m_NextRepeat;

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

            Vector2Int current = ReadDirection(kb);

            if (current == Vector2Int.zero)
            {
                m_Held = Vector2Int.zero;
                return false;
            }

            if (current != m_Held)
            {
                m_Held = current;
                m_NextRepeat = Time.unscaledTime + repeatDelay;
                dir = current;
                return true;
            }

            if (Time.unscaledTime >= m_NextRepeat)
            {
                m_NextRepeat = Time.unscaledTime + repeatInterval;
                dir = current;
                isRepeat = true;
                return true;
            }

            return false;
        }

        Vector2Int ReadDirection(Keyboard kb)
        {
            if (kb != null)
            {
                if (kb.upArrowKey.isPressed || kb.wKey.isPressed) return Vector2Int.up;
                if (kb.downArrowKey.isPressed || kb.sKey.isPressed) return Vector2Int.down;
                if (kb.leftArrowKey.isPressed || kb.aKey.isPressed) return Vector2Int.left;
                if (kb.rightArrowKey.isPressed || kb.dKey.isPressed) return Vector2Int.right;
            }

            if (joystick != null)
            {
                Vector2 v = joystick.Direction;
                // Hysteresis: easy to keep holding, needs a clear release to push again.
                float limit = m_Held == Vector2Int.zero ? threshold : releaseThreshold;
                if (v.magnitude >= limit)
                {
                    Vector2Int d;
                    if (Mathf.Abs(v.x) > Mathf.Abs(v.y)) d = v.x > 0f ? Vector2Int.right : Vector2Int.left;
                    else d = v.y > 0f ? Vector2Int.up : Vector2Int.down;

                    // Do not flip direction on tiny diagonal jitter while holding.
                    if (m_Held != Vector2Int.zero && d != m_Held)
                    {
                        float heldAmount = v.x * m_Held.x + v.y * m_Held.y;
                        float newAmount = v.x * d.x + v.y * d.y;
                        if (newAmount < threshold || newAmount < heldAmount + 0.15f) return m_Held;
                    }
                    return d;
                }
            }

            return Vector2Int.zero;
        }
    }
}
