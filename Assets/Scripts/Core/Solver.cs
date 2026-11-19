using System;
using System.Collections.Generic;

namespace TiltBox.Core
{
    public sealed class SolveResult
    {
        public bool Solved { get; }

        /// <summary>Fewest swipes that win. -1 when not solved within the depth limit.</summary>
        public int Par { get; }

        /// <summary>One optimal swipe sequence. Empty when not solved or already solved.</summary>
        public IReadOnlyList<Direction> Solution { get; }

        /// <summary>Number of different optimal swipe sequences (§5: designers want ≤ 3).</summary>
        public long OptimalSolutionCount { get; }

        /// <summary>Distinct states visited; useful to watch solver cost as boards grow.</summary>
        public int StatesExplored { get; }

        /// <summary>True when the search stopped at the state cap rather than proving there is no solution.</summary>
        public bool HitStateCap { get; }

        public SolveResult(bool solved, int par, IReadOnlyList<Direction> solution, long optimalCount, int states, bool hitCap)
        {
            Solved = solved;
            Par = par;
            Solution = solution;
            OptimalSolutionCount = optimalCount;
            StatesExplored = states;
            HitStateCap = hitCap;
        }

        /// <summary>The next optimal swipe (the in-game hint), or null.</summary>
        public Direction? FirstMove => Solution.Count > 0 ? Solution[0] : (Direction?)null;
    }

    /// <summary>
    /// Breadth-first search over board states (GDD §5). Ignored swipes are skipped; states are de-duplicated by
    /// <see cref="BoardState.CanonicalKey"/>, so same-colour blocks count as interchangeable.
    /// </summary>
    public static class Solver
    {
        public const int DefaultMaxDepth = 22;
        public const int DefaultMaxStates = 2000000;

        private sealed class Node
        {
            public BoardState State;
            public int Parent;
            public Direction Move;
            public int Depth;
            public long Paths;
        }

        public static SolveResult Solve(BoardLayout layout, BoardState start,
            int maxDepth = DefaultMaxDepth, int maxStates = DefaultMaxStates, RuleSet rules = null)
        {
            rules = rules ?? RuleSet.Standard;
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            if (start == null) throw new ArgumentNullException(nameof(start));

            if (start.IsSolved)
                return new SolveResult(true, 0, Array.Empty<Direction>(), 1, 1, false);

            var nodes = new List<Node> { new Node { State = start, Parent = -1, Depth = 0, Paths = 1 } };
            var index = new Dictionary<string, int>(StringComparer.Ordinal) { { start.CanonicalKey, 0 } };
            int layerStart = 0;
            int goal = -1;
            bool hitCap = false;

            for (int depth = 0; depth < maxDepth && goal < 0; depth++)
            {
                int layerEnd = nodes.Count;
                if (layerStart == layerEnd) break; // every reachable state explored: unsolvable

                // Finish the whole layer even after finding the goal so the path count is complete.
                for (int n = layerStart; n < layerEnd; n++)
                {
                    Node node = nodes[n];
                    foreach (Direction dir in DirectionExtensions.All)
                    {
                        if (!rules.AllowsMove(dir)) continue;
                        SwipeResult r = SwipeResolver.Resolve(layout, node.State, dir, rules.ConveyorPasses);
                        if (!r.Changed) continue;
                        string key = r.After.CanonicalKey;
                        if (index.TryGetValue(key, out int existing))
                        {
                            Node e = nodes[existing];
                            if (e.Depth == depth + 1) e.Paths += node.Paths;
                            continue;
                        }
                        if (nodes.Count >= maxStates) { hitCap = true; continue; }
                        index[key] = nodes.Count;
                        if (r.After.IsSolved && goal < 0) goal = nodes.Count;
                        nodes.Add(new Node { State = r.After, Parent = n, Move = dir, Depth = depth + 1, Paths = node.Paths });
                    }
                }
                layerStart = layerEnd;
            }

            if (goal < 0)
                return new SolveResult(false, -1, Array.Empty<Direction>(), 0, nodes.Count, hitCap);

            var path = new List<Direction>();
            for (int n = goal; nodes[n].Parent >= 0; n = nodes[n].Parent) path.Add(nodes[n].Move);
            path.Reverse();
            return new SolveResult(true, path.Count, path, nodes[goal].Paths, nodes.Count, hitCap);
        }

        /// <summary>
        /// Optimal next board move from the current state, or null if the state cannot be won. With reversed
        /// tower rules the player must swipe <see cref="RuleSet.SwipeFor"/> of this.
        /// </summary>
        public static Direction? Hint(BoardLayout layout, BoardState state, RuleSet rules = null) =>
            Solve(layout, state, DefaultMaxDepth, DefaultMaxStates, rules).FirstMove;
    }
}
