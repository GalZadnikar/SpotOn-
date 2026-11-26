using System.Collections.Generic;
using UnityEngine;

namespace ExpandPuzzle
{
    /// <summary>Everything on the board that can change while a level is played.</summary>
    public class BoardState
    {
        /// <summary>The blob: cell -> colour.</summary>
        public readonly Dictionary<Vector2Int, int> cells;
        /// <summary>Loose body pieces lying on the floor: cell -> colour. They join the blob when it pushes into them.</summary>
        public readonly Dictionary<Vector2Int, int> loose = new Dictionary<Vector2Int, int>();
        /// <summary>Traps that are still armed.</summary>
        public readonly HashSet<Vector2Int> traps = new HashSet<Vector2Int>();
        /// <summary>Movable walls. The index never changes, so a view can follow one wall around.</summary>
        public readonly List<Vector2Int> blocks = new List<Vector2Int>();

        public BoardState()
        {
            cells = new Dictionary<Vector2Int, int>();
        }

        /// <summary>Wraps a blob dictionary that somebody else owns (the game keeps its own).</summary>
        public BoardState(Dictionary<Vector2Int, int> blob)
        {
            cells = blob;
        }

        /// <summary>The position a level starts in.</summary>
        public static BoardState Start(LevelData lvl)
        {
            var s = new BoardState();
            for (int i = 0; i < lvl.startCells.Count; i++)
                s.cells[lvl.startCells[i]] = i < lvl.startColors.Count ? lvl.startColors[i] : 0;
            s.ResetPieces(lvl);
            return s;
        }

        /// <summary>Puts walls, loose pieces and traps back where the level has them. The blob is left alone.</summary>
        public void ResetPieces(LevelData lvl)
        {
            loose.Clear();
            foreach (var l in lvl.loose) loose[l.Key] = l.Value;
            traps.Clear();
            foreach (var t in lvl.traps) traps.Add(t);
            blocks.Clear();
            blocks.AddRange(lvl.blocks);
        }

        public BoardState Clone()
        {
            var s = new BoardState(new Dictionary<Vector2Int, int>(cells));
            s.CopyPiecesFrom(this);
            return s;
        }

        public void CopyPiecesFrom(BoardState other)
        {
            loose.Clear();
            foreach (var l in other.loose) loose[l.Key] = l.Value;
            traps.Clear();
            foreach (var t in other.traps) traps.Add(t);
            blocks.Clear();
            blocks.AddRange(other.blocks);
        }
    }

    public enum StepKind
    {
        /// <summary>Nothing changed.</summary>
        None,
        /// <summary>The blob walked one cell.</summary>
        Move,
        /// <summary>A pump was pressed and something came of it.</summary>
        Pump,
        /// <summary>The blob pushed into loose body and took it in (without walking).</summary>
        Merge
    }

    /// <summary>What one swipe did.</summary>
    public class StepResult
    {
        public StepKind kind = StepKind.None;
        /// <summary>The pump that was pushed (-1 = none). Set even when the press changed nothing.</summary>
        public int pump = -1;
        /// <summary>Cells the pump added.</summary>
        public readonly List<KeyValuePair<Vector2Int, int>> added = new List<KeyValuePair<Vector2Int, int>>();
        /// <summary>Loose cells that joined the blob.</summary>
        public readonly List<KeyValuePair<Vector2Int, int>> merged = new List<KeyValuePair<Vector2Int, int>>();
        /// <summary>Blob cells a trap destroyed: the trap's cell and the colour that was lost.</summary>
        public readonly List<KeyValuePair<Vector2Int, int>> popped = new List<KeyValuePair<Vector2Int, int>>();
    }

    /// <summary>
    /// The rules for a whole board: the blob together with movable walls, loose body and traps.
    /// The game and the solver both play through <see cref="Step"/>, so they can never disagree.
    ///
    ///  - Movable wall: solid for walking and for pump growth. The player drags it (one move) to any
    ///    free floor cell it can slide to.
    ///  - Loose body: when a swipe would push a blob cell into it, the blob stays where it is and the
    ///    whole loose piece joins it. Pump growth that reaches a loose piece takes it in as well.
    ///  - Trap: the blob cell that lands on it (by walking or by growing) is destroyed and the trap
    ///    is used up. A step that would destroy the whole blob is not allowed.
    /// </summary>
    public static class Play
    {
        /// <summary>
        /// May a movable wall stand on a target or a forbidden tile? Off: a wall parked on a forbidden tile
        /// would shield it, and then "grow huge, cover everything" solves most levels.
        /// </summary>
        public static bool WallsOnMarkedTiles = false;

        /// <summary>Nothing of the blob can enter this cell.</summary>
        public static bool Solid(LevelData lvl, BoardState s, Vector2Int c)
        {
            return !lvl.IsFloor(c) || s.blocks.Contains(c);
        }

        /// <summary>A movable wall may stand here.</summary>
        public static bool BlockFree(LevelData lvl, BoardState s, Vector2Int c)
        {
            return lvl.IsFloor(c) && !s.cells.ContainsKey(c) && !s.loose.ContainsKey(c)
                   && !s.traps.Contains(c) && !s.blocks.Contains(c)
                   && (WallsOnMarkedTiles || (!lvl.targets.ContainsKey(c) && !lvl.forbidden.Contains(c)));
        }

        /// <summary>Every cell a movable wall can be dragged to: it slides over free floor, never through anything.</summary>
        public static void Reachable(LevelData lvl, BoardState s, int block, List<Vector2Int> into)
        {
            into.Clear();
            if (block < 0 || block >= s.blocks.Count) return;
            Vector2Int from = s.blocks[block];
            var seen = new HashSet<Vector2Int>();
            var queue = new Queue<Vector2Int>();
            seen.Add(from);
            queue.Enqueue(from);
            while (queue.Count > 0)
            {
                Vector2Int c = queue.Dequeue();
                for (int d = 0; d < Rules.Dirs.Length; d++)
                {
                    Vector2Int n = c + Rules.Dirs[d];
                    if (seen.Contains(n) || !BlockFree(lvl, s, n)) continue;
                    seen.Add(n);
                    into.Add(n);
                    queue.Enqueue(n);
                }
            }
        }

        /// <summary>
        /// One swipe. Changes <paramref name="s"/> in place and says what happened.
        /// Order of checks: walk if the way is free, otherwise take in loose body that is in the way,
        /// otherwise press a pump that is being pushed (unless <paramref name="allowPump"/> is off).
        /// </summary>
        public static StepResult Step(LevelData lvl, BoardState s, Vector2Int dir, bool allowPump = true)
        {
            var r = new StepResult();
            bool solid = false, hitLoose = false;
            int survivors = 0;
            foreach (var c in s.cells)
            {
                Vector2Int n = c.Key + dir;
                if (Solid(lvl, s, n)) solid = true;
                else if (s.loose.ContainsKey(n)) hitLoose = true;
                if (!s.traps.Contains(n)) survivors++;
            }

            if (!solid && !hitLoose)
            {
                if (survivors == 0) return r;   // the traps would leave nothing: not allowed

                var moved = new List<KeyValuePair<Vector2Int, int>>(s.cells.Count);
                foreach (var c in s.cells) moved.Add(new KeyValuePair<Vector2Int, int>(c.Key + dir, c.Value));
                s.cells.Clear();
                for (int i = 0; i < moved.Count; i++)
                {
                    if (s.traps.Remove(moved[i].Key)) r.popped.Add(moved[i]);
                    else s.cells[moved[i].Key] = moved[i].Value;
                }
                r.kind = StepKind.Move;
                return r;
            }

            if (hitLoose)
            {
                var seeds = new List<Vector2Int>();
                foreach (var c in s.cells)
                    if (s.loose.ContainsKey(c.Key + dir)) seeds.Add(c.Key + dir);
                Absorb(s, seeds, r.merged);
                r.kind = StepKind.Merge;
                return r;
            }

            r.pump = Rules.FindPushedPump(lvl, s.cells, dir);
            if (r.pump < 0 || !allowPump) return r;

            PumpData pump = lvl.pumps[r.pump];
            var touched = new List<Vector2Int>();
            foreach (var c in s.cells)
            {
                Vector2Int n = c.Key + pump.dir;
                if (s.cells.ContainsKey(n) || Solid(lvl, s, n)) continue;
                if (s.loose.ContainsKey(n)) { touched.Add(n); continue; }
                var grown = new KeyValuePair<Vector2Int, int>(n, pump.color >= 0 ? pump.color : c.Value);
                if (s.traps.Contains(n)) r.popped.Add(grown);
                else r.added.Add(grown);
            }
            for (int i = 0; i < r.added.Count; i++) s.cells[r.added[i].Key] = r.added[i].Value;
            for (int i = 0; i < r.popped.Count; i++) s.traps.Remove(r.popped[i].Key);
            Absorb(s, touched, r.merged);

            if (r.added.Count + r.popped.Count + r.merged.Count > 0) r.kind = StepKind.Pump;
            return r;
        }

        /// <summary>Drags a movable wall to another cell. False (and nothing changes) when it cannot get there.</summary>
        public static bool MoveBlock(LevelData lvl, BoardState s, int block, Vector2Int to)
        {
            var reach = new List<Vector2Int>();
            Reachable(lvl, s, block, reach);
            if (!reach.Contains(to)) return false;
            s.blocks[block] = to;
            return true;
        }

        /// <summary>The loose pieces that contain the given cells join the blob, every connected cell of them.</summary>
        static void Absorb(BoardState s, List<Vector2Int> seeds, List<KeyValuePair<Vector2Int, int>> into)
        {
            var queue = new Queue<Vector2Int>();
            for (int i = 0; i < seeds.Count; i++) queue.Enqueue(seeds[i]);
            while (queue.Count > 0)
            {
                Vector2Int c = queue.Dequeue();
                int color;
                if (!s.loose.TryGetValue(c, out color)) continue;
                s.loose.Remove(c);
                s.cells[c] = color;
                into.Add(new KeyValuePair<Vector2Int, int>(c, color));
                for (int d = 0; d < Rules.Dirs.Length; d++) queue.Enqueue(c + Rules.Dirs[d]);
            }
        }
    }
}
