using System;
using System.Collections.Generic;

namespace TiltBox.Core
{
    /// <summary>How an optimal solution uses the board's mechanics. Used to score and filter generated levels.</summary>
    public struct MechanicUse
    {
        /// <summary>Times a block stopped against an iced block (ice used as a temporary wall).</summary>
        public int IceStops;

        /// <summary>Times a block slid through or onto a one-way gate.</summary>
        public int GatePasses;

        /// <summary>Times a block stopped against a gate pointing another way.</summary>
        public int GateStops;

        /// <summary>Times a block stopped against a bin of another colour.</summary>
        public int WrongBinStops;

        public int GateUses => GatePasses + GateStops;
    }

    /// <summary>The level-quality measures from GDD §5. Pure and deterministic (seeded).</summary>
    public static class LevelMetrics
    {
        /// <summary>Share of random swipe sequences (within the move limit) that win. §5: ≤15% normal, ≤5% hard.</summary>
        public static double RandomWinRate(BoardLayout layout, BoardState start, int limit, int runs = 200, int seed = 1)
        {
            var rng = new Random(seed);
            int wins = 0;
            for (int i = 0; i < runs; i++)
            {
                BoardState s = start;
                int used = 0, guard = 0;
                while (used < limit && !s.IsSolved && guard++ < 1000)
                {
                    SwipeResult r = SwipeResolver.Resolve(layout, s, DirectionExtensions.All[rng.Next(4)]);
                    if (!r.Changed) continue;
                    s = r.After;
                    used++;
                }
                if (s.IsSolved) wins++;
            }
            return (double)wins / runs;
        }

        /// <summary>
        /// Number of first swipes after which the level can no longer be won (§5 wants at least one for normal
        /// and hard levels). A search that hits the state cap counts as "still winnable" (conservative).
        /// </summary>
        public static int DeadEndsFromStart(BoardLayout layout, BoardState start, int maxStates = 200000)
        {
            int n = 0;
            foreach (Direction d in DirectionExtensions.All)
            {
                SwipeResult r = SwipeResolver.Resolve(layout, start, d);
                if (!r.Changed) continue;
                SolveResult s = Solver.Solve(layout, r.After, Solver.DefaultMaxDepth, maxStates);
                if (!s.Solved && !s.HitStateCap) n++;
            }
            return n;
        }

        public static MechanicUse Analyse(BoardLayout layout, BoardState state, IEnumerable<Direction> solution)
        {
            var use = new MechanicUse();
            foreach (Direction d in solution)
            {
                SwipeResult r = SwipeResolver.Resolve(layout, state, d);
                foreach (BlockMove m in r.Moves)
                {
                    // Cells entered on the way (excluding the start cell).
                    for (GridPos p = m.From.Step(d); ; p = p.Step(d))
                    {
                        if (layout[p].Kind == CellKind.Gate) { use.GatePasses++; break; }
                        if (p == m.To) break;
                    }
                    if (m.Binned) continue;
                    GridPos next = m.To.Step(d);
                    if (!layout.InBounds(next)) continue;
                    Cell c = layout[next];
                    if (c.Kind == CellKind.Gate && c.Gate != d) use.GateStops++;
                    else if (c.Kind == CellKind.Bin && c.Color != state.ColorOf(m.Block)) use.WrongBinStops++;
                    else
                    {
                        int other = state.BlockAt(next);
                        if (other >= 0 && state.IsIced(other)) use.IceStops++;
                    }
                }
                state = r.After;
            }
            return use;
        }

        /// <summary>
        /// True when the level cannot be won once the world's mechanic is taken away (ice melted, gates turned to
        /// plain floor, or gates turned to walls). That is what "the new rule is the only way to win" means (§4).
        /// </summary>
        public static bool MechanicIsEssential(BoardLayout layout, BoardState start, int maxStates = 200000)
        {
            bool hasIce = start.AnyIced;
            bool hasGates = layout.HasKind(CellKind.Gate);
            if (!hasIce && !hasGates) return false;

            if (hasIce && Unwinnable(layout, start.WithoutIce(), maxStates)) return true;
            if (hasGates)
            {
                BoardLayout asFloor = layout.Map(c => c.Kind == CellKind.Gate ? Cell.Floor : c);
                BoardLayout asWall = layout.Map(c => c.Kind == CellKind.Gate ? Cell.Wall : c);
                if (Unwinnable(asFloor, start, maxStates) || Unwinnable(asWall, start, maxStates)) return true;
            }
            return false;
        }

        private static bool Unwinnable(BoardLayout layout, BoardState start, int maxStates)
        {
            SolveResult r = Solver.Solve(layout, start, Solver.DefaultMaxDepth, maxStates);
            return !r.Solved && !r.HitStateCap;
        }
    }
}
