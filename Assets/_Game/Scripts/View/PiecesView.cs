using System.Collections.Generic;
using UnityEngine;

namespace ExpandPuzzle
{
    /// <summary>
    /// The loose things on a board: movable walls, traps, and blob body waiting to be picked up.
    /// Game logic is instant; this class shows where things are and animates the changes.
    /// </summary>
    public class PiecesView : MonoBehaviour
    {
        /// <summary>Movable walls stand a little taller than the fixed ones, so they read as separate pieces.</summary>
        public const float BlockHeight = 0.5f;

        class Block
        {
            public Transform root;
            public Vector3 pos, target;
            public bool lifted;
            public float lift;      // 0..1, eased
            public float hop;       // 1 -> 0 while a hint makes it jump
        }

        class Trap
        {
            public Transform root;
            public Vector2Int cell;
            public float phase;
            public Transform[] spikes;
            public float[] spikeHeight;
            public Material rim;
            public bool near;              // the blob is right next to it
            public float nearBlend;
            public float leaving = -1f;    // >= 0 while it snaps and sinks away
        }

        readonly List<Block> m_Blocks = new List<Block>();
        readonly List<Trap> m_Traps = new List<Trap>();
        GameTheme m_Theme;
        Mesh m_BlockMesh, m_SpikeMesh, m_LooseMesh;
        Material m_BlockMaterial, m_SpikeMaterial;
        Transform m_Loose;
        MeshRenderer m_LooseRenderer;
        float m_LoosePop = 1f;

        public void Build(BoardState state, GameTheme theme, int levelIndex)
        {
            m_Theme = theme;

            // Walls you can move are made of the board's own material, a shade darker, with yellow studs
            // on top: yellow is the colour of everything on the board that reacts to the player.
            BoardPalette palette = theme.PaletteFor(levelIndex);
            m_BlockMaterial = new Material(theme.boardSurface);
            m_BlockMaterial.name = "Movable wall";
            MaterialFactory.Tint(m_BlockMaterial, MaterialFactory.Shade(palette.pit));

            m_BlockMesh = CellMesh.Build(new[] { Vector2Int.zero }, CellMesh.Settings.Create(0.07f, 0.2f, 0.02f, BlockHeight, 0.09f, false));
            m_BlockMesh.name = "Movable wall";
            m_SpikeMesh = BuildCone(14);

            var looseGo = new GameObject("Loose body");
            m_Loose = looseGo.transform;
            m_Loose.SetParent(transform, false);
            m_LooseMesh = new Mesh();
            m_LooseMesh.name = "Loose body";
            m_LooseMesh.MarkDynamic();
            looseGo.AddComponent<MeshFilter>().sharedMesh = m_LooseMesh;
            m_LooseRenderer = looseGo.AddComponent<MeshRenderer>();

            Sync(state);
        }

        /// <summary>Everything jumps to where the state says it is (level start, undo).</summary>
        public void Sync(BoardState state)
        {
            while (m_Blocks.Count < state.blocks.Count) m_Blocks.Add(CreateBlock(m_Blocks.Count));
            for (int i = 0; i < m_Blocks.Count; i++)
            {
                Block b = m_Blocks[i];
                bool used = i < state.blocks.Count;
                b.root.gameObject.SetActive(used);
                if (!used) continue;
                b.pos = b.target = BoardView.CellToLocal(state.blocks[i]);
                b.lifted = false;
                b.lift = 0f;
                b.hop = 0f;
                b.root.localPosition = b.pos;
            }

            for (int i = 0; i < m_Traps.Count; i++)
            {
                if (m_Traps[i].root != null) Destroy(m_Traps[i].root.gameObject);
                if (m_Traps[i].rim != null) Destroy(m_Traps[i].rim);
            }
            m_Traps.Clear();
            foreach (Vector2Int cell in state.traps) m_Traps.Add(CreateTrap(cell));

            SetLoose(state.loose);
        }

        // ---------------------------------------------------------------- movable walls

        Block CreateBlock(int index)
        {
            var b = new Block();
            var root = new GameObject("Movable wall " + index);
            b.root = root.transform;
            b.root.SetParent(transform, false);

            var body = new GameObject("Body");
            body.transform.SetParent(b.root, false);
            body.AddComponent<MeshFilter>().sharedMesh = m_BlockMesh;
            var r = body.AddComponent<MeshRenderer>();
            r.sharedMaterial = m_BlockMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            // Four studs: a grip for the finger.
            for (int i = 0; i < 4; i++)
            {
                Transform stud = Primitive(PrimitiveType.Cylinder, "Stud", b.root, m_Theme.pumpKnob);
                stud.localPosition = new Vector3(i % 2 == 0 ? -0.14f : 0.14f, BlockHeight + 0.012f, i < 2 ? -0.14f : 0.14f);
                stud.localScale = new Vector3(0.15f, 0.022f, 0.15f);
            }
            return b;
        }

        /// <summary>Where a wall is drawn right now, in board space (for finding it under a finger).</summary>
        public Vector3 BlockPosition(int index)
        {
            return index >= 0 && index < m_Blocks.Count ? m_Blocks[index].pos : Vector3.zero;
        }

        /// <summary>The wall glides to this cell.</summary>
        public void SetBlockCell(int index, Vector2Int cell)
        {
            if (index < 0 || index >= m_Blocks.Count) return;
            m_Blocks[index].target = BoardView.CellToLocal(cell);
        }

        /// <summary>Picked up by the finger, or put down again.</summary>
        public void SetBlockLifted(int index, bool lifted)
        {
            if (index < 0 || index >= m_Blocks.Count) return;
            m_Blocks[index].lifted = lifted;
        }

        /// <summary>A little jump that says "this one" (hint).</summary>
        public void HopBlock(int index)
        {
            if (index < 0 || index >= m_Blocks.Count) return;
            m_Blocks[index].hop = 1f;
        }

        // ---------------------------------------------------------------- traps

        static readonly Color TrapRim = MaterialFactory.Hex(0xFFB23E);       // hazard amber
        static readonly Color TrapRimHot = MaterialFactory.Hex(0xFFE27A);
        static readonly Color TrapBody = MaterialFactory.Hex(0x3D3248);      // dark plum
        static readonly Color TrapPit = MaterialFactory.Hex(0x221B2A);
        static readonly Color TrapSpike = MaterialFactory.Hex(0xEEF1F8);

        // Centre spike and four around it: x, z, width at the base, height.
        static readonly Vector4[] SpikeLayout =
        {
            new Vector4(0f, 0f, 0.22f, 0.32f),
            new Vector4(-0.16f, -0.16f, 0.16f, 0.23f), new Vector4(0.16f, -0.16f, 0.16f, 0.23f),
            new Vector4(-0.16f, 0.16f, 0.16f, 0.23f), new Vector4(0.16f, 0.16f, 0.16f, 0.23f),
        };
        const float PitTop = 0.094f;

        Mesh m_TrapRimMesh, m_TrapBodyMesh, m_TrapPitMesh;
        Material m_TrapBodyMaterial, m_TrapPitMaterial;
        BoardView m_BoardView;

        void PrepareTrapLook()
        {
            if (m_TrapRimMesh != null) return;
            var one = new[] { Vector2Int.zero };
            m_TrapRimMesh = CellMesh.Build(one, CellMesh.Settings.Create(0.05f, 0.2f, 0f, 0.07f, 0.03f, false));
            m_TrapBodyMesh = CellMesh.Build(one, CellMesh.Settings.Create(0.12f, 0.15f, 0f, 0.086f, 0.022f, false));
            m_TrapPitMesh = CellMesh.Build(one, CellMesh.Settings.Create(0.19f, 0.1f, 0f, PitTop, 0f, false));
            m_TrapBodyMaterial = MaterialFactory.Toon(TrapBody, 0.1f, 0f);
            m_TrapPitMaterial = MaterialFactory.Toon(TrapPit, 0f, 0f);
            m_SpikeMaterial = MaterialFactory.Toon(TrapSpike, 0.5f, 0.25f);
        }

        Trap CreateTrap(Vector2Int cell)
        {
            PrepareTrapLook();
            var t = new Trap { cell = cell, phase = cell.x * 1.37f + cell.y * 0.71f };
            var root = new GameObject("Trap");
            t.root = root.transform;
            t.root.SetParent(transform, false);
            t.root.localPosition = BoardView.CellToLocal(cell);

            // An amber hazard rim around a dark pit: reads as "danger" in every board colour,
            // and unlike the forbidden tile (grey with a red cross) it has something sharp inside.
            t.rim = MaterialFactory.Toon(TrapRim, 0.2f, 0f);
            MeshPart("Rim", t.root, m_TrapRimMesh, t.rim, false);
            MeshPart("Body", t.root, m_TrapBodyMesh, m_TrapBodyMaterial, false);
            MeshPart("Pit", t.root, m_TrapPitMesh, m_TrapPitMaterial, false);

            t.spikes = new Transform[SpikeLayout.Length];
            t.spikeHeight = new float[SpikeLayout.Length];
            for (int i = 0; i < SpikeLayout.Length; i++)
            {
                Vector4 s = SpikeLayout[i];
                Transform spike = MeshPart("Spike", t.root, m_SpikeMesh, m_SpikeMaterial, true);
                spike.localPosition = new Vector3(s.x, PitTop, s.y);
                t.spikes[i] = spike;
                t.spikeHeight[i] = s.w;
                spike.localScale = new Vector3(s.z, s.w, s.z);   // the mesh is a unit cone
            }
            return t;
        }

        static Transform MeshPart(string name, Transform parent, Mesh mesh, Material material, bool shadows)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        /// <summary>The trap on this cell has snapped shut: the spikes stab up, then it sinks away.</summary>
        public void RemoveTrap(Vector2Int cell)
        {
            for (int i = 0; i < m_Traps.Count; i++)
                if (m_Traps[i].cell == cell && m_Traps[i].leaving < 0f) m_Traps[i].leaving = 0f;
        }

        /// <summary>Traps right next to one of these blob cells get restless.</summary>
        public void SetNearby(IEnumerable<Vector2Int> blob)
        {
            var cells = new HashSet<Vector2Int>(blob);
            for (int i = 0; i < m_Traps.Count; i++)
            {
                Trap t = m_Traps[i];
                bool near = false;
                for (int d = 0; d < Rules.Dirs.Length && !near; d++) near = cells.Contains(t.cell + Rules.Dirs[d]);
                t.near = near;
            }
        }

        /// <summary>A round cone, 1 wide and 1 high, standing on y = 0, with a small blunt tip so it reads as toy-like.</summary>
        static Mesh BuildCone(int segments)
        {
            const float tip = 0.16f;   // radius of the blunt top, relative to the base
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var tris = new List<int>();
            float slope = 0.5f * (1f - tip);   // how far the side leans in over the height
            for (int i = 0; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 n = new Vector3(dir.x, slope, dir.z).normalized;
                verts.Add(dir * 0.5f); normals.Add(n);
                verts.Add(dir * 0.5f * tip + Vector3.up); normals.Add(n);
            }
            for (int i = 0; i < segments; i++)
            {
                int k = i * 2;
                tris.Add(k); tris.Add(k + 1); tris.Add(k + 3);
                tris.Add(k); tris.Add(k + 3); tris.Add(k + 2);
            }
            int centre = verts.Count;
            verts.Add(Vector3.up); normals.Add(Vector3.up);
            for (int i = 0; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                verts.Add(new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.5f * tip + Vector3.up);
                normals.Add(Vector3.up);
            }
            for (int i = 0; i < segments; i++)
            {
                tris.Add(centre); tris.Add(centre + 2 + i); tris.Add(centre + 1 + i);
            }
            var mesh = new Mesh { name = "Spike" };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>How far out the spikes are (1 = normal) at this point of their rhythm.</summary>
        static float SpikeRhythm(float u)
        {
            // Pull in, wait, stab out with an overshoot, settle.
            if (u < 0.28f) return Mathf.Lerp(1f, 0.25f, Smooth(u / 0.28f));
            if (u < 0.46f) return 0.25f;
            if (u < 0.56f) return Mathf.Lerp(0.25f, 1.3f, (u - 0.46f) / 0.1f);
            if (u < 0.85f) return Mathf.Lerp(1.3f, 1f, Smooth((u - 0.56f) / 0.29f));
            return 1f;
        }

        static float Smooth(float k)
        {
            k = Mathf.Clamp01(k);
            return k * k * (3f - 2f * k);
        }

        void UpdateTraps(float dt)
        {
            if (m_BoardView == null) m_BoardView = GetComponentInParent<BoardView>();
            for (int i = m_Traps.Count - 1; i >= 0; i--)
            {
                Trap t = m_Traps[i];
                float lift = m_BoardView != null ? m_BoardView.LiftAt(t.cell) : 0f;   // ride the floor ripples
                Vector3 home = BoardView.CellToLocal(t.cell) + Vector3.up * lift;

                if (t.leaving >= 0f)
                {
                    // Snap: every spike stabs up at once and the rim flashes, then the trap sinks into the floor.
                    t.leaving += dt / 0.5f;
                    if (t.leaving >= 1f)
                    {
                        if (t.rim != null) Destroy(t.rim);
                        Destroy(t.root.gameObject);
                        m_Traps.RemoveAt(i);
                        continue;
                    }
                    float stab = t.leaving < 0.15f ? Mathf.Lerp(1f, 1.75f, t.leaving / 0.15f) : 1.75f;
                    for (int s = 0; s < t.spikes.Length; s++)
                        t.spikes[s].localScale = new Vector3(SpikeLayout[s].z, t.spikeHeight[s] * stab, SpikeLayout[s].z);
                    float sink = Smooth((t.leaving - 0.25f) / 0.75f);
                    t.root.localPosition = home + Vector3.down * 0.14f * sink;
                    float size = 1f - 0.45f * sink;
                    t.root.localScale = new Vector3(size, 1f - 0.8f * sink, size);
                    MaterialFactory.Tint(t.rim, Color.Lerp(Color.white, TrapRim, Smooth(t.leaving / 0.3f)));
                    continue;
                }

                t.root.localPosition = home;

                // Restless when the blob is next door: quicker, sharper pokes and a pulsing rim.
                t.nearBlend = Mathf.MoveTowards(t.nearBlend, t.near ? 1f : 0f, dt * 4f);
                float cycle = Mathf.Lerp(2.8f, 1.1f, t.nearBlend);
                t.phase += dt / cycle;
                for (int s = 0; s < t.spikes.Length; s++)
                {
                    // The outer spikes follow the centre one a moment later: a little wave.
                    float u = Mathf.Repeat(t.phase - (s == 0 ? 0f : 0.035f + 0.012f * s), 1f);
                    float h = SpikeRhythm(u);
                    float fat = 1f + (1f - h) * 0.25f;
                    t.spikes[s].localScale = new Vector3(SpikeLayout[s].z * fat, t.spikeHeight[s] * h, SpikeLayout[s].z * fat);
                }
                float glow = t.nearBlend * (0.5f + 0.5f * Mathf.Sin(Time.time * 9f + t.cell.x));
                MaterialFactory.Tint(t.rim, Color.Lerp(TrapRim, TrapRimHot, glow));
                t.root.localScale = Vector3.one;
            }
        }

        // ---------------------------------------------------------------- loose body

        /// <summary>Redraws the blob body that lies on the floor. It is lower than the blob and has no face: it is asleep.</summary>
        public void SetLoose(Dictionary<Vector2Int, int> loose)
        {
            if (loose == null || loose.Count == 0)
            {
                m_LooseMesh.Clear();
                m_LooseRenderer.enabled = false;
                return;
            }

            var cells = new HashSet<Vector2Int>(loose.Keys);
            var s = CellMesh.Settings.Create(m_Theme.blobMargin + 0.03f, m_Theme.blobCornerRadius, 0f,
                m_Theme.blobHeight * 0.7f, m_Theme.blobBevel, true);
            s.cornerSegments = 8;
            s.bevelSegments = 5;
            s.splitGroups = m_Theme.colorSeam == ColorSeam.Edge;
            s.seamMargin = m_Theme.seamGap;
            s.seamRadius = m_Theme.seamCorner;
            s.seamBevel = m_Theme.seamBevel;
            CellMesh.Build(cells, s, m_LooseMesh, loose, m_Theme.ColorCount);
            m_LooseRenderer.sharedMaterials = m_Theme.BlobMaterials;
            m_LooseRenderer.enabled = true;
            m_LoosePop = 0f;
        }

        // ---------------------------------------------------------------- animation

        void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 1f / 30f);

            for (int i = 0; i < m_Blocks.Count; i++)
            {
                Block b = m_Blocks[i];
                if (!b.root.gameObject.activeSelf) continue;
                b.pos = Vector3.Lerp(b.pos, b.target, 1f - Mathf.Exp(-22f * dt));
                b.lift = Mathf.MoveTowards(b.lift, b.lifted ? 1f : 0f, dt * 9f);
                b.hop = Mathf.MoveTowards(b.hop, 0f, dt * 2.6f);
                float up = b.lift * 0.16f + Mathf.Sin((1f - b.hop) * Mathf.PI) * (b.hop > 0f ? 0.3f : 0f);
                b.root.localPosition = b.pos + Vector3.up * up;
                float size = 1f + b.lift * 0.06f;
                b.root.localScale = new Vector3(size, 1f, size);
            }

            UpdateTraps(dt);

            if (m_LoosePop < 1f)
            {
                m_LoosePop = Mathf.MoveTowards(m_LoosePop, 1f, dt * 5f);
                float k = m_LoosePop - 1f;
                m_Loose.localScale = new Vector3(1f, 1f + 0.5f * k * k * Mathf.Sin(m_LoosePop * 9f), 1f);
            }
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

        void OnDestroy()
        {
            if (m_BlockMesh != null) Destroy(m_BlockMesh);
            if (m_SpikeMesh != null) Destroy(m_SpikeMesh);
            if (m_LooseMesh != null) Destroy(m_LooseMesh);
            if (m_BlockMaterial != null) Destroy(m_BlockMaterial);
            if (m_TrapBodyMaterial != null) Destroy(m_TrapBodyMaterial);
            if (m_TrapPitMaterial != null) Destroy(m_TrapPitMaterial);
            if (m_TrapRimMesh != null) Destroy(m_TrapRimMesh);
            if (m_TrapBodyMesh != null) Destroy(m_TrapBodyMesh);
            if (m_TrapPitMesh != null) Destroy(m_TrapPitMesh);
            for (int i = 0; i < m_Traps.Count; i++) if (m_Traps[i].rim != null) Destroy(m_Traps[i].rim);
            if (m_SpikeMaterial != null) Destroy(m_SpikeMaterial);
        }
    }
}
