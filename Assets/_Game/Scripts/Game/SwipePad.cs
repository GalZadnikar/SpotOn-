using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ExpandPuzzle
{
    /// <summary>
    /// An invisible, screen-filling touch surface for swipe controls. It is a normal UI element,
    /// so buttons drawn on top of it keep their taps and an open menu blocks it, exactly like the
    /// joystick it stands in for. It only reports where the finger is; <see cref="GameInput"/>
    /// turns that into steps.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class SwipePad : Graphic, IPointerDownHandler, IPointerUpHandler, IDragHandler, IInitializePotentialDragHandler
    {
        /// <summary>True while the finger that started the current swipe is down.</summary>
        public bool Touching { get; private set; }
        /// <summary>Where the current (or last) touch began, in screen pixels.</summary>
        public Vector2 StartPosition { get; private set; }
        /// <summary>Where that finger is now (or was when it lifted), in screen pixels.</summary>
        public Vector2 Position { get; private set; }
        /// <summary>Goes up by one for every new touch, so a reader can tell touches apart.</summary>
        public int TouchSerial { get; private set; }

        int m_PointerId;

        // Takes touches but draws nothing.
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
        }

        public void OnInitializePotentialDrag(PointerEventData eventData)
        {
            // Report movement from the first pixel instead of after Unity's drag threshold.
            eventData.useDragThreshold = false;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (Touching) return;   // a second finger does not disturb the swipe in progress
            m_PointerId = eventData.pointerId;
            Touching = true;
            StartPosition = Position = eventData.position;
            TouchSerial++;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (Touching && eventData.pointerId == m_PointerId) Position = eventData.position;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!Touching || eventData.pointerId != m_PointerId) return;
            Position = eventData.position;
            Touching = false;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            Touching = false;
        }
    }
}
