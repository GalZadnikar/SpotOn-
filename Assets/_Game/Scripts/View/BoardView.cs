using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ExpandPuzzle
{
    /// <summary>
    /// Builds the 3D board for a level. The playfield is a pit sunk into one endless surface,
    /// so board and background read as a single piece.
    /// </summary>
    public class BoardView : MonoBehaviour
    {
        public const float WallHeight = 0.34f;
        public const float PitDepth = 0.2f;
        const float SurfaceExtent = 90f;

        class Tile
        {
            public Transform transform;
            public Vector2Int cell;
            public bool isTarget;
            public Transform dot;        // the coloured mark of a target
            public float lift, bulge;    // set by the ripple
            public float sink;           // 0..1, a covered target rests a little lower
            public bool covered;
        }

        Tile m_LastTarget;       // the only target still open: it pulses as a gentle hint
        float m_HintBlend;

        readonly List<PumpView> m_Pumps = new List<PumpView>();
        readonly List<Mesh> m_Meshes = new List<Mesh>();
        readonly List<Tile> m_Tiles = new List<Tile>();
        Coroutine m_Ripple;

        public IList<PumpView> Pumps { get { return m_Pumps; } }
        /// <summary>The playfield (floor) in world space: what the camera centres on.</summary>
        public Bounds WorldBounds { get; private set; }
        /// <summary>Pump bodies in world space: must stay on screen. The supply queues behind them may not.</summary>
        public List<Bounds> PumpBounds { get; private set; }

        /// <summary>
        /// What the camera has to show, in grid units on the ground plane (x, y = grid x, grid y):
        /// the playfield and the pump bodies. Supply queues are decoration and may run off screen.
        /// </summary>
        public static Rect LayoutRect(LevelData level, int supplyBlocks)
        {
            const float margin = 0.22f;
            float xMin = float.MaxValue, yMin = float.MaxValue, xMax = float.MinValue, yMax = float.MinValue;

            for (int x = 0; x < level.width; x++)
                for (int y = 0; y < level.height; y++)
                {
                    if (!level.IsFloor(new Vector2Int(x, y))) continue;
                    xMin = Mathf.Min(xMin, x - 0.5f); xMax = Mathf.Max(xMax, x + 0.5f);
                    yMin = Mathf.Min(yMin, y - 0.5f); yMax = Mathf.Max(yMax, y + 0.5f);
                }
            if (xMin > xMax) { xMin = -0.5f; yMin = -0.5f; xMax = level.width - 0.5f; yMax = level.height - 0.5f; }

            for (int i = 0; i < level.pumps.Count; i++)
            {
                PumpData p = level.pumps[i];
                Vector2 c = new Vector2(p.cell.x, p.cell.y);
                foreach (var q in new[] { c + new Vector2(PumpHalf, PumpHalf), c - new Vector2(PumpHalf, PumpHalf) })
                {
                    xMin = Mathf.Min(xMin, q.x); xMax = Mathf.Max(xMax, q.x);
                    yMin = Mathf.Min(yMin, q.y); yMax = Mathf.Max(yMax, q.y);
                }
            }

            return Rect.MinMaxRect(xMin - margin, yMin - margin, xMax + margin, yMax + margin);
        }

        /// <summary>The supply queue needs solid ground behind the pump: two blocks fit per wall cell.</summary>
        public static int SupplyFor(LevelData level, PumpData data, int supplyBlocks)
        {
            int solidBehind = 0;
            while (solidBehind < 2 && !level.IsFloor(data.cell - data.dir * (solidBehind + 1))) solidBehind++;
            return Mathf.Min(supplyBlocks, solidBehind * 2);
        }

        /// <summary>Half size of a pump body on the ground plane (cell units).</summary>
        const float PumpHalf = 0.45f;

        public static Vector3 CellToLocal(Vector2Int c)
        {
            return new Vector3(c.x, 0f, c.y);
        }

        public Vector3 CellToWorld(Vector2Int c, float y)
        {
            return transform.TransformPoint(new Vector3(c.x, y, c.y));
        }

        GameTheme m_Theme;

        public void Build(LevelData level, GameTheme theme)
        {
            m_Theme = theme;
            var all = new List<Vector2Int>();
            var walls = new List<Vector2Int>();

            Vector2Int min = new Vector2Int(int.MaxValue, int.MaxValue);
            Vector2Int max = new Vector2Int(int.MinValue, int.MinValue);

            for (int x = 0; x < level.width; x++)
            {
                for (int y = 0; y < level.height; y++)
                {
                    var c = new Vector2Int(x, y);
                    all.Add(c);
                    if (level.IsFloor(c))
                    {
                        min = new Vector2Int(Mathf.Min(min.x, x), Mathf.Min(min.y, y));
                        max = new Vector2Int(Mathf.Max(max.x, x), Mathf.Max(max.y, y));
                    }
                    else
                    {
                        walls.Add(c);
                    }
                }
            }

            // The surface: wall cells with a rimless outer edge, continued by a huge flat frame.
            var wallSettings = CellMesh.Settings.Create(0.04f, 0.3f, -PitDepth, WallHeight, 0.07f, true);
            wallSettings.solidOutside = true;
            wallSettings.boundsMin = Vector2Int.zero;
            wallSettings.boundsMax = new Vector2Int(level.width - 1, level.height - 1);
            AddMesh("Surface", CellMesh.Build(walls, wallSettings), theme.boardSurface, true, Vector3.zero);
            AddMesh("Surface Frame", BuildFrame(level.width, level.height), theme.boardSurface, false, Vector3.zero);

            var floorSet = new HashSet<Vector2Int>();
            foreach (var c in all) if (level.IsFloor(c)) floorSet.Add(c);
            AddMesh("Pit Corners", CellMesh.BuildPitCorners(floorSet, wallSettings, 0.22f), theme.boardSurface, true, Vector3.zero);

            // Bottom of the pit.
            var pitSettings = CellMesh.Settings.Create(0f, 0f, -PitDepth, 0f, 0f, true);
            pitSettings.solidOutside = true;
            pitSettings.boundsMin = wallSettings.boundsMin;
            pitSettings.boundsMax = wallSettings.boundsMax;
            AddMesh("Pit", CellMesh.Build(all, pitSettings), theme.boardPit, false, Vector3.zero);

            // Floor tiles are separate objects (sharing one mesh each) so they can ripple.
            var origin = new[] { Vector2Int.zero };
            Mesh padMesh = Keep(CellMesh.Build(origin, CellMesh.Settings.Create(0.075f, 0.16f, 0f, 0.035f, 0.02f, false)), "Pad");
            Mesh tileMesh = Keep(CellMesh.Build(origin, CellMesh.Settings.Create(0.075f, 0.16f, 0f, 0.05f, 0.02f, false)), "Tile");
            Mesh dotMesh = Keep(CellMesh.Build(origin, CellMesh.Settings.Create(0.27f, 0.23f, 0f, 0.085f, 0.025f, false)), "Dot");

            var tiles = new GameObject("Tiles").transform;
            tiles.SetParent(transform, false);

            for (int x = 0; x < level.width; x++)
            {
                for (int y = 0; y < level.height; y++)
                {
                    var c = new Vector2Int(x, y);
                    if (!level.IsFloor(c)) continue;

                    int need;
                    Transform tile;
                    Transform dot = null;
                    if (level.targets.TryGetValue(c, out need))
                    {
                        tile = Part("Target", tiles, tileMesh, theme.target);
                        dot = Part("Dot", tile, dotMesh, theme.MarkMaterial(need));
                    }
                    else if (level.forbidden.Contains(c))
                    {
                        tile = Part("Forbidden", tiles, tileMesh, theme.forbidden);
                        AddCross(tile, theme.forbiddenMark);
                    }
                    else
                    {
                        tile = Part("Pad", tiles, padMesh, theme.floorPad);
                    }

                    tile.localPosition = CellToLocal(c);
                    m_Tiles.Add(new Tile { transform = tile, cell = c, isTarget = dot != null, dot = dot });
                }
            }

            for (int i = 0; i < level.pumps.Count; i++)
            {
                PumpData data = level.pumps[i];
                int supply = SupplyFor(level, data, theme.pumpSupplyBlocks);

                var go = new GameObject("Pump " + i);
                go.transform.SetParent(transform, false);
                var pump = go.AddComponent<PumpView>();
                pump.Build(data, theme, level.startColors.Count > 0 ? level.startColors[0] : 0, supply);
                m_Pumps.Add(pump);
            }

            // Frame exactly what is on show: playfield, pumps and queues. Height covers the blob.
            // The camera centres on the playfield; pumps only have to stay on screen.
            if (min.x > max.x) { min = Vector2Int.zero; max = new Vector2Int(level.width - 1, level.height - 1); }
            const float edge = 0.62f;   // half a cell plus a sliver of the surrounding surface
            Vector3 lo = new Vector3(min.x - edge, 0f, min.y - edge);
            Vector3 hi = new Vector3(max.x + edge, 0.72f, max.y + edge);
            WorldBounds = new Bounds(transform.TransformPoint((lo + hi) * 0.5f), hi - lo);

            PumpBounds = new List<Bounds>();
            for (int i = 0; i < level.pumps.Count; i++)
            {
                Vector3 pc = CellToLocal(level.pumps[i].cell) + new Vector3(0f, 0.6f, 0f);
                PumpBounds.Add(new Bounds(transform.TransformPoint(pc), new Vector3(PumpHalf * 2f, 0.9f, PumpHalf * 2f)));
            }
        }

        // ---------------------------------------------------------------- pieces

        Mesh Keep(Mesh mesh, string name)
        {
            mesh.name = name;
            m_Meshes.Add(mesh);
            return mesh;
        }

        void AddMesh(string name, Mesh mesh, Material material, bool castShadows, Vector3 localPos)
        {
            Keep(mesh, name);
            var t = Part(name, transform, mesh, material);
            t.localPosition = localPos;
            t.GetComponent<MeshRenderer>().shadowCastingMode = castShadows
                ? UnityEngine.Rendering.ShadowCastingMode.On
                : UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        static Transform Part(string name, Transform parent, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        static void AddCross(Transform tile, Material material)
        {
            for (int i = 0; i < 2; i++)
            {
                var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                bar.name = "Cross";
                StripCollider(bar);
                bar.transform.SetParent(tile, false);
                bar.transform.localPosition = new Vector3(0f, 0.065f, 0f);
                bar.transform.localRotation = Quaternion.Euler(0f, i == 0 ? 45f : -45f, 0f);
                bar.transform.localScale = new Vector3(0.54f, 0.05f, 0.14f);
                var r = bar.GetComponent<MeshRenderer>();
                r.sharedMaterial = material;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        /// <summary>Four big quads around the level rectangle, level with the top of the walls.</summary>
        static Mesh BuildFrame(int width, int height)
        {
            float x0 = -0.5f, x1 = width - 0.5f, z0 = -0.5f, z1 = height - 0.5f;
            float e = SurfaceExtent;
            float y = WallHeight;

            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var tris = new List<int>();
            AddQuad(verts, normals, tris, x0 - e, z0 - e, x1 + e, z0, y);   // front
            AddQuad(verts, normals, tris, x0 - e, z1, x1 + e, z1 + e, y);   // back
            AddQuad(verts, normals, tris, x0 - e, z0, x0, z1, y);           // left
            AddQuad(verts, normals, tris, x1, z0, x1 + e, z1, y);           // right

            var mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static void AddQuad(List<Vector3> verts, List<Vector3> normals, List<int> tris,
            float xa, float za, float xb, float zb, float y)
        {
            int i = verts.Count;
            verts.Add(new Vector3(xa, y, za));
            verts.Add(new Vector3(xa, y, zb));
            verts.Add(new Vector3(xb, y, zb));
            verts.Add(new Vector3(xb, y, za));
            for (int k = 0; k < 4; k++) normals.Add(Vector3.up);
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
            tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
        }

        public static void StripCollider(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
        }

        // ---------------------------------------------------------------- juice

        /// <summary>A wave runs through the floor tiles, starting at <paramref name="origin"/>.</summary>
        public void Ripple(Vector2Int origin, float strength, float speed)
        {
            if (m_Ripple != null) StopCoroutine(m_Ripple);
            m_Ripple = StartCoroutine(RippleRoutine(origin, strength, speed));
        }

        IEnumerator RippleRoutine(Vector2Int origin, float strength, float speed)
        {
            const float bump = 0.32f;
            float maxDist = 0f;
            for (int i = 0; i < m_Tiles.Count; i++)
                maxDist = Mathf.Max(maxDist, Vector2.Distance(m_Tiles[i].cell, origin));

            float total = maxDist / speed + bump;
            float t = 0f;
            while (t < total)
            {
                t += Time.deltaTime;
                for (int i = 0; i < m_Tiles.Count; i++)
                {
                    Tile tile = m_Tiles[i];
                    float local = (t - Vector2.Distance(tile.cell, origin) / speed) / bump;
                    float k = local > 0f && local < 1f ? Mathf.Sin(local * Mathf.PI) : 0f;
                    tile.lift = k * strength;
                    tile.bulge = k;
                }
                yield return null;
            }

            for (int i = 0; i < m_Tiles.Count; i++)
            {
                m_Tiles[i].lift = 0f;
                m_Tiles[i].bulge = 0f;
            }
            m_Ripple = null;
        }

        /// <summary>
        /// Tells the board which targets are covered with the right colour. Covered targets rest a little
        /// lower; when exactly one is still open (and <paramref name="hint"/> is on) it pulses.
        /// </summary>
        public void SetCovered(ICollection<Vector2Int> covered, bool hint)
        {
            Tile open = null;
            int openCount = 0;
            for (int i = 0; i < m_Tiles.Count; i++)
            {
                Tile tile = m_Tiles[i];
                if (!tile.isTarget) continue;
                tile.covered = covered != null && covered.Contains(tile.cell);
                if (!tile.covered) { open = tile; openCount++; }
            }
            m_LastTarget = hint && openCount == 1 ? open : null;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            m_HintBlend = Mathf.MoveTowards(m_HintBlend, m_LastTarget != null ? 1f : 0f, dt * 3f);
            float beat = 0.5f + 0.5f * Mathf.Sin(Time.time * 5.2f);

            for (int i = 0; i < m_Tiles.Count; i++)
            {
                Tile tile = m_Tiles[i];
                if (tile.transform == null) continue;

                float hint = 0f;
                if (tile.isTarget)
                {
                    tile.sink = Mathf.MoveTowards(tile.sink, tile.covered ? 1f : 0f, dt * 7f);
                    if (tile == m_LastTarget) hint = m_HintBlend * beat;
                    if (tile.dot != null)
                    {
                        float d = 1f + hint * 0.22f;
                        tile.dot.localScale = new Vector3(d, 1f + hint * 0.8f, d);
                    }
                }

                Vector3 p = CellToLocal(tile.cell);
                p.y = tile.lift - tile.sink * 0.022f + hint * 0.03f;
                tile.transform.localPosition = p;
                float s = 1f + tile.bulge * 0.08f + hint * 0.035f;
                tile.transform.localScale = new Vector3(s, 1f + tile.bulge * 0.6f, s);
            }
        }

        // ---------------------------------------------------------------- boosters

        /// <summary>A forbidden tile turns into plain floor: its cross flies off and the tile changes colour.</summary>
        public void ClearForbidden(Vector2Int cell)
        {
            for (int i = 0; i < m_Tiles.Count; i++)
            {
                Tile tile = m_Tiles[i];
                if (tile.cell != cell || tile.transform == null) continue;
                var renderer = tile.transform.GetComponent<MeshRenderer>();
                if (renderer != null && m_Theme != null) renderer.sharedMaterial = m_Theme.floorPad;
                int n = 0;
                foreach (Transform child in tile.transform)
                    StartCoroutine(FlyOff(child, n++ == 0 ? 1f : -1f));
                return;
            }
        }

        IEnumerator FlyOff(Transform part, float side)
        {
            Vector3 start = part.localPosition, scale = part.localScale;
            Quaternion rotation = part.localRotation;
            float t = 0f;
            while (t < 1f && part != null)
            {
                t += Time.deltaTime / 0.45f;
                float k = Mathf.Clamp01(t);
                part.localPosition = start + new Vector3(side * 0.5f * k, 1.6f * k - 1.5f * k * k, 0.25f * k);
                part.localRotation = rotation * Quaternion.Euler(k * 540f * side, k * 300f, 0f);
                part.localScale = scale * (1f - k * k);
                yield return null;
            }
            if (part != null) Destroy(part.gameObject);
        }

        readonly List<Transform> m_Markers = new List<Transform>();
        readonly List<float> m_MarkerHeight = new List<float>();
        int m_MarkersShown;

        /// <summary>
        /// Shows a bouncing white dot on every cell the player may tap (picking a booster target).
        /// <paramref name="heights"/> gives the height of each dot above the floor.
        /// </summary>
        public void ShowMarkers(IList<Vector2Int> cells, IList<float> heights)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                if (i >= m_Markers.Count)
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    go.name = "Tap Marker";
                    StripCollider(go);
                    go.transform.SetParent(transform, false);
                    var r = go.GetComponent<MeshRenderer>();
                    r.sharedMaterial = m_Theme != null ? m_Theme.eyeShine : null;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.receiveShadows = false;
                    m_Markers.Add(go.transform);
                    m_MarkerHeight.Add(0f);
                }
                m_Markers[i].gameObject.SetActive(true);
                m_Markers[i].localPosition = new Vector3(cells[i].x, heights[i], cells[i].y);
                m_Markers[i].localScale = Vector3.zero;
                m_MarkerHeight[i] = heights[i];
            }
            for (int i = cells.Count; i < m_Markers.Count; i++) m_Markers[i].gameObject.SetActive(false);
            m_MarkersShown = cells.Count;
        }

        public void HideMarkers()
        {
            for (int i = 0; i < m_Markers.Count; i++) m_Markers[i].gameObject.SetActive(false);
            m_MarkersShown = 0;
        }

        void Update()
        {
            // The tap markers breathe, one after another, so the eye is drawn across all of them.
            for (int i = 0; i < m_MarkersShown; i++)
            {
                Transform m = m_Markers[i];
                float beat = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6.5f - i * 0.7f);
                float size = Mathf.Lerp(m.localScale.x, 0.3f + 0.12f * beat, Time.unscaledDeltaTime * 14f);
                m.localScale = new Vector3(size, 0.006f, size);
                Vector3 p = m.localPosition;
                p.y = m_MarkerHeight[i] + 0.03f * beat;
                m.localPosition = p;
            }
        }

        void OnDestroy()
        {
            for (int i = 0; i < m_Meshes.Count; i++)
                if (m_Meshes[i] != null) Destroy(m_Meshes[i]);
        }
    }
}
