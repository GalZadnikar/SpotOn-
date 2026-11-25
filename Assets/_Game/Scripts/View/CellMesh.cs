using System.Collections.Generic;
using UnityEngine;

namespace ExpandPuzzle
{
    /// <summary>
    /// Turns a set of grid cells into one soft, rounded solid: outer corners are rounded,
    /// neighbouring cells fuse together and the top edge gets a bevel.
    /// Used for the blob, the board walls, the floor pads and the small props.
    /// Mesh space: grid x -> X, grid y -> Z, height -> Y. A cell is 1x1 and centred on its coordinate.
    /// </summary>
    public static class CellMesh
    {
        [System.Serializable]
        public struct Settings
        {
            [Tooltip("Gap between the solid and the edge of its cells.")]
            public float margin;
            [Tooltip("Radius of the outer (convex) corners.")]
            public float cornerRadius;
            [Tooltip("Y of the bottom of the solid.")]
            public float baseY;
            [Tooltip("Y of the top face.")]
            public float topY;
            [Tooltip("Size of the rounded top edge.")]
            public float bevel;
            [Range(1, 12)] public int cornerSegments;
            [Range(1, 8)] public int bevelSegments;
            [Tooltip("When off every cell is its own little island.")]
            public bool merge;
            [Tooltip("Treat everything outside the given bounds as solid, so the outer rim has no edge.")]
            public bool solidOutside;
            public Vector2Int boundsMin;
            public Vector2Int boundsMax;

            public static Settings Create(float margin, float cornerRadius, float baseY, float topY, float bevel, bool merge)
            {
                return new Settings
                {
                    margin = margin,
                    cornerRadius = cornerRadius,
                    baseY = baseY,
                    topY = topY,
                    bevel = bevel,
                    cornerSegments = 6,
                    bevelSegments = 4,
                    merge = merge
                };
            }
        }

        // Sides in counter-clockwise order (seen from above): E, N, W, S.
        static readonly Vector2Int[] SideDir =
        {
            new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(-1, 0), new Vector2Int(0, -1)
        };

        struct LoopPoint
        {
            public Vector2 pos;      // position on the wall line
            public Vector2 normal;   // outward normal, zero when the point is inside the solid
            public bool concave;     // concave fillet point: radius grows as the bevel moves in
            public bool wallAfter;   // true when the segment to the next point is an exposed wall
        }

        static readonly List<Vector3> s_Verts = new List<Vector3>();
        static readonly List<Vector3> s_Normals = new List<Vector3>();
        static readonly List<Vector2> s_Uvs = new List<Vector2>();
        static readonly List<List<int>> s_SubTris = new List<List<int>>();
        static List<int> s_Tris;
        static readonly List<LoopPoint> s_Loop = new List<LoopPoint>();
        static readonly HashSet<Vector2Int> s_Set = new HashSet<Vector2Int>();
        static Settings s_Settings;

        public static Mesh Build(ICollection<Vector2Int> cells, Settings s, Mesh reuse = null)
        {
            return Build(cells, s, reuse, null, 1);
        }

        /// <param name="submeshOf">Optional: which submesh (material slot) each cell goes to.</param>
        public static Mesh Build(ICollection<Vector2Int> cells, Settings s, Mesh reuse,
            Dictionary<Vector2Int, int> submeshOf, int submeshCount)
        {
            var set = s_Set;
            set.Clear();
            foreach (var c in cells) set.Add(c);
            s_Settings = s;

            submeshCount = Mathf.Max(1, submeshCount);
            while (s_SubTris.Count < submeshCount) s_SubTris.Add(new List<int>());
            for (int i = 0; i < s_SubTris.Count; i++) s_SubTris[i].Clear();

            s_Verts.Clear();
            s_Normals.Clear();
            s_Uvs.Clear();

            int cornerSeg = Mathf.Max(1, s.cornerSegments);
            int bevelSeg = Mathf.Max(1, s.bevelSegments);
            float height = s.topY - s.baseY;
            float bevel = Mathf.Clamp(s.bevel, 0f, height);
            float radius = Mathf.Clamp(s.cornerRadius, 0f, 0.5f - s.margin);

            foreach (var cell in set)
            {
                int sub = 0;
                if (submeshOf != null && submeshOf.TryGetValue(cell, out sub)) sub = Mathf.Clamp(sub, 0, submeshCount - 1);
                s_Tris = s_SubTris[sub];
                BuildLoop(set, cell, s.merge, s.margin, radius, cornerSeg);
                EmitCell(cell, s.baseY, s.topY, bevel, bevelSeg);
            }

            var mesh = reuse != null ? reuse : new Mesh();
            mesh.Clear();
            mesh.indexFormat = s_Verts.Count > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(s_Verts);
            mesh.SetNormals(s_Normals);
            mesh.SetUVs(0, s_Uvs);
            mesh.subMeshCount = submeshCount;
            for (int i = 0; i < submeshCount; i++) mesh.SetTriangles(s_SubTris[i], i);
            mesh.RecalculateBounds();
            return mesh;
        }

        static void BuildLoop(HashSet<Vector2Int> set, Vector2Int cell, bool merge, float margin, float radius, int seg)
        {
            s_Loop.Clear();
            var center = new Vector2(cell.x, cell.y);

            for (int k = 0; k < 4; k++)
            {
                Vector2Int dA = SideDir[k];            // side we arrive along
                Vector2Int dB = SideDir[(k + 1) & 3];  // side we leave along
                Vector2 nA = new Vector2(dA.x, dA.y);
                Vector2 nB = new Vector2(dB.x, dB.y);

                bool openA = !merge || !Solid(set, cell + dA);
                bool openB = !merge || !Solid(set, cell + dB);
                bool openDiag = !merge || !Solid(set, cell + dA + dB);

                Vector2 corner = center + (nA + nB) * 0.5f;

                if (openA && openB)
                {
                    // Convex corner: quarter circle from side A's normal to side B's normal.
                    Vector2 arcCenter = corner - (nA + nB) * (margin + radius);
                    for (int i = 0; i <= seg; i++)
                    {
                        float t = (float)i / seg * Mathf.PI * 0.5f;
                        Vector2 n = nA * Mathf.Cos(t) + nB * Mathf.Sin(t);
                        s_Loop.Add(new LoopPoint { pos = arcCenter + n * radius, normal = n, wallAfter = true });
                    }
                }
                else if (openA && !openB)
                {
                    // Wall A runs straight on into the neighbour on side B.
                    s_Loop.Add(new LoopPoint { pos = corner - nA * margin, normal = nA, wallAfter = false });
                }
                else if (!openA && openB)
                {
                    // Wall B starts here, coming out of the neighbour on side A.
                    s_Loop.Add(new LoopPoint { pos = corner - nB * margin, normal = nB, wallAfter = true });
                }
                else if (openDiag)
                {
                    // Concave corner (inside of an L): small fillet centred on the cell corner.
                    for (int i = 0; i <= seg; i++)
                    {
                        float t = (float)i / seg * Mathf.PI * 0.5f;
                        // Sweeps from -nB to -nA (as seen from the corner).
                        Vector2 dir = -(nB * Mathf.Cos(t) + nA * Mathf.Sin(t));
                        s_Loop.Add(new LoopPoint
                        {
                            pos = corner + dir * margin,
                            normal = -dir,
                            concave = true,
                            wallAfter = i < seg
                        });
                    }
                }
                else
                {
                    // Fully surrounded corner.
                    s_Loop.Add(new LoopPoint { pos = corner, normal = Vector2.zero, wallAfter = false });
                }
            }
        }

        static bool Solid(HashSet<Vector2Int> set, Vector2Int c)
        {
            if (s_Settings.solidOutside &&
                (c.x < s_Settings.boundsMin.x || c.y < s_Settings.boundsMin.y ||
                 c.x > s_Settings.boundsMax.x || c.y > s_Settings.boundsMax.y)) return true;
            return set.Contains(c);
        }

        static void EmitCell(Vector2Int cell, float baseY, float topY, float bevel, int bevelSeg)
        {
            int count = s_Loop.Count;
            int rings = 2 + bevelSeg;           // bottom, wall top, then the bevel steps
            int start = s_Verts.Count;
            float wallTop = topY - bevel;

            for (int i = 0; i < count; i++)
            {
                LoopPoint p = s_Loop[i];
                bool inside = p.normal.sqrMagnitude < 0.0001f;
                Vector3 flat = new Vector3(p.normal.x, 0f, p.normal.y);

                AddVertex(new Vector3(p.pos.x, baseY, p.pos.y), inside ? Vector3.up : flat);
                AddVertex(new Vector3(p.pos.x, wallTop, p.pos.y), inside ? Vector3.up : flat);

                for (int b = 1; b <= bevelSeg; b++)
                {
                    float a = (float)b / bevelSeg * Mathf.PI * 0.5f;
                    float inset = bevel * (1f - Mathf.Cos(a));
                    Vector2 pos = p.pos - p.normal * inset;
                    Vector3 n = inside || b == bevelSeg
                        ? Vector3.up
                        : (flat * Mathf.Cos(a) + Vector3.up * Mathf.Sin(a)).normalized;
                    AddVertex(new Vector3(pos.x, wallTop + bevel * Mathf.Sin(a), pos.y), n);
                }
            }

            int centerIndex = s_Verts.Count;
            AddVertex(new Vector3(cell.x, topY, cell.y), Vector3.up);

            for (int i = 0; i < count; i++)
            {
                int j = (i + 1) % count;
                int vi = start + i * rings;
                int vj = start + j * rings;

                if (s_Loop[i].wallAfter)
                {
                    for (int r = 0; r < rings - 1; r++)
                    {
                        int lo_i = vi + r, hi_i = vi + r + 1;
                        int lo_j = vj + r, hi_j = vj + r + 1;
                        AddTri(lo_i, hi_i, hi_j);
                        AddTri(lo_i, hi_j, lo_j);
                    }
                }

                // Top cap.
                AddTri(centerIndex, vj + rings - 1, vi + rings - 1);
            }
        }

        /// <summary>
        /// Rounds the inside corners of a pit. For every corner of a floor cell that is boxed in by
        /// walls on both sides, this adds a filler piece with a quarter-round face of the given radius.
        /// The filler lines up with walls built by <see cref="Build"/> using the same margin, heights and bevel.
        /// </summary>
        public static Mesh BuildPitCorners(HashSet<Vector2Int> floorCells, Settings wall, float radius)
        {
            s_Verts.Clear();
            s_Normals.Clear();
            s_Uvs.Clear();
            if (s_SubTris.Count == 0) s_SubTris.Add(new List<int>());
            for (int i = 0; i < s_SubTris.Count; i++) s_SubTris[i].Clear();
            s_Tris = s_SubTris[0];

            int seg = Mathf.Max(2, wall.cornerSegments);
            int bevelSeg = Mathf.Max(1, wall.bevelSegments);
            float bevel = wall.bevel;
            float wallTop = wall.topY - bevel;
            int rings = 2 + bevelSeg;

            foreach (var cell in floorCells)
            {
                for (int k = 0; k < 4; k++)
                {
                    Vector2Int dA = SideDir[k];
                    Vector2Int dB = SideDir[(k + 1) & 3];
                    if (floorCells.Contains(cell + dA) || floorCells.Contains(cell + dB) || floorCells.Contains(cell + dA + dB)) continue;

                    Vector2 nA = new Vector2(dA.x, dA.y);
                    Vector2 nB = new Vector2(dB.x, dB.y);
                    Vector2 corner = new Vector2(cell.x, cell.y) + (nA + nB) * 0.5f;
                    Vector2 arcCenter = corner + (nA + nB) * (wall.margin - radius);

                    int start = s_Verts.Count;
                    for (int i = 0; i <= seg; i++)
                    {
                        float t = (float)i / seg * Mathf.PI * 0.5f;
                        Vector2 dir = nA * Mathf.Cos(t) + nB * Mathf.Sin(t);
                        Vector3 inward = new Vector3(-dir.x, 0f, -dir.y);   // faces the pit

                        Vector2 p = arcCenter + dir * radius;
                        AddVertex(new Vector3(p.x, wall.baseY, p.y), inward);
                        AddVertex(new Vector3(p.x, wallTop, p.y), inward);
                        for (int b = 1; b <= bevelSeg; b++)
                        {
                            float a = (float)b / bevelSeg * Mathf.PI * 0.5f;
                            Vector2 q = arcCenter + dir * (radius + bevel * (1f - Mathf.Cos(a)));
                            Vector3 n = b == bevelSeg ? Vector3.up : (inward * Mathf.Cos(a) + Vector3.up * Mathf.Sin(a)).normalized;
                            AddVertex(new Vector3(q.x, wallTop + bevel * Mathf.Sin(a), q.y), n);
                        }
                    }

                    Vector2 capCorner = corner + (nA + nB) * (wall.margin + bevel);
                    int capIndex = s_Verts.Count;
                    AddVertex(new Vector3(capCorner.x, wall.topY, capCorner.y), Vector3.up);

                    for (int i = 0; i < seg; i++)
                    {
                        int vi = start + i * rings;
                        int vj = vi + rings;
                        float mid = (i + 0.5f) / seg * Mathf.PI * 0.5f;
                        Vector2 midDir = nA * Mathf.Cos(mid) + nB * Mathf.Sin(mid);
                        Vector3 facing = new Vector3(-midDir.x, 0.35f, -midDir.y);

                        for (int r = 0; r < rings - 1; r++)
                        {
                            AddTriFacing(vi + r, vi + r + 1, vj + r + 1, facing);
                            AddTriFacing(vi + r, vj + r + 1, vj + r, facing);
                        }
                        AddTriFacing(capIndex, vi + rings - 1, vj + rings - 1, Vector3.up);
                    }
                }
            }

            var mesh = new Mesh();
            mesh.SetVertices(s_Verts);
            mesh.SetNormals(s_Normals);
            mesh.SetUVs(0, s_Uvs);
            mesh.SetTriangles(s_Tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static void AddTriFacing(int a, int b, int c, Vector3 facing)
        {
            Vector3 pa = s_Verts[a], pb = s_Verts[b], pc = s_Verts[c];
            Vector3 n = Vector3.Cross(pb - pa, pc - pa);
            if (n.sqrMagnitude < 1e-12f) return;
            if (Vector3.Dot(n, facing) < 0f) { int tmp = b; b = c; c = tmp; }
            s_Tris.Add(a);
            s_Tris.Add(b);
            s_Tris.Add(c);
        }

        static void AddVertex(Vector3 pos, Vector3 normal)
        {
            s_Verts.Add(pos);
            s_Normals.Add(normal);
            s_Uvs.Add(new Vector2(pos.x, pos.z));
        }

        static void AddTri(int a, int b, int c)
        {
            // Skip triangles that collapsed to a line (zero-radius fillets, shared points).
            Vector3 pa = s_Verts[a], pb = s_Verts[b], pc = s_Verts[c];
            if (Vector3.Cross(pb - pa, pc - pa).sqrMagnitude < 1e-12f) return;
            s_Tris.Add(a);
            s_Tris.Add(b);
            s_Tris.Add(c);
        }
    }
}
