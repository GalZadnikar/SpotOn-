using System.Collections.Generic;
using UnityEngine;

namespace ExpandPuzzle
{
    /// <summary>What to draw on top of one blob cell.</summary>
    public struct BadgeInfo
    {
        public Vector2Int cell;
        public CellStatus status;
        /// <summary>Colour the tile underneath wants (only for <see cref="CellStatus.TargetWrongColor"/>).</summary>
        public int needColor;
        /// <summary>Colour of the blob cell itself, so the mark can be pressed into it.</summary>
        public int cellColor;
    }

    /// <summary>
    /// The player: a jelly blob made of grid cells, with a face on its head cell.
    /// Game logic is instant; this class only makes it look alive (glide, squash, wobble, blink)
    /// and shows what each cell is standing on (check, wrong colour, forbidden).
    /// </summary>
    public class BlobView : MonoBehaviour
    {
        [Header("Shape")]
        public float height = 0.62f;
        public float margin = 0.05f;
        public float cornerRadius = 0.24f;
        public float bevel = 0.14f;

        [Header("Feel")]
        public float glideSpeed = 24f;
        public float springStiffness = 240f;
        public float springDamping = 13f;

        class Badge
        {
            public Transform root;
            public GameObject check;
            public GameObject cross;
            public GameObject dot;
            public readonly List<MeshRenderer> groove = new List<MeshRenderer>();
            public readonly List<MeshRenderer> edge = new List<MeshRenderer>();
            public MeshRenderer dotInner;
            public Vector2Int rel;
            public CellStatus status;
            public int needColor;
            public int cellColor;
            public float age;
            public bool used;
        }

        GameTheme m_Theme;
        Transform m_Pivot;
        Transform m_MeshRoot;
        Transform m_Face;
        Transform m_EyeL, m_EyeR;
        Transform m_BadgeRoot;
        MeshFilter m_Filter;
        MeshRenderer m_Renderer;
        Mesh m_Mesh;

        readonly HashSet<Vector2Int> m_Rel = new HashSet<Vector2Int>();
        readonly Dictionary<Vector2Int, int> m_RelColor = new Dictionary<Vector2Int, int>();
        readonly List<Badge> m_Badges = new List<Badge>();
        Vector2Int m_Head;

        Vector3 m_Target;
        Vector3 m_Pos;
        Vector3 m_Scale, m_ScaleVel;     // spring, offset from (1,1,1)
        Vector3 m_Nudge, m_NudgeVel;     // spring, bump against walls
        float m_HopY, m_HopVel;
        int m_HopsLeft;

        Vector3 m_Look, m_LookTarget;
        float m_LookTimer;
        float m_BlinkTimer = 2f;
        float m_BlinkTime;
        float m_SquintTime;
        float m_WideTime;
        bool m_Worried;
        bool m_HeadHasBadge;
        float m_FaceZ = 0.02f;

        static readonly Vector3 EyeSize = new Vector3(0.15f, 0.07f, 0.2f);

        /// <summary>World position of the middle of the blob's top.</summary>
        public Vector3 TopCenter { get { return m_Pivot.position + Vector3.up * height; } }

        public void Init(GameTheme theme, Dictionary<Vector2Int, int> cells, Vector2Int head)
        {
            m_Theme = theme;

            var pivot = new GameObject("Pivot");
            m_Pivot = pivot.transform;
            m_Pivot.SetParent(transform, false);

            var meshGo = new GameObject("Body");
            m_MeshRoot = meshGo.transform;
            m_MeshRoot.SetParent(m_Pivot, false);
            m_Filter = meshGo.AddComponent<MeshFilter>();
            m_Renderer = meshGo.AddComponent<MeshRenderer>();
            m_Renderer.sharedMaterials = theme.BlobMaterials;
            m_Mesh = new Mesh();
            m_Mesh.name = "Blob";
            m_Mesh.MarkDynamic();
            m_Filter.sharedMesh = m_Mesh;

            var face = new GameObject("Face");
            m_Face = face.transform;
            m_Face.SetParent(m_MeshRoot, false);
            m_EyeL = MakeEye("EyeL", -0.155f, theme);
            m_EyeR = MakeEye("EyeR", 0.155f, theme);

            var badges = new GameObject("Badges");
            m_BadgeRoot = badges.transform;
            m_BadgeRoot.SetParent(m_MeshRoot, false);

            Snap(cells, head);
        }

        Transform MakeEye(string name, float x, GameTheme theme)
        {
            Transform eye = Primitive(PrimitiveType.Sphere, name, m_Face, theme.eye);
            eye.localPosition = new Vector3(x, 0f, 0f);
            eye.localScale = EyeSize;

            Transform shine = Primitive(PrimitiveType.Sphere, "Shine", eye, theme.eyeShine);
            shine.localPosition = new Vector3(0.18f, 0.3f, 0.2f);
            shine.localScale = new Vector3(0.34f, 0.5f, 0.3f);
            return eye;
        }

        static Transform Primitive(PrimitiveType type, string name, Transform parent, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            BoardView.StripCollider(go);
            go.transform.SetParent(parent, false);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go.transform;
        }

        // ---------------------------------------------------------------- shape

        void SetShape(Dictionary<Vector2Int, int> cells, Vector2Int head)
        {
            m_Head = head;
            m_Rel.Clear();
            m_RelColor.Clear();
            Vector2Int min = Vector2Int.zero, max = Vector2Int.zero;
            foreach (var c in cells)
            {
                var r = c.Key - head;
                m_Rel.Add(r);
                m_RelColor[r] = c.Value;
                min = new Vector2Int(Mathf.Min(min.x, r.x), Mathf.Min(min.y, r.y));
                max = new Vector2Int(Mathf.Max(max.x, r.x), Mathf.Max(max.y, r.y));
            }

            var s = CellMesh.Settings.Create(margin, cornerRadius, 0f, height, bevel, true);
            s.cornerSegments = 8;
            s.bevelSegments = 5;
            CellMesh.Build(m_Rel, s, m_Mesh, m_RelColor, m_Theme.ColorCount);

            // Scale around the middle of the shape so squash and stretch stay centred.
            Vector3 center = new Vector3((min.x + max.x) * 0.5f, 0f, (min.y + max.y) * 0.5f);
            m_Pivot.localPosition = center;
            m_MeshRoot.localPosition = -center;
        }

        /// <summary>Jump straight to a state (level start, undo).</summary>
        public void Snap(Dictionary<Vector2Int, int> cells, Vector2Int head)
        {
            SetShape(cells, head);
            m_Target = BoardView.CellToLocal(head);
            m_Pos = m_Target;
            transform.localPosition = m_Pos;
            m_ScaleVel += new Vector3(1.2f, -1.6f, 1.2f);
        }

        /// <summary>Falls onto the board from above (level start).</summary>
        public void DropIn()
        {
            m_HopY = 2.2f;
            m_HopVel = -1f;
            m_HopsLeft = 0;
            m_WideTime = 0.9f;
        }

        public void MoveTo(Vector2Int head, Vector2Int dir)
        {
            m_Head = head;
            m_Target = BoardView.CellToLocal(head);
            Vector3 along = new Vector3(Mathf.Abs(dir.x), 0f, Mathf.Abs(dir.y));
            Vector3 across = new Vector3(1f, 0f, 1f) - along;
            m_ScaleVel += along * 2.4f - across * 1.5f - Vector3.up * 0.5f;
            LookAt(dir);
        }

        public void Grow(Dictionary<Vector2Int, int> cells, Vector2Int head, Vector2Int dir)
        {
            SetShape(cells, head);
            Vector3 along = new Vector3(Mathf.Abs(dir.x), 0f, Mathf.Abs(dir.y));
            m_ScaleVel += new Vector3(2.6f, 3.4f, 2.6f) + along * 1.6f;
            m_SquintTime = 0.22f;
            m_WideTime = 0.6f;
            LookAt(dir);
        }

        /// <summary>Walked into something solid.</summary>
        public void Bump(Vector2Int dir)
        {
            Vector3 d = new Vector3(dir.x, 0f, dir.y);
            Vector3 along = new Vector3(Mathf.Abs(dir.x), 0f, Mathf.Abs(dir.y));
            Vector3 across = new Vector3(1f, 0f, 1f) - along;
            m_NudgeVel += d * 2.6f;
            m_ScaleVel += -along * 2.2f + across * 1.1f + Vector3.up * 0.8f;
            m_SquintTime = 0.18f;
            LookAt(dir);
        }

        /// <summary>The "click" when the last tile is covered.</summary>
        public void Settle()
        {
            m_ScaleVel += new Vector3(2.2f, -4.2f, 2.2f);
            m_SquintTime = 0.25f;
        }

        public void Celebrate()
        {
            m_HopsLeft = 3;
            m_HopVel = 5.6f;
            m_WideTime = 2f;
            m_ScaleVel += new Vector3(-1.5f, 3f, -1.5f);
        }

        void LookAt(Vector2Int dir)
        {
            m_LookTarget = new Vector3(dir.x, 0f, dir.y) * 0.075f;
            m_LookTimer = 0.9f;
        }

        // ---------------------------------------------------------------- badges

        /// <summary>Shows what each cell stands on. Cells listed as <see cref="CellStatus.Plain"/> get nothing.</summary>
        public void SetBadges(List<BadgeInfo> infos)
        {
            for (int i = 0; i < m_Badges.Count; i++) m_Badges[i].used = false;
            m_Worried = false;
            m_HeadHasBadge = false;

            for (int i = 0; i < infos.Count; i++)
            {
                BadgeInfo info = infos[i];
                if (info.status == CellStatus.Plain) continue;
                Vector2Int rel = info.cell - m_Head;
                if (info.status == CellStatus.Forbidden) m_Worried = true;
                if (rel == Vector2Int.zero) m_HeadHasBadge = true;

                // Keep a badge that already shows the same thing on the same cell (no re-pop).
                Badge badge = null;
                for (int b = 0; b < m_Badges.Count; b++)
                {
                    Badge c = m_Badges[b];
                    if (!c.used && c.root.gameObject.activeSelf && c.rel == rel && c.status == info.status
                        && c.needColor == info.needColor && c.cellColor == info.cellColor)
                    {
                        badge = c;
                        break;
                    }
                }

                if (badge == null)
                {
                    for (int b = 0; b < m_Badges.Count; b++)
                        if (!m_Badges[b].used && !m_Badges[b].root.gameObject.activeSelf) { badge = m_Badges[b]; break; }
                    if (badge == null)
                    {
                        badge = CreateBadge();
                        m_Badges.Add(badge);
                    }
                    Configure(badge, rel, info);
                }
                badge.used = true;
            }

            for (int i = 0; i < m_Badges.Count; i++)
                if (!m_Badges[i].used) m_Badges[i].root.gameObject.SetActive(false);
        }

        /// <summary>Replays the pop of the badge on a cell (used by the win sequence).</summary>
        public void PopBadge(Vector2Int cell)
        {
            Vector2Int rel = cell - m_Head;
            for (int i = 0; i < m_Badges.Count; i++)
                if (m_Badges[i].root.gameObject.activeSelf && m_Badges[i].rel == rel) m_Badges[i].age = 0f;
        }

        Badge CreateBadge()
        {
            var b = new Badge();
            var root = new GameObject("Mark");
            b.root = root.transform;
            b.root.SetParent(m_BadgeRoot, false);

            // Check: pressed into the jelly in the cell's own colour (colour is reserved for gameplay).
            var check = new GameObject("Check");
            check.transform.SetParent(b.root, false);
            Vector2 c0 = new Vector2(-0.16f, 0.01f), c1 = new Vector2(-0.055f, -0.105f), c2 = new Vector2(0.17f, 0.13f);
            Vector2 lit = new Vector2(0.014f, -0.016f);   // the edge that catches the light
            Stroke(check.transform, c0 + lit, c1 + lit, 0.092f, 0.003f, null, b.edge);
            Stroke(check.transform, c1 + lit, c2 + lit, 0.092f, 0.003f, null, b.edge);
            Stroke(check.transform, c0, c1, 0.092f, 0.007f, null, b.groove);
            Stroke(check.transform, c1, c2, 0.092f, 0.007f, null, b.groove);
            b.check = check;

            // Cross: pressed into the jelly - dark groove with a light far edge.
            var cross = new GameObject("Cross");
            cross.transform.SetParent(b.root, false);
            Vector2 x0 = new Vector2(-0.125f, -0.125f), x1 = new Vector2(0.125f, 0.125f);
            Vector2 x2 = new Vector2(-0.125f, 0.125f), x3 = new Vector2(0.125f, -0.125f);
            Stroke(cross.transform, x0 + lit, x1 + lit, 0.092f, 0.003f, null, b.edge);
            Stroke(cross.transform, x2 + lit, x3 + lit, 0.092f, 0.003f, null, b.edge);
            Stroke(cross.transform, x0, x1, 0.092f, 0.007f, null, b.groove);
            Stroke(cross.transform, x2, x3, 0.092f, 0.007f, null, b.groove);
            b.cross = cross;

            // Wrong colour: a little socket showing the colour the tile wants.
            var dot = new GameObject("Dot");
            dot.transform.SetParent(b.root, false);
            Disc(dot.transform, lit, 0.36f, 0.003f, null, b.edge);
            Disc(dot.transform, Vector2.zero, 0.36f, 0.007f, null, b.groove);
            b.dotInner = Disc(dot.transform, Vector2.zero, 0.25f, 0.011f, m_Theme.targetMark, null);
            b.dot = dot;
            return b;
        }

        /// <summary>A flat rounded stroke from a to b (x, z on top of the blob).</summary>
        static void Stroke(Transform parent, Vector2 a, Vector2 b, float width, float y, Material material, List<MeshRenderer> into)
        {
            Transform bar = Primitive(PrimitiveType.Cube, "Bar", parent, material);
            Vector2 d = b - a;
            Vector2 mid = (a + b) * 0.5f;
            bar.localPosition = new Vector3(mid.x, y, mid.y);
            bar.localRotation = Quaternion.Euler(0f, Mathf.Atan2(-d.y, d.x) * Mathf.Rad2Deg, 0f);
            bar.localScale = new Vector3(d.magnitude, 0.006f, width);
            if (into != null) into.Add(bar.GetComponent<MeshRenderer>());
            Disc(parent, a, width, y, material, into);
            Disc(parent, b, width, y, material, into);
        }

        static MeshRenderer Disc(Transform parent, Vector2 at, float diameter, float y, Material material, List<MeshRenderer> into)
        {
            Transform disc = Primitive(PrimitiveType.Cylinder, "Disc", parent, material);
            disc.localPosition = new Vector3(at.x, y, at.y);
            disc.localScale = new Vector3(diameter, 0.003f, diameter);
            var r = disc.GetComponent<MeshRenderer>();
            if (into != null) into.Add(r);
            return r;
        }

        void Configure(Badge b, Vector2Int rel, BadgeInfo info)
        {
            b.rel = rel;
            b.status = info.status;
            b.needColor = info.needColor;
            b.cellColor = info.cellColor;
            b.age = 0f;

            bool isHead = rel == Vector2Int.zero;
            b.root.localPosition = new Vector3(rel.x, height + 0.001f, rel.y + (isHead ? 0.2f : 0f));
            b.root.localScale = Vector3.zero;
            b.root.gameObject.SetActive(true);

            b.check.SetActive(info.status == CellStatus.TargetOk);
            // Symbols only: a target covered by the wrong colour simply does not fit, so it gets the cross too.
            b.cross.SetActive(info.status == CellStatus.Forbidden || info.status == CellStatus.TargetWrongColor);
            b.dot.SetActive(false);

            Material groove = m_Theme.GrooveMaterial(info.cellColor);
            Material edge = m_Theme.GrooveEdgeMaterial(info.cellColor);
            for (int i = 0; i < b.groove.Count; i++) b.groove[i].sharedMaterial = groove;
            for (int i = 0; i < b.edge.Count; i++) b.edge[i].sharedMaterial = edge;
            if (info.status == CellStatus.TargetWrongColor)
                b.dotInner.sharedMaterial = m_Theme.MarkMaterial(info.needColor);
        }

        void UpdateBadges(float dt)
        {
            for (int i = 0; i < m_Badges.Count; i++)
            {
                Badge b = m_Badges[i];
                if (!b.root.gameObject.activeSelf) continue;
                b.age += dt;

                // Pop in with overshoot.
                float t = Mathf.Clamp01(b.age / 0.26f) - 1f;
                float s = 1f + 2.7f * t * t * t + 1.7f * t * t;
                float baseScale = b.rel == Vector2Int.zero ? 0.8f : 1.22f;
                float wiggle = 0f;

                if (b.status == CellStatus.Forbidden || b.status == CellStatus.TargetWrongColor)
                {
                    s *= 1f + 0.07f * Mathf.Sin(Time.time * 10f);
                    wiggle = Mathf.Sin(Time.time * 15f) * 6f;
                }
                else
                {
                    s *= 1f + 0.035f * Mathf.Sin(Time.time * 3.2f + b.rel.x + b.rel.y);
                }

                b.root.localScale = new Vector3(s * baseScale, 1f, s * baseScale);
                b.root.localRotation = Quaternion.Euler(0f, wiggle, 0f);
            }
        }

        // ---------------------------------------------------------------- animation

        void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 1f / 30f);

            // Glide to the logical position.
            m_Pos = Vector3.Lerp(m_Pos, m_Target, 1f - Mathf.Exp(-glideSpeed * dt));

            // Springs.
            m_ScaleVel += (-springStiffness * m_Scale - springDamping * m_ScaleVel) * dt;
            m_Scale += m_ScaleVel * dt;
            m_NudgeVel += (-320f * m_Nudge - 20f * m_NudgeVel) * dt;
            m_Nudge += m_NudgeVel * dt;

            // Hop (win).
            if (m_HopY > 0f || m_HopVel > 0f)
            {
                m_HopVel -= 30f * dt;
                m_HopY += m_HopVel * dt;
                if (m_HopY <= 0f)
                {
                    m_HopY = 0f;
                    m_HopVel = 0f;
                    m_ScaleVel += new Vector3(2f, -3.2f, 2f);
                    m_HopsLeft--;
                    if (m_HopsLeft > 0) m_HopVel = 4.2f;
                }
            }

            // Uneasy shiver while sitting on a forbidden tile.
            Vector3 shiver = m_Worried ? new Vector3(Mathf.Sin(Time.time * 38f) * 0.012f, 0f, 0f) : Vector3.zero;

            transform.localPosition = m_Pos + m_Nudge + shiver + Vector3.up * m_HopY;

            float breathe = Mathf.Sin(Time.time * 2.3f) * 0.012f;
            Vector3 scale = Vector3.one + m_Scale + new Vector3(-breathe * 0.5f, breathe, -breathe * 0.5f);
            scale.x = Mathf.Clamp(scale.x, 0.6f, 1.5f);
            scale.y = Mathf.Clamp(scale.y, 0.5f, 1.6f);
            scale.z = Mathf.Clamp(scale.z, 0.6f, 1.5f);
            m_Pivot.localScale = scale;

            UpdateFace(dt);
            UpdateBadges(dt);
        }

        void UpdateFace(float dt)
        {
            // Eyes drift towards the last direction, then relax. They step aside for a badge on the head.
            m_LookTimer -= dt;
            if (m_LookTimer <= 0f) m_LookTarget = Vector3.zero;
            m_Look = Vector3.Lerp(m_Look, m_LookTarget, 1f - Mathf.Exp(-14f * dt));
            m_FaceZ = Mathf.Lerp(m_FaceZ, m_HeadHasBadge ? -0.16f : 0.02f, 1f - Mathf.Exp(-16f * dt));
            m_Face.localPosition = new Vector3(m_Look.x, height - 0.012f, m_FaceZ + m_Look.z);

            // Blink now and then.
            m_BlinkTimer -= dt;
            if (m_BlinkTimer <= 0f)
            {
                m_BlinkTimer = Random.Range(1.8f, 4.5f);
                m_BlinkTime = 0.11f;
            }

            m_BlinkTime -= dt;
            m_SquintTime -= dt;
            m_WideTime -= dt;

            float open = 1f;
            if (m_SquintTime > 0f) open = 0.22f;
            else if (m_BlinkTime > 0f) open = 0.12f;
            else if (m_WideTime > 0f) open = 1.22f;
            else if (m_Worried) open = 0.6f;

            Vector3 target = new Vector3(EyeSize.x * (open > 1f ? 1.1f : 1f), EyeSize.y, EyeSize.z * open);
            float k = 1f - Mathf.Exp(-38f * dt);
            m_EyeL.localScale = Vector3.Lerp(m_EyeL.localScale, target, k);
            m_EyeR.localScale = Vector3.Lerp(m_EyeR.localScale, target, k);
        }

        void OnDestroy()
        {
            if (m_Mesh != null) Destroy(m_Mesh);
        }
    }
}
