using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ExpandPuzzle
{
    /// <summary>Makes a button dip under the finger and spring back. Tells the game about the press (for the click sound).</summary>
    public class PressScale : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public float pressedScale = 0.88f;

        /// <summary>Raised whenever any button with this component is pressed.</summary>
        public static event Action Pressed;

        bool m_Down;
        float m_Scale = 1f, m_Velocity;

        public static void AddTo(Component target)
        {
            if (target != null && target.GetComponent<PressScale>() == null) target.gameObject.AddComponent<PressScale>();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            m_Down = true;
            if (Pressed != null) Pressed();
        }

        public void OnPointerUp(PointerEventData eventData) { m_Down = false; }
        public void OnPointerExit(PointerEventData eventData) { m_Down = false; }

        void OnDisable()
        {
            m_Down = false;
            m_Scale = 1f;
            m_Velocity = 0f;
            transform.localScale = Vector3.one;
        }

        void Update()
        {
            // A small spring: quick dip, bouncy return.
            float dt = Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
            float target = m_Down ? pressedScale : 1f;
            m_Velocity += ((target - m_Scale) * 520f - m_Velocity * 22f) * dt;
            m_Scale += m_Velocity * dt;
            transform.localScale = new Vector3(m_Scale, m_Scale, 1f);
        }
    }
}
