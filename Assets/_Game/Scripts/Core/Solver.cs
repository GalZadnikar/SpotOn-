using System.Collections.Generic;
using UnityEngine;

namespace ExpandPuzzle
{
    /// <summary>One move of a solution: a swipe, or dragging a movable wall to another cell.</summary>
    public struct SolveMove
    {
        /// <summary>Direction of the swipe (zero when this move is a wall drag).</summary>
        public Vector2Int dir;
        /// <summary>-1 = a swipe. Otherwise the index of the movable wall that is dragged.</summary>
        public int block;
        /// <summary>Where the wall is dragged to.</summary>
        public Vector2Int to;

        public static SolveMove Swipe(Vector2Int dir)
        {
            return new SolveMove { dir = dir, block = -1 };
        }

        public static SolveMove Drag(int block, Vector2Int to)
        {
            return new SolveMove { block = block, to = to };
        }
    }

    /// <summary>What the solver found for one position.</summary>
    public class SolveResult
    {
        /// <summary>True when a winning line exists from the given position.</summary>
        public bool solvable;
        /// <summary>Fewest moves to win (only meaningful when <see cref="solvable"/>).</summary>
        public int par;
        /// <summary>The swipes of one shortest solution, in order (zero where the move is a wall drag).</summary>
        public readonly List<Vector2Int> moves = new List<Vector2Int>();
        /// <summary>The same solution with the wall drags spelled out.</summary>
        public readonly List<SolveMove> steps = new List<SolveMove>();
        /// <summary>How many different positions were looked at.</summary>
        public int states;
        /// <summary>False when the search hit the state limit before finishing (result then not proven).</summary>
        public bool complete = true;
    }

    /// <summary>
    /// Breadth-first search over board positions. It plays through <see cref="Play.Step"/> exactly the
    /// way the game does, and tries every place a movable wall can be dragged to, so it can never
    /// disagree with the game. Use it to check levels, to get the true par, and to produce hints.
    /// </summary>
    public static class Solver
    {
        struct Node
        {
            public BoardState state;
            public int parent;
            public SolveMove move;
            public int depth;
        }

        /// <summary>Solves a level from its starting position.</summary>
        public static SolveResult Solve(LevelData lvl, int maxStates = 200000)
        {
            return Solve(lvl, BoardState.Start(lvl), maxStates);
        }

        /// <summary>Solves from a blob position, with walls, loose pieces and traps where the level starts them.</summary>
        public static SolveResult Solve(LevelData lvl, Dictionary<Vector2Int, int> startCells, int maxStates = 200000)
        {
            var start = new BoardState(new Dictionary<Vector2Int, int>(startCells));
            start.ResetPieces(lvl);
            return Solve(lvl, start, maxStates);
        }

        /// <summary>Solves from any position, e.g. the current one when the player asks for a hint.</summary>
        public static SolveResult Solve(LevelData lvl, BoardState start, int maxStates = 200000)
        {
            var result = new SolveResult();
            var nodes = new List<Node>();
            var seen = new HashSet<string>();
            var key = new char[lvl.width * lvl.height];
            var reach = new List<Vector2Int>();

            BoardState first = start.Clone();
            nodes.Add(new Node { state = first, parent = -1, depth = 0 });
            seen.Add(KeyOf(lvl, first, key));

            if (Rules.IsSolved(lvl, first.cells))
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
                    BoardState next = node.state.Clone();
                    if (Play.Step(lvl, next, Rules.Dirs[d]).kind == StepKind.None) continue;
                    if (Add(lvl, nodes, seen, key, next, head, SolveMove.Swipe(Rules.Dirs[d]), node.depth + 1, result, maxStates)) return result;
                }

                for (int b = 0; b < node.state.blocks.Count; b++)
                {
                    Play.Reachable(lvl, node.state, b, reach);
                    KeyOf(lvl, node.state, key);   // the key of a drag is the parent's key with one wall moved
                    int from = node.state.blocks[b].y * lvl.width + node.state.blocks[b].x;
                    for (int i = 0; i < reach.Count; i++)
                    {
                        // Seen before? Then the position does not have to be built at all.
                        int to = reach[i].y * lvl.width + reach[i].x;
                        key[from] = '.';
                        key[to] = 'W';
                        string moved = new string(key);
                        key[to] = '.';
                        key[from] = 'W';
                        if (seen.Contains(moved)) continue;

                        BoardState next = node.state.Clone();
                        next.blocks[b] = reach[i];
                        bool done = Add(lvl, nodes, seen, key, next, head, SolveMove.Drag(b, reach[i]), node.depth + 1, result, maxStates);
                        key[to] = '.';      // Add left the new position's key in the buffer: back to the parent's
                        key[from] = 'W';
                        if (done) return result;
                    }
                }
            }

            result.states = nodes.Count;
            return result;
        }

        /// <summary>Queues a new position. True when the search is over (solved, or the limit is reached).</summary>
        static bool Add(LevelData lvl, List<Node> nodes, HashSet<string> seen, char[] key, BoardState next,
            int parent, SolveMove move, int depth, SolveResult result, int maxStates)
        {
            if (!seen.Add(KeyOf(lvl, next, key))) return false;
            nodes.Add(new Node { state = next, parent = parent, move = move, depth = depth });

            if (Rules.IsSolved(lvl, next.cells))
            {
                result.solvable = true;
                result.par = depth;
                result.states = nodes.Count;
                for (int n = nodes.Count - 1; nodes[n].parent >= 0; n = nodes[n].parent)
                    result.steps.Add(nodes[n].move);
                result.steps.Reverse();
                for (int i = 0; i < result.steps.Count; i++)
                    result.moves.Add(result.steps[i].block < 0 ? result.steps[i].dir : Vector2Int.zero);
                return true;
            }

            if (nodes.Count >= maxStates)
            {
                result.complete = false;
                result.states = nodes.Count;
                return true;
            }
            return false;
        }

        /// <summary>The next swipe of a shortest solution from this position, or zero when there is none.</summary>
        public static Vector2Int Hint(LevelData lvl, Dictionary<Vector2Int, int> cells, int maxStates = 200000)
        {
            SolveResult r = Solve(lvl, cells, maxStates);
            return r.solvable && r.moves.Count > 0 ? r.moves[0] : Vector2Int.zero;
        }

        /// <summary>A position as text: two positions are the same exactly when their keys are.</summary>
        static string KeyOf(LevelData lvl, BoardState s, char[] buffer)
        {
            for (int i = 0; i < buffer.Length; i++) buffer[i] = '.';
            foreach (var c in s.cells)
                buffer[c.Key.y * lvl.width + c.Key.x] = (char)('0' + c.Value);
            foreach (var c in s.loose)
                buffer[c.Key.y * lvl.width + c.Key.x] = (char)('a' + c.Value);
            foreach (var t in s.traps)
                buffer[t.y * lvl.width + t.x] = '*';
            for (int i = 0; i < s.blocks.Count; i++)
                buffer[s.blocks[i].y * lvl.width + s.blocks[i].x] = 'W';
            return new string(buffer);
        }
    }
}
