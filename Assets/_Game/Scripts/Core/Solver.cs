using System.Collections.Generic;
using UnityEngine;

namespace ExpandPuzzle
{
    /// <summary>What the solver found for one position.</summary>
    public class SolveResult
    {
        /// <summary>True when a winning line exists from the given position.</summary>
        public bool solvable;
        /// <summary>Fewest moves to win (only meaningful when <see cref="solvable"/>).</summary>
        public int par;
        /// <summary>The swipes of one shortest solution, in order.</summary>
        public readonly List<Vector2Int> moves = new List<Vector2Int>();
        /// <summary>How many different positions were looked at.</summary>
        public int states;
        /// <summary>False when the search hit the state limit before finishing (result then not proven).</summary>
        public bool complete = true;
    }

    /// <summary>
    /// Breadth-first search over blob positions. It plays through <see cref="Rules"/> exactly the way
    /// GameManager.Step does (move if possible, otherwise pump), so it can never disagree with the game.
    /// Use it to check levels, to get the true par, and to produce hints from any position.
    /// </summary>
    public static class Solver
    {
        struct Node
        {
            public Dictionary<Vector2Int, int> cells;
            public int parent;
            public Vector2Int dir;
            public int depth;
        }

        /// <summary>Solves a level from its starting position.</summary>
        public static SolveResult Solve(LevelData lvl, int maxStates = 200000)
        {
            var start = new Dictionary<Vector2Int, int>();
            for (int i = 0; i < lvl.startCells.Count; i++)
                start[lvl.startCells[i]] = i < lvl.startColors.Count ? lvl.startColors[i] : 0;
            return Solve(lvl, start, maxStates);
        }

        /// <summary>Solves from any position, e.g. the current one when the player asks for a hint.</summary>
        public static SolveResult Solve(LevelData lvl, Dictionary<Vector2Int, int> startCells, int maxStates = 200000)
        {
            var result = new SolveResult();
            var nodes = new List<Node>();
            var seen = new HashSet<string>();
            var key = new char[lvl.width * lvl.height];

            var first = new Dictionary<Vector2Int, int>(startCells);
            nodes.Add(new Node { cells = first, parent = -1, depth = 0 });
            seen.Add(KeyOf(lvl, first, key));

            if (Rules.IsSolved(lvl, first))
            {
                result.solvable = true;
                result.states = 1;
                return result;
            }

            for (int head = 0; head < nodes.Count; head++)
            {
                Node node = nodes[head];
                for (int d = 0; d < Rules.Dirs.Length; d++)
                {
                    Vector2Int dir = Rules.Dirs[d];
                    Dictionary<Vector2Int, int> next = Apply(lvl, node.cells, dir);
                    if (next == null) continue;
                    if (!seen.Add(KeyOf(lvl, next, key))) continue;

                    nodes.Add(new Node { cells = next, parent = head, dir = dir, depth = node.depth + 1 });

                    if (Rules.IsSolved(lvl, next))
                    {
                        result.solvable = true;
                        result.par = node.depth + 1;
                        result.states = nodes.Count;
                        for (int n = nodes.Count - 1; nodes[n].parent >= 0; n = nodes[n].parent)
                            result.moves.Add(nodes[n].dir);
                        result.moves.Reverse();
                        return result;
                    }

                    if (nodes.Count >= maxStates)
                    {
                        result.complete = false;
                        result.states = nodes.Count;
                        return result;
                    }
                }
            }

            result.states = nodes.Count;
            return result;
        }

        /// <summary>The next swipe of a shortest solution from this position, or zero when there is none.</summary>
        public static Vector2Int Hint(LevelData lvl, Dictionary<Vector2Int, int> cells, int maxStates = 200000)
        {
            SolveResult r = Solve(lvl, cells, maxStates);
            return r.solvable && r.moves.Count > 0 ? r.moves[0] : Vector2Int.zero;
        }

        /// <summary>One swipe, same order of checks as the game. Null when nothing happens.</summary>
        static Dictionary<Vector2Int, int> Apply(LevelData lvl, Dictionary<Vector2Int, int> cells, Vector2Int dir)
        {
            if (Rules.CanMove(lvl, cells, dir))
            {
                var moved = new Dictionary<Vector2Int, int>(cells.Count);
                foreach (var c in cells) moved[c.Key + dir] = c.Value;
                return moved;
            }

            int pumpIndex = Rules.FindPushedPump(lvl, cells, dir);
            if (pumpIndex < 0) return null;

            List<KeyValuePair<Vector2Int, int>> added = Rules.GrowCells(lvl, cells, lvl.pumps[pumpIndex]);
            if (added.Count == 0) return null;

            var grown = new Dictionary<Vector2Int, int>(cells);
            foreach (var a in added) grown[a.Key] = a.Value;
            return grown;
        }

        static string KeyOf(LevelData lvl, Dictionary<Vector2Int, int> cells, char[] buffer)
        {
            for (int i = 0; i < buffer.Length; i++) buffer[i] = '.';
            foreach (var c in cells)
                buffer[c.Key.y * lvl.width + c.Key.x] = (char)('0' + c.Value);
            return new string(buffer);
        }
    }
}
