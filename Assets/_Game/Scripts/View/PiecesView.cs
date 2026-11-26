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
            public float leaving = -1f;   // >= 0 while it shrinks away
        }

        readonly List<Block> m_Blocks = new List<Block>();
        readonly List<Trap> m_Traps = new List<Trap>();
        GameTheme m_Theme;
        Mesh m_BlockMesh, m_SpikeMesh, m_LooseMesh;
        Material m_BlockMaterial, m_PlateMaterial, m_SpikeMaterial;
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
            m_PlateMaterial = MaterialFactory.Toon(MaterialFactory.Hex(0x3A3340), 0f, 0f);
            m_SpikeMaterial = MaterialFactory.Toon(MaterialFactory.Hex(0xF4F4F8), 0.3f, 0f);

            m_BlockMesh = CellMesh.Build(new[] { Vector2Int.zero }, CellMesh.Settings.Create(0.07f, 0.2f, 0.02f, BlockHeight, 0.09f, false));
            m_BlockMesh.name = "Movable wall";
            m_SpikeMesh = BuildSpike(0.085f, 0.27f);

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
                if (m_Traps[i].root != null) Destroy(m_Traps[i].root.gameObject);
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

        Trap CreateTrap(Vector2Int cell)
        {
            var t = new Trap { cell = cell, phase = cell.x * 1.3f + cell.y * 0.7f };
            var root = new GameObject("Trap");
            t.root = root.transform;
            t.root.SetParent(transform, false);
            t.root.localPosition = BoardView.CellToLocal(cell);

            Transform plate = Primitive(PrimitiveType.Cylinder, "Plate", t.root, m_PlateMaterial);
            plate.localPosition = new Vector3(0f, 0.05f, 0f);
            plate.localScale = new Vector3(0.74f, 0.018f, 0.74f);

            for (int i = 0; i < 5; i++)
            {
                var spike = new GameObject("Spike");
                spike.transform.SetParent(t.root, false);
                float x = i == 0 ? 0f : (i % 2 == 1 ? -0.19f : 0.19f);
                float z = i == 0 ? 0f : (i <= 2 ? -0.19f : 0.19f);
                spike.transform.localPosition = new Vector3(x, 0.066f, z);
                if (i == 0) spike.transform.localScale = new Vector3(1.15f, 1.2f, 1.15f);
                spike.AddComponent<MeshFilter>().sharedMesh = m_SpikeMesh;
                var r = spike.AddComponent<MeshRenderer>();
                r.sharedMaterial = m_SpikeMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            return t;
        }

        /// <summary>The trap on this cell has snapped shut: it shrinks away.</summary>
        public void RemoveTrap(Vector2Int cell)
        {
            for (int i = 0; i < m_Traps.Count; i++)
                if (m_Traps[i].cell == cell && m_Traps[i].leaving < 0f) m_Traps[i].leaving = 0f;
        }

        /// <summary>A four-sided spike standing on y = 0.</summary>
        static Mesh BuildSpike(float half, float height)
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var tris = new List<int>();
            Vector3 apex = new Vector3(0f, height, 0f);
            Vector3[] corner =
            {
                new Vector3(-half, 0f, -half), new Vector3(-half, 0f, half),
                new Vector3(half, 0f, half), new Vector3(half, 0f, -half)
            };
            for (int i = 0; i < 4; i++)
            {
                Vector3 a = corner[i], b = corner[(i + 1) % 4];
                Vector3 n = Vector3.Cross(a - apex, b - apex);
                if (n.y < 0f)
                {
                    Vector3 swap = a; a = b; b = swap;
                    n = -n;
                }
                int at = verts.Count;
                verts.Add(apex); verts.Add(a); verts.Add(b);
                for (int k = 0; k < 3; k++) normals.Add(n.normalized);
                tris.Add(at); tris.Add(at + 1); tris.Add(at + 2);
            }
            var mesh = new Mesh { name = "Spike" };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
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

            for (int i = m_Traps.Count - 1; i >= 0; i--)
            {
                Trap t = m_Traps[i];
                if (t.leaving >= 0f)
                {
                    t.leaving += dt / 0.22f;
                    if (t.leaving >= 1f)
                    {
                        Destroy(t.root.gameObject);
                        m_Traps.RemoveAt(i);
                        continue;
                    }
                    float k = 1f - t.leaving;
                    t.root.localScale = new Vector3(k, k * 1.6f, k);
                    continue;
                }
                // The spikes breathe a little, so a trap never looks like a harmless floor pattern.
                float beat = Mathf.Sin(Time.time * 3.2f + t.phase);
                t.root.localScale = new Vector3(1f, 1f + 0.14f * beat, 1f);
            }

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
            if (m_PlateMaterial != null) Destroy(m_PlateMaterial);
            if (m_SpikeMaterial != null) Destroy(m_SpikeMaterial);
        }
    }
}
