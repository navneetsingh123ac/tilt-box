using System;
using System.Collections.Generic;

namespace TiltBox.Core
{
    /// <summary>How a solution uses the board's mechanics. Used to score and filter generated levels.</summary>
    public struct MechanicUse
    {
        public int IceStops;       // a block stopped against an iced block (ice as a temporary wall)
        public int GatePasses;     // a block slid through or onto a one-way gate
        public int GateStops;      // a block stopped against a gate pointing another way
        public int WrongBinStops;  // a block stopped against a bin of another colour
        public int StickyStops;    // a block stopped on a sticky pad
        public int PortalJumps;    // a block went through a portal
        public int PaintChanges;   // a block changed colour on paint
        public int KeyUses;        // a key went into a keyhole
        public int LockPasses;     // a block slid through an opened lock wall
        public int ConveyorSteps;  // a conveyor moved a block
        public int LinkedMoves;    // a linked pair moved together

        public int GateUses => GatePasses + GateStops;

        /// <summary>How many different mechanics the solution relies on (World 10 wants several).</summary>
        public int KindsUsed =>
            (IceStops > 0 ? 1 : 0) + (GateUses > 0 ? 1 : 0) + (StickyStops > 0 ? 1 : 0) + (PortalJumps > 0 ? 1 : 0) +
            (PaintChanges > 0 ? 1 : 0) + (KeyUses > 0 && LockPasses > 0 ? 1 : 0) + (ConveyorSteps > 0 ? 1 : 0) +
            (LinkedMoves > 0 ? 1 : 0);

        /// <summary>Does the solution use the mechanic this world introduces (§4)?</summary>
        public bool UsesWorldMechanic(int world)
        {
            switch (world)
            {
                case 1: return true;
                case 2: return IceStops > 0;
                case 3: return GateUses > 0;
                case 4: return StickyStops > 0;
                case 5: return PortalJumps > 0;
                case 6: return PaintChanges > 0;
                case 7: return KeyUses > 0 && LockPasses > 0;
                case 8: return ConveyorSteps > 0;
                case 9: return LinkedMoves > 0;
                default: return KindsUsed >= 2;
            }
        }
    }

    /// <summary>The level-quality measures from GDD §5. Pure and deterministic (seeded).</summary>
    public static class LevelMetrics
    {
        /// <summary>Share of random swipe sequences (within the move limit) that win. §5: ≤15% normal, ≤5% hard.</summary>
        public static double RandomWinRate(BoardLayout layout, BoardState start, int limit, int runs = 200, int seed = 1,
            RuleSet rules = null)
        {
            rules = rules ?? RuleSet.Standard;
            var rng = new Random(seed);
            int wins = 0;
            for (int i = 0; i < runs; i++)
            {
                BoardState s = start;
                int used = 0, guard = 0;
                while (used < limit && !s.IsSolved && guard++ < 1000)
                {
                    Direction d = DirectionExtensions.All[rng.Next(4)];
                    if (!rules.AllowsMove(d)) continue;
                    SwipeResult r = SwipeResolver.Resolve(layout, s, d, rules.ConveyorPasses);
                    if (!r.Changed) continue;
                    s = r.After;
                    used++;
                }
                if (s.IsSolved) wins++;
            }
            return (double)wins / runs;
        }

        /// <summary>
        /// Number of first swipes after which the level can no longer be won within the move limit (§5 wants at
        /// least one for normal and hard levels, "a swipe that makes the level unwinnable"). Pass the level's limit;
        /// a search that hits the state cap counts as "still winnable" (conservative).
        /// </summary>
        public static int DeadEndsFromStart(BoardLayout layout, BoardState start, int limit, int maxStates = 60000, RuleSet rules = null)
        {
            rules = rules ?? RuleSet.Standard;
            int n = 0;
            foreach (Direction d in DirectionExtensions.All)
            {
                if (!rules.AllowsMove(d)) continue;
                SwipeResult r = SwipeResolver.Resolve(layout, start, d, rules.ConveyorPasses);
                if (!r.Changed) continue;
                SolveResult s = Solver.Solve(layout, r.After, Math.Max(0, limit - 1), maxStates, rules);
                if (!s.Solved && !s.HitStateCap) n++;
            }
            return n;
        }

        public static MechanicUse Analyse(BoardLayout layout, BoardState state, IEnumerable<Direction> solution, RuleSet rules = null)
        {
            rules = rules ?? RuleSet.Standard;
            var use = new MechanicUse();
            foreach (Direction d in solution)
            {
                SwipeResult r = SwipeResolver.Resolve(layout, state, d, rules.ConveyorPasses);
                foreach (BlockMove m in r.Moves)
                {
                    bool gate = false, lockPass = false;
                    foreach (Segment seg in m.Segments)
                    {
                        if (seg.From == seg.To) continue;
                        for (GridPos p = seg.From.Step(d); ; p = p.Step(d))
                        {
                            CellKind k = layout[p].Kind;
                            gate |= k == CellKind.Gate;
                            lockPass |= k == CellKind.Lock;
                            if (p == seg.To) break;
                        }
                    }
                    if (gate) use.GatePasses++;
                    if (lockPass) use.LockPasses++;
                    if (m.Teleported) use.PortalJumps++;
                    if (state.PartnerOf(m.Block) >= 0) use.LinkedMoves++;
                    if (r.After.ColorOf(m.Block) != state.ColorOf(m.Block)) use.PaintChanges++;
                    if (m.Binned)
                    {
                        if (state.IsKey(m.Block)) use.KeyUses++;
                        continue;
                    }
                    if (layout[m.To].Kind == CellKind.Sticky) use.StickyStops++;
                    GridPos next = m.To.Step(d);
                    if (!layout.InBounds(next)) continue;
                    Cell c = layout[next];
                    if (c.Kind == CellKind.Gate && c.Dir != d) use.GateStops++;
                    else if (c.Kind == CellKind.Bin && c.Color != r.After.ColorOf(m.Block)) use.WrongBinStops++;
                    else
                    {
                        int other = state.BlockAt(next);
                        if (other >= 0 && state.IsIced(other)) use.IceStops++;
                    }
                }
                foreach (BlockMove m in r.ConveyorMoves)
                {
                    use.ConveyorSteps++;
                    if (m.Binned && state.IsKey(m.Block)) use.KeyUses++;
                }
                state = r.After;
            }
            return use;
        }

        /// <summary>
        /// True when the level cannot be won once one of its mechanics is taken away (ice melted, gates or portals
        /// turned to floor or walls, sticky pads / paint / conveyors turned to floor, keyholes walled up, links
        /// removed). That is what "the new rule is the only way to win" means for teach levels (§4).
        /// </summary>
        public static bool MechanicIsEssential(BoardLayout layout, BoardState start, int maxStates = 200000, RuleSet rules = null)
        {
            foreach (var variant in WithoutMechanics(layout, start))
                if (Unwinnable(variant.Key, variant.Value, maxStates, rules)) return true;
            return false;
        }

        private static IEnumerable<KeyValuePair<BoardLayout, BoardState>> WithoutMechanics(BoardLayout layout, BoardState start)
        {
            if (start.AnyIced) yield return Pair(layout, start.WithoutIce());
            bool linked = false;
            for (int i = 0; i < start.BlockCount; i++) linked |= start.LinkOf(i) != 0;
            if (linked) yield return Pair(layout, start.WithoutLinks());

            foreach (CellKind kind in new[] { CellKind.Gate, CellKind.Portal })
            {
                if (!layout.HasKind(kind)) continue;
                yield return Pair(layout.Map(c => c.Kind == kind ? Cell.Floor : c), start);
                yield return Pair(layout.Map(c => c.Kind == kind ? Cell.Wall : c), start);
            }
            foreach (CellKind kind in new[] { CellKind.Sticky, CellKind.Paint, CellKind.Conveyor })
                if (layout.HasKind(kind)) yield return Pair(layout.Map(c => c.Kind == kind ? Cell.Floor : c), start);
            if (layout.HasKind(CellKind.Keyhole))
                yield return Pair(layout.Map(c => c.Kind == CellKind.Keyhole ? Cell.Wall : c), start);
        }

        private static KeyValuePair<BoardLayout, BoardState> Pair(BoardLayout l, BoardState s) =>
            new KeyValuePair<BoardLayout, BoardState>(l, s);

        private static bool Unwinnable(BoardLayout layout, BoardState start, int maxStates, RuleSet rules)
        {
            SolveResult r = Solver.Solve(layout, start, Solver.DefaultMaxDepth, maxStates, rules);
            return !r.Solved && !r.HitStateCap;
        }
    }
}
