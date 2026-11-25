using UnityEngine;

namespace ExpandPuzzle
{
    /// <summary>
    /// Keeps the whole board in view for any screen shape. It moves <see cref="rig"/>
    /// (the Cinemachine camera, or the main camera when there is none).
    /// </summary>
    public class CameraFramer : MonoBehaviour
    {
        [Tooltip("Transform that gets positioned. Usually the CinemachineCamera.")]
        public Transform rig;
        [Tooltip("Camera used for field of view and aspect. Defaults to the main camera.")]
        public Camera cam;

        [Range(30f, 90f)] public float pitch = 68f;
        public float yaw = 0f;
        public float fieldOfView = 30f;

        [Header("Free space around the board (fraction of the screen)")]
        [Range(0f, 0.4f)] public float padSides = 0.035f;
        [Range(0f, 0.4f)] public float padTop = 0.14f;
        [Range(0f, 0.4f)] public float padBottom = 0.1f;

        public float smoothTime = 0.35f;

        [Header("Mobile")]
        [Tooltip("Keep the board clear of notches / camera cut-outs (uses the same safe area as the HUD).")]
        public bool respectSafeArea = true;
        [Tooltip("Fit the shadow distance to the board every level: sharper shadows from the same shadow map.")]
        public bool fitShadowDistance = true;

        float m_Punch, m_PunchVel;

        Bounds m_Bounds = new Bounds(Vector3.zero, new Vector3(8f, 1f, 6f));
        Vector3 m_TargetPos;
        Vector3 m_Smoothed;
        Vector3 m_Velocity;
        bool m_HasTarget;
        int m_LastW, m_LastH;
        Rect m_LastSafe;

        [Tooltip("How far (fraction of half the screen) the board may slide off-centre to keep a pump fully on screen.")]
        [Range(0f, 0.5f)] public float maxOffCenter = 0.15f;

        readonly System.Collections.Generic.List<Bounds> m_KeepVisible = new System.Collections.Generic.List<Bounds>();
        static readonly System.Collections.Generic.List<Vector3> s_Points = new System.Collections.Generic.List<Vector3>();

        public void Frame(Bounds bounds, bool instant)
        {
            Frame(bounds, null, instant);
        }

        /// <param name="board">What the shot is built around: centred and as big as possible.</param>
        /// <param name="keepVisible">Extra things that must stay fully on screen (pumps). They may nudge the board slightly off-centre.</param>
        public void Frame(Bounds board, System.Collections.Generic.IList<Bounds> keepVisible, bool instant)
        {
            m_Bounds = board;
            m_KeepVisible.Clear();
            if (keepVisible != null) m_KeepVisible.AddRange(keepVisible);
            Recalculate();
            if (instant || !m_HasTarget) Apply(m_TargetPos);
            m_HasTarget = true;
        }

        static void AddCorners(Bounds b)
        {
            Vector3 c = b.center, e = b.extents;
            for (int k = 0; k < 8; k++)
                s_Points.Add(c + new Vector3((k & 1) == 0 ? -e.x : e.x, (k & 2) == 0 ? -e.y : e.y, (k & 4) == 0 ? -e.z : e.z));
        }

        void Recalculate()
        {
            if (cam == null) cam = Camera.main;
            if (rig == null && cam != null) rig = cam.transform;

            float aspect = cam != null ? cam.aspect : 0.5625f;
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            Quaternion inv = Quaternion.Inverse(rot);
            float tanV = Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad);
            float tanH = tanV * aspect;

            float top, bottom;
            Pads(out top, out bottom);

            // The view is not centred on the board when top and bottom padding differ.
            float limitSide = tanH * (1f - 2f * padSides);
            float limitUp = tanV * (1f - 2f * top);
            float limitDown = tanV * (1f - 2f * bottom);

            s_Points.Clear();
            AddCorners(m_Bounds);
            for (int i = 0; i < m_KeepVisible.Count; i++) AddCorners(m_KeepVisible[i]);

            Vector3 c = m_Bounds.center;
            Vector3 forward = rot * Vector3.forward;
            Vector3 right = rot * Vector3.right;
            Vector3 up = rot * Vector3.up;

            // Smallest distance at which everything fits, looking at the board's centre.
            // Sliding the camera sideways / up only moves points across the screen (their depth stays),
            // so for each distance the allowed slide is an exact interval; prefer no slide at all.
            float lo = 1f, hi = 200f, dx = 0f, dy = 0f;
            for (int i = 0; i < 30; i++)
            {
                float mid = (lo + hi) * 0.5f;
                float sx, sy;
                if (Fits(c, forward, inv, mid, limitSide, limitUp, limitDown, out sx, out sy)) hi = mid; else lo = mid;
            }
            Fits(c, forward, inv, hi, limitSide, limitUp, limitDown, out dx, out dy);

            m_TargetPos = c - forward * hi + right * dx + up * dy;
            m_LastW = Screen.width;
            m_LastH = Screen.height;
            m_LastSafe = SafeAreaFitter.SafeRect;

            if (fitShadowDistance)
            {
                // Just past the far corner of the board: the whole shadow map is spent on what is on screen.
                // URP fades shadows over the last part of the distance (the cascade border), so cover that too.
                float far = 0f;
                for (int k = 0; k < s_Points.Count; k++)
                    far = Mathf.Max(far, Vector3.Distance(s_Points[k], m_TargetPos));
                var urp = UnityEngine.Rendering.Universal.UniversalRenderPipeline.asset;
                if (urp != null)
                {
                    float fade = Mathf.Clamp(urp.cascadeBorder, 0f, 0.5f);
                    urp.shadowDistance = Mathf.Ceil(far / (1f - fade) + 1f);
                }
            }
        }

        bool Fits(Vector3 c, Vector3 forward, Quaternion inv, float dist, float limitSide, float limitUp, float limitDown,
            out float slideX, out float slideY)
        {
            slideX = slideY = 0f;
            Vector3 pos = c - forward * dist;
            float xLo = float.MinValue, xHi = float.MaxValue, yLo = float.MinValue, yHi = float.MaxValue;
            for (int k = 0; k < s_Points.Count; k++)
            {
                Vector3 local = inv * (s_Points[k] - pos);
                if (local.z <= 0.01f) return false;
                xLo = Mathf.Max(xLo, local.x - limitSide * local.z);
                xHi = Mathf.Min(xHi, local.x + limitSide * local.z);
                yLo = Mathf.Max(yLo, local.y - limitUp * local.z);
                yHi = Mathf.Min(yHi, local.y + limitDown * local.z);
            }

            // Keep the board close to the middle: only a small slide is allowed.
            float maxX = maxOffCenter * limitSide * dist;
            float maxY = maxOffCenter * Mathf.Max(limitUp, limitDown) * dist;
            xLo = Mathf.Max(xLo, -maxX); xHi = Mathf.Min(xHi, maxX);
            yLo = Mathf.Max(yLo, -maxY); yHi = Mathf.Min(yHi, maxY);
            if (xLo > xHi || yLo > yHi) return false;

            slideX = Mathf.Clamp(0f, xLo, xHi);
            slideY = Mathf.Clamp(0f, yLo, yHi);
            return true;
        }

        /// <summary>Top and bottom padding as fractions of the screen height, safe area included.</summary>
        void Pads(out float top, out float bottom)
        {
            top = padTop;
            bottom = padBottom;
            if (!respectSafeArea || Screen.height <= 0) return;
            Rect safe = SafeAreaFitter.SafeRect;
            top += (Screen.height - safe.yMax) / Screen.height * 0.5f;
            bottom += safe.yMin / Screen.height * 0.5f;
        }

        /// <summary>
        /// Rough on-screen size of a board footprint (grid units) at this camera setup. Only used to compare
        /// layouts, e.g. to pick the level orientation that shows the board biggest.
        /// </summary>
        public float EstimateScale(Vector2 footprint)
        {
            if (cam == null) cam = Camera.main;
            float aspect = cam != null ? cam.aspect : 0.5625f;
            float top, bottom;
            Pads(out top, out bottom);
            float availW = aspect * (1f - 2f * padSides);
            float availH = 1f - top - bottom;
            float s = Mathf.Sin(pitch * Mathf.Deg2Rad);
            float h = footprint.y * s + 0.7f * Mathf.Cos(pitch * Mathf.Deg2Rad);
            return Mathf.Min(availW / Mathf.Max(0.01f, footprint.x), availH / Mathf.Max(0.01f, h));
        }

        void Apply(Vector3 pos)
        {
            if (rig == null) return;
            m_Smoothed = pos;
            rig.position = pos;
            rig.rotation = Quaternion.Euler(pitch, yaw, 0f);
            if (cam != null && rig == cam.transform) cam.fieldOfView = fieldOfView;
            m_Velocity = Vector3.zero;
        }

        /// <summary>A quick zoom kick that springs back. Positive = towards the board.</summary>
        public void Punch(float amount)
        {
            m_PunchVel += amount * 22f;
        }

        void LateUpdate()
        {
            if (!m_HasTarget || rig == null) return;
            if (Screen.width != m_LastW || Screen.height != m_LastH || SafeAreaFitter.SafeRect != m_LastSafe) Recalculate();

            float dt = Mathf.Min(Time.deltaTime, 1f / 30f);
            m_PunchVel += (-180f * m_Punch - 14f * m_PunchVel) * dt;
            m_Punch += m_PunchVel * dt;

            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            m_Smoothed = Vector3.SmoothDamp(m_Smoothed, m_TargetPos, ref m_Velocity, smoothTime);
            rig.position = m_Smoothed + rot * Vector3.forward * m_Punch;
            rig.rotation = rot;
        }
    }
}
