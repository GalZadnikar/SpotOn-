using UnityEngine;

namespace ExpandPuzzle
{
    /// <summary>
    /// Shrinks this RectTransform to the device safe area (notches, punch-hole cameras, rounded corners).
    /// Put HUD elements inside it. The camera framing reads the same rect so the board avoids the HUD too.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class SafeAreaFitter : MonoBehaviour
    {
        [Tooltip("Editor only: pretend there is a camera cut-out of this many pixels at the top, to test the layout.")]
        public float simulateTopInsetPx = 0f;

        static float s_SimulatedTop;

        RectTransform m_Rect;
        Rect m_Applied;
        Vector2Int m_Screen;

        /// <summary>The safe area in screen pixels (including the editor simulation).</summary>
        public static Rect SafeRect
        {
            get
            {
                Rect r = Screen.safeArea;
                if (Application.isEditor && s_SimulatedTop > 0f)
                    r.yMax = Mathf.Min(r.yMax, Screen.height - s_SimulatedTop);
                return r;
            }
        }

        void Awake()
        {
            m_Rect = GetComponent<RectTransform>();
            Apply();
        }

        void Update()
        {
            s_SimulatedTop = Application.isEditor ? simulateTopInsetPx : 0f;
            Rect safe = SafeRect;
            if (safe != m_Applied || Screen.width != m_Screen.x || Screen.height != m_Screen.y) Apply();
        }

        void Apply()
        {
            s_SimulatedTop = Application.isEditor ? simulateTopInsetPx : 0f;
            Rect safe = SafeRect;
            m_Applied = safe;
            m_Screen = new Vector2Int(Screen.width, Screen.height);
            if (Screen.width <= 0 || Screen.height <= 0) return;

            m_Rect.anchorMin = new Vector2(safe.xMin / Screen.width, safe.yMin / Screen.height);
            m_Rect.anchorMax = new Vector2(safe.xMax / Screen.width, safe.yMax / Screen.height);
            m_Rect.offsetMin = Vector2.zero;
            m_Rect.offsetMax = Vector2.zero;
        }
    }
}
