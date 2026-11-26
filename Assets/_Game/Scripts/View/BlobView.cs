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
    /// The player: a jelly blob made of grid cells, with a face on its head cell
    /// and one more face for every other colour it carries.
    /// Game logic is instant; this class only makes it look alive (glide, squash, wobble, blink)
    /// and shows what each cell is standing on (check, wrong colour, forbidden).
    /// </summary>
    public class BlobView : MonoBehaviour
    {
        // Shape values come from the theme (GameManager > Theme > Blob shape), where they are saved
        // and can be changed while playing. They are copied here before every rebuild.
        [System.NonSerialized] public float height = 0.62f;
        [System.NonSerialized] public float margin = 0.05f;
        [System.NonSerialized] public float cornerRadius = 0.24f;
        [System.NonSerialized] public float bevel = 0.14f;

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
            /// <summary>Sits on a cell that has a face, so it is drawn smaller and shares the cell with the eyes.</summary>
            public bool onFace;
        }

        /// <summary>A pair of eyes. Index 0 is the head; the others belong to the other colours of the blob.</summary>
        class Face
        {
            public Transform root;
            public Transform eyeL, eyeR;
            public Vector2Int rel;
            public int color;
            public bool hasBadge;
            public float z = 0.02f;
            public float blinkTimer = 2f;
            public float blinkTime;
            public Vector3 glance;        // idle: where this face is looking right now
            public Vector3 glanceTarget;
        }

        GameTheme m_Theme;
        Transform m_Pivot;
        Transform m_MeshRoot;
        Transform m_FaceRoot;
        readonly List<Face> m_Faces = new List<Face>();
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
        float m_SquintTime;
        float m_WideTime;
        bool m_Worried;

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

            var faces = new GameObject("Faces");
            m_FaceRoot = faces.transform;
            m_FaceRoot.SetParent(m_MeshRoot, false);
            m_Faces.Add(CreateFace("Face"));   // the head

            var badges = new GameObject("Badges");
            m_BadgeRoot = badges.transform;
            m_BadgeRoot.SetParent(m_MeshRoot, false);

            Snap(cells, head);
        }

        Face CreateFace(string name)
        {
            var f = new Face();
            var go = new GameObject(name);
            f.root = go.transform;
            f.root.SetParent(m_FaceRoot, false);
            f.eyeL = MakeEye("EyeL", -0.155f, f.root, m_Theme);
            f.eyeR = MakeEye("EyeR", 0.155f, f.root, m_Theme);
            f.blinkTimer = Random.Range(0.8f, 4f);   // every face blinks in its own time
            return f;
        }

        Transform MakeEye(string name, float x, Transform parent, GameTheme theme)
        {
            Transform eye = Primitive(PrimitiveType.Sphere, name, parent, theme.eye);
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
            foreach (var c in cells)
            {
                var r = c.Key - head;
                m_Rel.Add(r);
                m_RelColor[r] = c.Value;
            }

            BuildMesh();
            AssignFaces();
        }

        readonly float[] m_Tuning = new float[9];

        /// <summary>Reads the shape values from the theme. Returns true when any of them changed.</summary>
        bool ReadTuning()
        {
            height = m_Theme.blobHeight;
            margin = m_Theme.blobMargin;
            cornerRadius = m_Theme.blobCornerRadius;
            bevel = m_Theme.blobBevel;

            bool changed = false;
            for (int i = 0; i < m_Tuning.Length; i++)
            {
                float v = i == 0 ? height : i == 1 ? margin : i == 2 ? cornerRadius : i == 3 ? bevel
                        : i == 4 ? (float)m_Theme.colorSeam : i == 5 ? m_Theme.seamGap : i == 6 ? m_Theme.seamCorner
                        : i == 7 ? m_Theme.seamBevel : m_Theme.colorBlend;
                if (m_Tuning[i] != v) changed = true;
                m_Tuning[i] = v;
            }
            return changed;
        }

        /// <summary>Builds the mesh for the current cells with the current theme values.</summary>
        void BuildMesh()
        {
            ReadTuning();

            Vector2Int min = Vector2Int.zero, max = Vector2Int.zero;
            foreach (var r in m_Rel)
            {
                min = new Vector2Int(Mathf.Min(min.x, r.x), Mathf.Min(min.y, r.y));
                max = new Vector2Int(Mathf.Max(max.x, r.x), Mathf.Max(max.y, r.y));
            }

            var s = CellMesh.Settings.Create(margin, cornerRadius, 0f, height, bevel, true);
            s.cornerSegments = 8;
            s.bevelSegments = 5;
            if (m_Theme.colorSeam == ColorSeam.SoftBlend && m_Theme.colorBlend > 0.001f && m_Theme.BlobBlendMaterial != null)
            {
                // One piece of jelly; the colours are painted on with a texture so they can run into each other.
                CellMesh.Build(m_Rel, s, m_Mesh);
                PaintColors(min, max);
            }
            else
            {
                // One piece per colour. With "Edge" the pieces do not fuse, so a bevelled edge runs between them.
                s.splitGroups = m_Theme.colorSeam == ColorSeam.Edge;
                s.seamMargin = m_Theme.seamGap;
                s.seamRadius = m_Theme.seamCorner;
                s.seamBevel = m_Theme.seamBevel;
                CellMesh.Build(m_Rel, s, m_Mesh, m_RelColor, m_Theme.ColorCount);
                m_Renderer.sharedMaterials = m_Theme.BlobMaterials;
            }

            // Scale around the middle of the shape so squash and stretch stay centred.
            Vector3 center = new Vector3((min.x + max.x) * 0.5f, 0f, (min.y + max.y) * 0.5f);
            m_Pivot.localPosition = center;
            m_MeshRoot.localPosition = -center;

            // Marks sit on top of the blob, so they follow its height.
            for (int i = 0; i < m_Badges.Count; i++)
            {
                Vector3 p = m_Badges[i].root.localPosition;
                m_Badges[i].root.localPosition = new Vector3(p.x, height + 0.001f, p.z);
            }
        }

        // ---------------------------------------------------------------- colours

        const int TexelsPerCell = 16;
        Texture2D m_ColorTex;
        Material m_BlendMaterial;
        Color32[] m_Pixels;
        static readonly List<Vector2> s_Uv = new List<Vector2>();

        /// <summary>
        /// Paints every cell's colour into a small texture, easing from one colour to the next across
        /// <see cref="GameTheme.colorBlend"/> cells on each side of a seam, and maps the mesh onto it.
        /// </summary>
        void PaintColors(Vector2Int min, Vector2Int max)
        {
            int cellsX = max.x - min.x + 1, cellsY = max.y - min.y + 1;
            int w = cellsX * TexelsPerCell, h = cellsY * TexelsPerCell;

            if (m_ColorTex == null || m_ColorTex.width != w || m_ColorTex.height != h)
            {
                if (m_ColorTex != null) Destroy(m_ColorTex);
                m_ColorTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                m_ColorTex.name = "Blob colours";
                m_ColorTex.wrapMode = TextureWrapMode.Clamp;
                m_ColorTex.filterMode = FilterMode.Bilinear;
                m_Pixels = new Color32[w * h];
            }

            float band = Mathf.Max(0.001f, m_Theme.colorBlend);
            for (int ty = 0; ty < h; ty++)
            {
                int cy = ty / TexelsPerCell;
                float v = (ty % TexelsPerCell + 0.5f) / TexelsPerCell - 0.5f;
                for (int tx = 0; tx < w; tx++)
                {
                    int cx = tx / TexelsPerCell;
                    float u = (tx % TexelsPerCell + 0.5f) / TexelsPerCell - 0.5f;
                    m_Pixels[ty * w + tx] = TexelColor(new Vector2Int(min.x + cx, min.y + cy), u, v, band);
                }
            }
            m_ColorTex.SetPixels32(m_Pixels);
            m_ColorTex.Apply(false);

            // Mesh UVs arrive as grid positions; squeeze them onto the texture.
            m_Mesh.GetUVs(0, s_Uv);
            for (int i = 0; i < s_Uv.Count; i++)
                s_Uv[i] = new Vector2((s_Uv[i].x - (min.x - 0.5f)) / cellsX, (s_Uv[i].y - (min.y - 0.5f)) / cellsY);
            m_Mesh.SetUVs(0, s_Uv);

            if (m_BlendMaterial == null)
            {
                m_BlendMaterial = new Material(m_Theme.BlobBlendMaterial);
                m_BlendMaterial.name = "Blob (blended colours, instance)";
            }
            if (m_BlendMaterial.HasProperty("_BaseMap")) m_BlendMaterial.SetTexture("_BaseMap", m_ColorTex);
            if (m_BlendMaterial.HasProperty("_MainTex")) m_BlendMaterial.SetTexture("_MainTex", m_ColorTex);
            m_Renderer.sharedMaterials = new[] { m_BlendMaterial };
        }

        /// <summary>Colour at a point inside a cell (u, v = -0.5 .. 0.5 from its middle), eased towards the neighbours near its edges.</summary>
        Color32 TexelColor(Vector2Int cell, float u, float v, float band)
        {
            int own;
            if (!m_RelColor.TryGetValue(cell, out own)) return new Color32(255, 255, 255, 255);   // never shown
            Color c0 = m_Theme.CellColor(own).linear;

            Vector2Int sx = new Vector2Int(u >= 0f ? 1 : -1, 0);
            Vector2Int sy = new Vector2Int(0, v >= 0f ? 1 : -1);
            float wx = SeamWeight(0.5f - Mathf.Abs(u), band);
            float wy = SeamWeight(0.5f - Mathf.Abs(v), band);
            if (wx <= 0f && wy <= 0f) return (Color32)c0.gamma;

            int ix, iy, id;
            bool hasX = m_RelColor.TryGetValue(cell + sx, out ix);
            bool hasY = m_RelColor.TryGetValue(cell + sy, out iy);
            bool hasD = m_RelColor.TryGetValue(cell + sx + sy, out id);
            Color cx = hasX ? m_Theme.CellColor(ix).linear : c0;
            Color cy = hasY ? m_Theme.CellColor(iy).linear : c0;
            Color cd;
            if (hasX && hasY) cd = hasD ? m_Theme.CellColor(id).linear : (cx + cy) * 0.5f;
            else if (hasX) cd = cx;
            else if (hasY) cd = cy;
            else cd = c0;

            // Blending in linear light keeps the seam bright instead of muddy.
            Color nearRow = Color.Lerp(c0, cx, wx);
            Color farRow = Color.Lerp(cy, cd, wx);
            Color mixed = Color.Lerp(nearRow, farRow, wy);
            mixed.a = 1f;
            return (Color32)mixed.gamma;
        }

        /// <summary>0 well inside the cell, rising smoothly to 0.5 exactly on the seam (the neighbour does the other half).</summary>
        static float SeamWeight(float distanceToEdge, float band)
        {
            if (distanceToEdge >= band) return 0f;
            float p = 0.5f - 0.5f * distanceToEdge / band;   // 0 .. 0.5
            return p * p * (3f - 2f * p);
        }

        /// <summary>
        /// The head keeps the main face. Every other colour in the blob gets a face of its own, on the cell
        /// nearest the middle of that colour. A face stays on its cell for as long as that cell keeps its colour.
        /// </summary>
        void AssignFaces()
        {
            int headColor;
            if (!m_RelColor.TryGetValue(Vector2Int.zero, out headColor)) headColor = 0;
            m_Faces[0].rel = Vector2Int.zero;
            m_Faces[0].color = headColor;

            var colors = new List<int>();
            foreach (var c in m_RelColor)
                if (c.Value != headColor && !colors.Contains(c.Value)) colors.Add(c.Value);
            colors.Sort();

            var keep = new HashSet<Face>();
            for (int i = 0; i < colors.Count; i++)
            {
                int color = colors[i];

                // Already has a face on a cell that is still this colour? Leave it where it is.
                Face face = null;
                for (int f = 1; f < m_Faces.Count; f++)
                {
                    Face old = m_Faces[f];
                    int now;
                    if (old.root.gameObject.activeSelf && old.color == color && !keep.Contains(old)
                        && m_RelColor.TryGetValue(old.rel, out now) && now == color)
                    {
                        face = old;
                        break;
                    }
                }

                if (face == null)
                {
                    for (int f = 1; f < m_Faces.Count; f++)
                        if (!m_Faces[f].root.gameObject.activeSelf && !keep.Contains(m_Faces[f])) { face = m_Faces[f]; break; }
                    if (face == null)
                    {
                        face = CreateFace("Face " + (m_Faces.Count + 1));
                        m_Faces.Add(face);
                    }
                    face.rel = MiddleCellOf(color);
                    face.color = color;
                    face.hasBadge = false;
                    face.z = 0.02f;
                    // Eyes start shut and pop open.
                    face.eyeL.localScale = new Vector3(EyeSize.x, EyeSize.y, 0f);
                    face.eyeR.localScale = new Vector3(EyeSize.x, EyeSize.y, 0f);
                    face.root.gameObject.SetActive(true);
                }
                keep.Add(face);
            }

            for (int f = 1; f < m_Faces.Count; f++)
                if (!keep.Contains(m_Faces[f])) m_Faces[f].root.gameObject.SetActive(false);
        }

        /// <summary>The cell of a colour closest to the middle of all cells of that colour.</summary>
        Vector2Int MiddleCellOf(int color)
        {
            Vector2 sum = Vector2.zero;
            int count = 0;
            foreach (var c in m_RelColor)
                if (c.Value == color) { sum += c.Key; count++; }
            Vector2 middle = count > 0 ? sum / count : Vector2.zero;

            Vector2Int best = Vector2Int.zero;
            float bestDist = float.MaxValue;
            foreach (var c in m_RelColor)
            {
                if (c.Value != color) continue;
                float d = ((Vector2)c.Key - middle).sqrMagnitude;
                // Ties go to the lower-left cell so the choice never depends on dictionary order.
                bool better = d < bestDist - 0.0001f ||
                              (Mathf.Abs(d - bestDist) <= 0.0001f && (c.Key.y < best.y || (c.Key.y == best.y && c.Key.x < best.x)));
                if (better) { best = c.Key; bestDist = d; }
            }
            return best;
        }

        Face FaceOn(Vector2Int rel)
        {
            for (int f = 0; f < m_Faces.Count; f++)
                if (m_Faces[f].root.gameObject.activeSelf && m_Faces[f].rel == rel) return m_Faces[f];
            return null;
        }

        /// <summary>Jump straight to a state (level start, undo).</summary>
        public void Snap(Dictionary<Vector2Int, int> cells, Vector2Int head)
        {
            WakeUp();
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
            WakeUp();
            m_Head = head;
            m_Target = BoardView.CellToLocal(head);
            Vector3 along = new Vector3(Mathf.Abs(dir.x), 0f, Mathf.Abs(dir.y));
            Vector3 across = new Vector3(1f, 0f, 1f) - along;
            m_ScaleVel += along * 2.4f - across * 1.5f - Vector3.up * 0.5f;
            LookAt(dir);
        }

        /// <param name="pulseColor">Colour of the pump (-1 = plain): a wave in that colour runs through the blob to the new cells.</param>
        public void Grow(Dictionary<Vector2Int, int> cells, Vector2Int head, Vector2Int dir, int pulseColor = -1)
        {
            WakeUp();
            SetShape(cells, head);
            StartPulse(dir, pulseColor);
            Vector3 along = new Vector3(Mathf.Abs(dir.x), 0f, Mathf.Abs(dir.y));
            m_ScaleVel += new Vector3(2.6f, 3.4f, 2.6f) + along * 1.6f;
            m_SquintTime = 0.22f;
            m_WideTime = 0.6f;
            LookAt(dir);
        }

        /// <summary>Walked into something solid.</summary>
        public void Bump(Vector2Int dir)
        {
            WakeUp();
            Vector3 d = new Vector3(dir.x, 0f, dir.y);
            Vector3 along = new Vector3(Mathf.Abs(dir.x), 0f, Mathf.Abs(dir.y));
            Vector3 across = new Vector3(1f, 0f, 1f) - along;
            m_NudgeVel += d * 2.6f;
            m_ScaleVel += -along * 2.2f + across * 1.1f + Vector3.up * 0.8f;
            m_SquintTime = 0.18f;
            LookAt(dir);
        }

        /// <summary>A booster blew one block away: a startled squash and wide eyes.</summary>
        public void Startle()
        {
            WakeUp();
            m_ScaleVel += new Vector3(-3.2f, 4.6f, -3.2f);
            m_SquintTime = 0.12f;
            m_WideTime = 0.9f;
        }

        /// <summary>The "click" when the last tile is covered.</summary>
        public void Settle()
        {
            WakeUp();
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

        // ---------------------------------------------------------------- colour pulse

        class PulseDot
        {
            public Transform transform;
            public MeshRenderer renderer;
            public float delay;
            public float age = -1f;   // < 0 = not playing
        }

        readonly List<PulseDot> m_Pulse = new List<PulseDot>();
        Transform m_PulseRoot;
        const float PulseStep = 0.045f;    // seconds per cell the wave travels
        const float PulseLife = 0.24f;

        /// <summary>
        /// A row-by-row wave of light dots that starts at the pump side and runs to the far side,
        /// where the new cells are: it shows that what the pump pushed in travelled through the blob.
        /// </summary>
        void StartPulse(Vector2Int dir, int pulseColor)
        {
            if (m_PulseRoot == null)
            {
                var go = new GameObject("Pulse");
                m_PulseRoot = go.transform;
                m_PulseRoot.SetParent(m_MeshRoot, false);
            }

            int minAlong = int.MaxValue;
            foreach (var r in m_Rel) minAlong = Mathf.Min(minAlong, r.x * dir.x + r.y * dir.y);

            int used = 0;
            foreach (var cell in m_RelColor)
            {
                if (used >= 48) break;
                PulseDot dot;
                if (used < m_Pulse.Count) dot = m_Pulse[used];
                else
                {
                    Transform t = Primitive(PrimitiveType.Cylinder, "Dot", m_PulseRoot, null);
                    dot = new PulseDot { transform = t, renderer = t.GetComponent<MeshRenderer>() };
                    m_Pulse.Add(dot);
                }
                used++;

                int along = cell.Key.x * dir.x + cell.Key.y * dir.y - minAlong;
                dot.delay = along * PulseStep;
                dot.age = 0f;
                dot.renderer.sharedMaterial = m_Theme.GrooveEdgeMaterial(pulseColor >= 0 ? pulseColor : cell.Value);
                dot.transform.localPosition = new Vector3(cell.Key.x, height + 0.004f, cell.Key.y);
                dot.transform.localScale = Vector3.zero;
                dot.transform.gameObject.SetActive(true);
            }
            for (int i = used; i < m_Pulse.Count; i++)
            {
                m_Pulse[i].age = -1f;
                m_Pulse[i].transform.gameObject.SetActive(false);
            }
        }

        void UpdatePulse(float dt)
        {
            for (int i = 0; i < m_Pulse.Count; i++)
            {
                PulseDot dot = m_Pulse[i];
                if (dot.age < 0f) continue;
                dot.age += dt;
                float t = (dot.age - dot.delay) / PulseLife;
                if (t >= 1f)
                {
                    dot.age = -1f;
                    dot.transform.gameObject.SetActive(false);
                    continue;
                }
                float size = t <= 0f ? 0f : Mathf.Sin(t * Mathf.PI) * 0.46f;
                dot.transform.localScale = new Vector3(size, 0.003f, size);
            }
        }

        // ---------------------------------------------------------------- idle life

        /// <summary>
        /// Where something interesting is, seen from the head, in cells (zero = nothing): the game
        /// points it at the nearest target that is still open. Bored faces glance that way.
        /// </summary>
        [System.NonSerialized] public Vector2 interest;

        const float IdleAfter = 4f;
        float m_IdleTime;
        float m_GlanceTimer;
        int m_Glances;

        /// <summary>Something happened: stop daydreaming.</summary>
        void WakeUp()
        {
            m_IdleTime = 0f;
            m_GlanceTimer = 0f;
            for (int f = 0; f < m_Faces.Count; f++) m_Faces[f].glanceTarget = Vector3.zero;
        }

        void UpdateIdle(float dt)
        {
            m_IdleTime += dt;
            if (m_IdleTime < IdleAfter || m_HopY > 0f) return;

            m_GlanceTimer -= dt;
            if (m_GlanceTimer > 0f) return;

            // Alternate: look somewhere for a moment, then back to the middle for a while.
            bool looking = false;
            for (int f = 0; f < m_Faces.Count; f++)
                if (m_Faces[f].glanceTarget != Vector3.zero) looking = true;

            if (looking)
            {
                for (int f = 0; f < m_Faces.Count; f++) m_Faces[f].glanceTarget = Vector3.zero;
                m_GlanceTimer = Random.Range(1.6f, 3.4f);
                return;
            }

            m_Glances++;
            int active = 0;
            for (int f = 0; f < m_Faces.Count; f++)
                if (m_Faces[f].root.gameObject.activeSelf) active++;

            // With company, every other glance is at each other; otherwise at the open target, or just around.
            bool atEachOther = active > 1 && m_Glances % 2 == 0;
            Vector2 around = Random.insideUnitCircle.normalized;
            for (int f = 0; f < m_Faces.Count; f++)
            {
                Face face = m_Faces[f];
                if (!face.root.gameObject.activeSelf) continue;

                Vector2 to = Vector2.zero;
                if (atEachOther)
                {
                    float best = float.MaxValue;
                    for (int o = 0; o < m_Faces.Count; o++)
                    {
                        if (o == f || !m_Faces[o].root.gameObject.activeSelf) continue;
                        Vector2 d = m_Faces[o].rel - face.rel;
                        if (d.sqrMagnitude < best) { best = d.sqrMagnitude; to = d; }
                    }
                }
                else if (interest != Vector2.zero) to = interest - (Vector2)face.rel;
                if (to == Vector2.zero) to = around;

                to.Normalize();
                face.glanceTarget = new Vector3(to.x, 0f, to.y) * 0.085f;
                face.blinkTime = 0.1f;   // a blink goes with turning the eyes
            }
            m_GlanceTimer = Random.Range(1.1f, 1.9f);

            // Now and then a little stretch.
            if (m_Glances % 3 == 0) m_ScaleVel += new Vector3(-0.7f, 1.1f, -0.7f);
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
            for (int f = 0; f < m_Faces.Count; f++) m_Faces[f].hasBadge = false;

            for (int i = 0; i < infos.Count; i++)
            {
                BadgeInfo info = infos[i];
                if (info.status == CellStatus.Plain) continue;
                Vector2Int rel = info.cell - m_Head;
                if (info.status == CellStatus.Forbidden) m_Worried = true;
                Face faceHere = FaceOn(rel);
                if (faceHere != null) faceHere.hasBadge = true;

                // Keep a badge that already shows the same thing on the same cell (no re-pop).
                Badge badge = null;
                for (int b = 0; b < m_Badges.Count; b++)
                {
                    Badge c = m_Badges[b];
                    if (!c.used && c.root.gameObject.activeSelf && c.rel == rel && c.status == info.status
                        && c.needColor == info.needColor && c.cellColor == info.cellColor && c.onFace == (faceHere != null))
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

            b.onFace = FaceOn(rel) != null;
            b.root.localPosition = new Vector3(rel.x, height + 0.001f, rel.y + (b.onFace ? 0.2f : 0f));
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
                float baseScale = b.onFace ? 0.8f : 1.22f;
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

            // Shape values changed in the inspector while playing? Rebuild straight away.
            if (ReadTuning()) BuildMesh();

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

            UpdateIdle(dt);
            UpdateFace(dt);
            UpdateBadges(dt);
            UpdatePulse(dt);
        }

        void UpdateFace(float dt)
        {
            // Eyes drift towards the last direction, then relax.
            m_LookTimer -= dt;
            if (m_LookTimer <= 0f) m_LookTarget = Vector3.zero;
            m_Look = Vector3.Lerp(m_Look, m_LookTarget, 1f - Mathf.Exp(-14f * dt));

            m_SquintTime -= dt;
            m_WideTime -= dt;

            float k = 1f - Mathf.Exp(-38f * dt);
            for (int f = 0; f < m_Faces.Count; f++)
            {
                Face face = m_Faces[f];
                if (!face.root.gameObject.activeSelf) continue;

                // Each face steps aside for a badge on its own cell.
                face.z = Mathf.Lerp(face.z, face.hasBadge ? -0.16f : 0.02f, 1f - Mathf.Exp(-16f * dt));
                face.glance = Vector3.Lerp(face.glance, face.glanceTarget, 1f - Mathf.Exp(-9f * dt));
                face.root.localPosition = new Vector3(face.rel.x + m_Look.x + face.glance.x, height - 0.012f,
                    face.rel.y + face.z + m_Look.z + face.glance.z);

                // Blink now and then, every face in its own rhythm.
                face.blinkTimer -= dt;
                if (face.blinkTimer <= 0f)
                {
                    face.blinkTimer = Random.Range(1.8f, 4.5f);
                    face.blinkTime = 0.11f;
                }
                face.blinkTime -= dt;

                float open = 1f;
                if (m_SquintTime > 0f) open = 0.22f;
                else if (face.blinkTime > 0f) open = 0.12f;
                else if (m_WideTime > 0f) open = 1.22f;
                else if (m_Worried) open = 0.6f;

                Vector3 target = new Vector3(EyeSize.x * (open > 1f ? 1.1f : 1f), EyeSize.y, EyeSize.z * open);
                face.eyeL.localScale = Vector3.Lerp(face.eyeL.localScale, target, k);
                face.eyeR.localScale = Vector3.Lerp(face.eyeR.localScale, target, k);
            }
        }

        void OnDestroy()
        {
            if (m_Mesh != null) Destroy(m_Mesh);
            if (m_ColorTex != null) Destroy(m_ColorTex);
            if (m_BlendMaterial != null) Destroy(m_BlendMaterial);
        }
    }
}
