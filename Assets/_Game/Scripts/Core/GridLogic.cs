using System.Collections.Generic;
using UnityEngine;

namespace ExpandPuzzle
{
    /// <summary>A pump sits inside a wall cell and blows air in <see cref="dir"/> (towards the floor).</summary>
    [System.Serializable]
    public struct PumpData
    {
        public Vector2Int cell;
        public Vector2Int dir;
        /// <summary>-1 = plain pump (new cells copy the cell they grew from). 0+ = paints new cells that colour.</summary>
        public int color;

        /// <summary>The floor cell right in front of the pump. A blob cell must stand here to use it.</summary>
        public Vector2Int Nozzle { get { return cell + dir; } }
    }

    /// <summary>What a blob cell is standing on, used for the on-blob feedback marks.</summary>
    public enum CellStatus { Plain, TargetOk, TargetWrongColor, Forbidden }

    /// <summary>
    /// Immutable description of one level. Grid space: x = right, y = up.
    /// Everything that is not floor is solid.
    /// </summary>
    public class LevelData
    {
        public const int AnyColor = -1;

        public string name;
        /// <summary>Fewest moves needed (0 = unknown).</summary>
        public int par;
        public int width;
        public int height;
        public bool[,] floor;
        public readonly List<PumpData> pumps = new List<PumpData>();
        /// <summary>Target cell -> colour it needs (<see cref="AnyColor"/> when any colour will do).</summary>
        public readonly Dictionary<Vector2Int, int> targets = new Dictionary<Vector2Int, int>();
        public readonly HashSet<Vector2Int> forbidden = new HashSet<Vector2Int>();
        /// <summary>Cells the blob starts on. Index 0 is the head (the cell that carries the face).</summary>
        public readonly List<Vector2Int> startCells = new List<Vector2Int>();
        public readonly List<int> startColors = new List<int>();

        /// <summary>Movable walls ('W'): solid like a wall, but the player drags them to another floor cell.</summary>
        public readonly List<Vector2Int> blocks = new List<Vector2Int>();
        /// <summary>Loose blob body ('B'): cell -> colour. It joins the blob when the blob pushes into it.</summary>
        public readonly Dictionary<Vector2Int, int> loose = new Dictionary<Vector2Int, int>();
        /// <summary>Traps ('*'): the blob cell that lands on one is destroyed, and the trap is used up.</summary>
        public readonly HashSet<Vector2Int> traps = new HashSet<Vector2Int>();

        public bool HasPieces { get { return blocks.Count + loose.Count + traps.Count > 0; } }

        public bool UsesColor
        {
            get
            {
                foreach (var p in pumps) if (p.color >= 0) return true;
                foreach (var t in targets) if (t.Value >= 0) return true;
                return false;
            }
        }

        public bool IsFloor(Vector2Int c)
        {
            return c.x >= 0 && c.y >= 0 && c.x < width && c.y < height && floor[c.x, c.y];
        }

        /// <summary>Returns a copy turned a quarter clockwise, so wide levels can stand upright on a phone.</summary>
        public LevelData RotatedClockwise()
        {
            var r = new LevelData();
            r.name = name;
            r.par = par;
            r.width = height;
            r.height = width;
            r.floor = new bool[r.width, r.height];
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                {
                    Vector2Int c = RotateCell(new Vector2Int(x, y));
                    r.floor[c.x, c.y] = floor[x, y];
                }
            foreach (var p in pumps)
                r.pumps.Add(new PumpData { cell = RotateCell(p.cell), dir = new Vector2Int(p.dir.y, -p.dir.x), color = p.color });
            foreach (var t in targets) r.targets[RotateCell(t.Key)] = t.Value;
            foreach (var f in forbidden) r.forbidden.Add(RotateCell(f));
            foreach (var c in startCells) r.startCells.Add(RotateCell(c));
            r.startColors.AddRange(startColors);
            foreach (var wb in blocks) r.blocks.Add(RotateCell(wb));
            foreach (var lb in loose) r.loose[RotateCell(lb.Key)] = lb.Value;
            foreach (var tr in traps) r.traps.Add(RotateCell(tr));
            return r;
        }

        Vector2Int RotateCell(Vector2Int c)
        {
            return new Vector2Int(c.y, width - 1 - c.x);
        }

        /// <summary>
        /// Legend (rows are written top to bottom):
        ///   #  wall            .  floor
        ///   H  blob head       P  extra blob cell
        ///   T  target          X  forbidden tile (must stay uncovered)
        ///   ^ v &lt; &gt;  pump inside a wall, blowing in that direction
        ///   W  movable wall    B  loose blob body (joins the blob when pushed into)
        ///   *  trap (destroys the blob cell that lands on it, then it is gone)
        ///
        /// Optional colour layer, same size: a digit on top of a pump, target or blob cell gives it
        /// that colour (0 = the blob's own colour, 1 = second colour, ...). Anything else = no colour.
        /// </summary>
        public static LevelData Parse(string name, string[] rows, string[] colorRows = null)
        {
            var lvl = new LevelData();
            lvl.name = name;
            lvl.height = rows.Length;
            lvl.width = 0;
            for (int i = 0; i < rows.Length; i++) lvl.width = Mathf.Max(lvl.width, rows[i].Length);
            lvl.floor = new bool[lvl.width, lvl.height];

            bool hasHead = false;
            Vector2Int head = Vector2Int.zero;
            int headColor = 0;
            var body = new List<Vector2Int>();
            var bodyColors = new List<int>();

            for (int r = 0; r < rows.Length; r++)
            {
                int y = lvl.height - 1 - r;
                for (int x = 0; x < rows[r].Length; x++)
                {
                    char ch = rows[r][x];
                    var c = new Vector2Int(x, y);

                    int color = AnyColor;
                    if (colorRows != null && r < colorRows.Length && x < colorRows[r].Length)
                    {
                        char cc = colorRows[r][x];
                        if (cc >= '0' && cc <= '9') color = cc - '0';
                    }

                    switch (ch)
                    {
                        case '.': lvl.floor[x, y] = true; break;
                        case 'H': lvl.floor[x, y] = true; hasHead = true; head = c; headColor = Mathf.Max(0, color); break;
                        case 'P': lvl.floor[x, y] = true; body.Add(c); bodyColors.Add(Mathf.Max(0, color)); break;
                        case 'T': lvl.floor[x, y] = true; lvl.targets[c] = color; break;
                        case 'X': lvl.floor[x, y] = true; lvl.forbidden.Add(c); break;
                        case 'W': lvl.floor[x, y] = true; lvl.blocks.Add(c); break;
                        case 'B': lvl.floor[x, y] = true; lvl.loose[c] = Mathf.Max(0, color); break;
                        case '*': lvl.floor[x, y] = true; lvl.traps.Add(c); break;
                        case '^': lvl.pumps.Add(new PumpData { cell = c, dir = Vector2Int.up, color = color }); break;
                        case 'v': lvl.pumps.Add(new PumpData { cell = c, dir = Vector2Int.down, color = color }); break;
                        case '<': lvl.pumps.Add(new PumpData { cell = c, dir = Vector2Int.left, color = color }); break;
                        case '>': lvl.pumps.Add(new PumpData { cell = c, dir = Vector2Int.right, color = color }); break;
                        default: break; // wall
                    }
                }
            }

            if (hasHead)
            {
                lvl.startCells.Add(head);
                lvl.startColors.Add(headColor);
            }
            lvl.startCells.AddRange(body);
            lvl.startColors.AddRange(bodyColors);
            return lvl;
        }
    }

    /// <summary>
    /// The rules of the game, with no Unity scene dependencies so they can be tested on their own.
    /// The blob is a map of cell -> colour index.
    /// </summary>
    public static class Rules
    {
        public static readonly Vector2Int[] Dirs =
        {
            Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left
        };

        /// <summary>The blob moves as one rigid piece: every cell must land on floor.</summary>
        public static bool CanMove(LevelData lvl, Dictionary<Vector2Int, int> cells, Vector2Int dir)
        {
            foreach (var c in cells)
                if (!lvl.IsFloor(c.Key + dir)) return false;
            return true;
        }

        /// <summary>
        /// Pushing towards a pump while touching its nozzle activates it.
        /// Returns the pump index, or -1 when no pump is being pushed.
        /// (If the blob touches several pumps on the same side, the first one in the level wins.)
        /// </summary>
        public static int FindPushedPump(LevelData lvl, Dictionary<Vector2Int, int> cells, Vector2Int inputDir)
        {
            for (int i = 0; i < lvl.pumps.Count; i++)
            {
                var p = lvl.pumps[i];
                if (p.dir == -inputDir && cells.ContainsKey(p.Nozzle)) return i;
            }
            return -1;
        }

        /// <summary>
        /// Inflating adds one layer in the pump's direction: every cell tries to spawn a copy one step
        /// further. Copies that would land in a wall are simply not created. A coloured pump paints
        /// the new cells; a plain pump lets them keep the colour of the cell they came from.
        /// </summary>
        public static List<KeyValuePair<Vector2Int, int>> GrowCells(LevelData lvl, Dictionary<Vector2Int, int> cells, PumpData pump)
        {
            var added = new List<KeyValuePair<Vector2Int, int>>();
            foreach (var c in cells)
            {
                var n = c.Key + pump.dir;
                if (!cells.ContainsKey(n) && lvl.IsFloor(n))
                    added.Add(new KeyValuePair<Vector2Int, int>(n, pump.color >= 0 ? pump.color : c.Value));
            }
            return added;
        }

        public static CellStatus StatusOf(LevelData lvl, Vector2Int cell, int color)
        {
            if (lvl.forbidden.Contains(cell)) return CellStatus.Forbidden;
            int need;
            if (lvl.targets.TryGetValue(cell, out need))
                return need < 0 || need == color ? CellStatus.TargetOk : CellStatus.TargetWrongColor;
            return CellStatus.Plain;
        }

        /// <summary>Most moves allowed for a given star count (3, 2). One star has no limit.</summary>
        public static int MoveLimit(LevelData lvl, int stars)
        {
            if (lvl.par <= 0) return int.MaxValue;
            if (stars >= 3) return lvl.par;
            if (stars == 2) return lvl.par + Mathf.Max(2, Mathf.CeilToInt(lvl.par * 0.4f));
            return int.MaxValue;
        }

        public static int StarsFor(LevelData lvl, int moves)
        {
            if (moves <= MoveLimit(lvl, 3)) return 3;
            if (moves <= MoveLimit(lvl, 2)) return 2;
            return 1;
        }

        /// <summary>Solved when every target is covered by the right colour and no forbidden tile is covered.</summary>
        public static bool IsSolved(LevelData lvl, Dictionary<Vector2Int, int> cells)
        {
            if (lvl.targets.Count == 0) return false;
            foreach (var t in lvl.targets)
            {
                int color;
                if (!cells.TryGetValue(t.Key, out color)) return false;
                if (t.Value >= 0 && t.Value != color) return false;
            }
            foreach (var f in lvl.forbidden)
                if (cells.ContainsKey(f)) return false;
            return true;
        }
    }
}
