using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TiltBox.Core
{
    /// <summary>A generated level plus the numbers a designer needs to pick between candidates.</summary>
    public sealed class Candidate
    {
        public LevelDefinition Level;
        public SolveResult Solution;
        public double RandomWin;
        public int DeadEnds;
        public MechanicUse Use;
        public bool MechanicEssential;
        public double Score;
        public int Seed;

        /// <summary>Layout + start state; equal keys mean the same puzzle.</summary>
        public string Key;

        public string Summary =>
            "par " + Solution.Par + "  optimal " + Solution.OptimalSolutionCount +
            "  randomWin " + (RandomWin * 100).ToString("0") + "%  deadEnds " + DeadEnds +
            "  mechanics " + Use.KindsUsed + "  wrongBin " + Use.WrongBinStops +
            "  score " + Score.ToString("0.0") + "  seed " + Seed;
    }

    /// <summary>
    /// GDD §5 generator: place walls, bins, mechanics and blocks at random, solve, and keep the boards that pass
    /// the slot's filters (par band, random-win rate, optimal-solution count, dead ends, mechanic use, and for
    /// towers a rule that really changes the puzzle). Deterministic for a given seed. Runs on all cores.
    /// Pure C#, so the editor window, LevelLab and tests share it.
    /// </summary>
    public static class LevelGenerator
    {
        /// <summary>States the solver may visit per candidate before the board is skipped as too expensive.</summary>
        public const int StateBudget = 60000;

        public static List<Candidate> Generate(SlotSpec spec, int baseSeed, int attempts, int keep,
            CancellationToken cancel = default, Action<int> onProgress = null)
        {
            var found = new ConcurrentBag<Candidate>();
            int done = 0;
            var options = new ParallelOptions { CancellationToken = cancel };
            try
            {
                Parallel.For(0, attempts, options, i =>
                {
                    Candidate c = TryBuild(spec, unchecked(baseSeed * 1000003 + i));
                    if (c != null) found.Add(c);
                    int n = Interlocked.Increment(ref done);
                    if (onProgress != null && n % 50 == 0) onProgress(n);
                });
            }
            catch (OperationCanceledException)
            {
                // Return what we have so far.
            }

            var unique = new Dictionary<string, Candidate>();
            foreach (Candidate c in found.OrderByDescending(c => c.Score).ThenBy(c => c.Seed))
                if (!unique.ContainsKey(c.Key)) unique.Add(c.Key, c);
            return unique.Values.Take(keep).ToList();
        }

        /// <summary>One attempt. Returns null when the random board fails any filter.</summary>
        public static Candidate TryBuild(SlotSpec spec, int seed)
        {
            var rng = new Random(seed);
            int w = spec.Width, h = spec.Height;

            int ice = Between(rng, spec.MinIce, spec.MaxIce);
            int gates = Between(rng, spec.MinGates, spec.MaxGates);
            int sticky = Between(rng, spec.MinSticky, spec.MaxSticky);
            int portals = Between(rng, spec.MinPortalPairs, spec.MaxPortalPairs);
            int paint = Between(rng, spec.MinPaint, spec.MaxPaint);
            int keys = Between(rng, spec.MinKeys, spec.MaxKeys);
            int locks = keys > 0 ? Between(rng, Math.Max(1, spec.MinLocks), Math.Max(1, spec.MaxLocks)) : 0;
            int conveyors = Between(rng, spec.MinConveyors, spec.MaxConveyors);
            int links = Between(rng, spec.MinLinks, spec.MaxLinks);
            if (spec.Mastery) MasteryMix(rng, ref ice, ref gates, ref sticky, ref portals, ref paint, ref keys, ref locks, ref conveyors, ref links);

            int colours = Between(rng, spec.MinColours, spec.MaxColours);
            int coloured = Math.Max(colours + Between(rng, spec.MinExtraBlocks, spec.MaxExtraBlocks), links * 2);
            int blocks = coloured + keys;
            if (blocks > LevelParser.MaxBlocks) return null;
            int walls = Between(rng, spec.MinWalls, spec.MaxWalls);
            int special = gates + sticky + portals * 2 + paint + keys + locks + conveyors;
            if (walls + colours + special + blocks + 2 > w * h) return null;

            // Shuffle all cells, then hand them out in a fixed order.
            var order = Enumerable.Range(0, w * h).ToArray();
            for (int i = order.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                int t = order[i]; order[i] = order[j]; order[j] = t;
            }
            var cells = new Cell[w * h];
            int take = 0;
            for (int i = 0; i < walls; i++) cells[order[take++]] = Cell.Wall;
            for (int c = 0; c < colours; c++) cells[order[take++]] = Cell.Bin((BlockColor)c);
            for (int i = 0; i < gates; i++) cells[order[take++]] = Cell.OneWay(DirectionExtensions.All[rng.Next(4)]);
            for (int i = 0; i < sticky; i++) cells[order[take++]] = Cell.Sticky;
            for (int p = 1; p <= portals; p++)
            {
                cells[order[take++]] = Cell.Portal(p);
                cells[order[take++]] = Cell.Portal(p);
            }
            for (int i = 0; i < paint; i++) cells[order[take++]] = Cell.PaintOf((BlockColor)rng.Next(colours));
            if (keys > 0) cells[order[take++]] = Cell.Keyhole;
            for (int i = 0; i < locks; i++) cells[order[take++]] = Cell.Lock;
            for (int i = 0; i < conveyors; i++) cells[order[take++]] = Cell.ConveyorTo(DirectionExtensions.All[rng.Next(4)]);

            // Blocks go on the remaining floor cells; linked pairs need two side-by-side free cells.
            var used = new bool[w * h];
            for (int i = 0; i < take; i++) used[order[i]] = true;
            var positions = new List<GridPos>();
            var colors = new List<BlockColor>();
            var linkIds = new List<int>();
            for (int l = 1; l <= links; l++)
            {
                bool placed = false;
                for (int i = take; i < order.Length && !placed; i++)
                {
                    int a = order[i];
                    if (used[a]) continue;
                    GridPos pa = new GridPos(a % w, a / w);
                    foreach (Direction d in DirectionExtensions.All)
                    {
                        GridPos pb = pa.Step(d);
                        if (pb.X < 0 || pb.Y < 0 || pb.X >= w || pb.Y >= h) continue;
                        int bIdx = pb.Y * w + pb.X;
                        if (used[bIdx] || cells[bIdx].Kind != CellKind.Floor) continue;
                        used[a] = used[bIdx] = true;
                        positions.Add(pa); positions.Add(pb);
                        linkIds.Add(l); linkIds.Add(l);
                        placed = true;
                        break;
                    }
                }
                if (!placed) return null;
            }
            for (int i = take; positions.Count < blocks && i < order.Length; i++)
            {
                if (used[order[i]]) continue;
                used[order[i]] = true;
                positions.Add(new GridPos(order[i] % w, order[i] / w));
                linkIds.Add(0);
            }
            if (positions.Count < blocks) return null;
            for (int b = 0; b < coloured; b++) colors.Add(b < colours ? (BlockColor)b : (BlockColor)rng.Next(colours));
            if (keys > 0) colors.Add(BlockColor.Key);

            var iced = new bool[blocks];
            for (int k = 0; k < Math.Min(ice, coloured); k++)
            {
                int b = rng.Next(coloured);
                while (iced[b]) b = (b + 1) % coloured;
                iced[b] = true;
            }

            var layout = new BoardLayout(w, h, cells);
            var start = new BoardState(positions.ToArray(), colors.ToArray(), null, iced, linkIds.ToArray());
            return Evaluate(spec, layout, start, seed);
        }

        /// <summary>World 10: switch on 2-3 of the earlier mechanics, one or two of each.</summary>
        private static void MasteryMix(Random rng, ref int ice, ref int gates, ref int sticky, ref int portals, ref int paint,
            ref int keys, ref int locks, ref int conveyors, ref int links)
        {
            var kinds = Enumerable.Range(0, 8).OrderBy(_ => rng.Next()).Take(rng.Next(2, 4)).ToList();
            foreach (int k in kinds)
            {
                switch (k)
                {
                    case 0: ice = 1; break;
                    case 1: gates = rng.Next(1, 3); break;
                    case 2: sticky = rng.Next(1, 3); break;
                    case 3: portals = 1; break;
                    case 4: paint = 1; break;
                    case 5: keys = 1; locks = rng.Next(1, 3); break;
                    case 6: conveyors = rng.Next(1, 3); break;
                    default: links = 1; break;
                }
            }
        }

        /// <summary>Scores an existing board against a slot (also used to re-check hand-made boards).</summary>
        public static Candidate Evaluate(SlotSpec spec, BoardLayout layout, BoardState start, int seed = 0)
        {
            RuleSet rules = spec.Rules ?? RuleSet.Standard;

            // Cheap checks first, expensive ones last.
            SolveResult solve = Solver.Solve(layout, start, spec.ParMax, StateBudget, rules);
            if (!solve.Solved || solve.Par < spec.ParMin) return null;
            if (solve.OptimalSolutionCount > spec.MaxOptimalSolutions) return null;

            MechanicUse use = LevelMetrics.Analyse(layout, start, solve.Solution, rules);
            if (spec.RequireMechanicUse && !use.UsesWorldMechanic(spec.World)) return null;

            // A tower rule must change the puzzle: without it the level is shorter or plays out differently.
            if (!rules.IsStandard && !rules.Reversed && !RuleMatters(layout, start, solve, rules)) return null;

            int limit = solve.Par + spec.Slack;
            double randomWin = LevelMetrics.RandomWinRate(layout, start, limit, 200, seed, rules);
            if (randomWin > spec.MaxRandomWin) return null;

            int deadEnds = LevelMetrics.DeadEndsFromStart(layout, start, limit, StateBudget, rules);
            if (spec.RequireDeadEnd && deadEnds == 0) return null;

            bool essential = spec.RequireMechanicEssential && LevelMetrics.MechanicIsEssential(layout, start, StateBudget, rules);
            if (spec.RequireMechanicEssential && !essential) return null;

            var level = new LevelDefinition(spec.Id, spec.World, layout, start, solve.Par, limit, spec.Kind, rules);
            int wallCount = layout.CountKind(CellKind.Wall);

            // Higher is better: a clear single "aha" route, few random wins, real choices, mechanics in play,
            // and a par near the middle of the slot's range.
            double mid = (spec.ParMin + spec.ParMax) / 2.0;
            double score = 0;
            score += solve.OptimalSolutionCount == 1 ? 3 : solve.OptimalSolutionCount == 2 ? 1.5 : 0;
            score -= randomWin * 20;
            score += Math.Min(deadEnds, 2);
            score += Math.Min(use.KindsUsed + use.WrongBinStops, 3) * 0.7;
            score -= Math.Abs(solve.Par - mid) * 0.8;
            score -= wallCount * 0.08;

            return new Candidate
            {
                Level = level,
                Solution = solve,
                RandomWin = randomWin,
                DeadEnds = deadEnds,
                Use = use,
                MechanicEssential = essential,
                Score = score,
                Seed = seed,
                Key = KeyOf(layout, start),
            };
        }

        /// <summary>
        /// True when the rule changes the puzzle: under standard rules the level is unwinnable/shorter, or its
        /// optimal solutions differ (e.g. they use the forbidden direction).
        /// </summary>
        public static bool RuleMatters(BoardLayout layout, BoardState start, SolveResult withRule, RuleSet rule)
        {
            SolveResult standard = Solver.Solve(layout, start, withRule.Par, StateBudget);
            if (!standard.Solved || standard.Par != withRule.Par) return true;
            if (rule.Forbidden != null && standard.Solution.Contains(rule.Forbidden.Value)) return true;
            if (rule.ConveyorPasses > 1)
            {
                // Same par, but does the standard solution still win with the rule? If not, the rule matters.
                BoardState s = start;
                foreach (Direction d in standard.Solution) s = SwipeResolver.Resolve(layout, s, d, rule.ConveyorPasses).After;
                return !s.IsSolved;
            }
            return false;
        }

        public static string KeyOf(BoardLayout layout, BoardState start)
        {
            var chars = new char[layout.CellCount];
            for (int y = 0; y < layout.Height; y++)
            for (int x = 0; x < layout.Width; x++)
                chars[y * layout.Width + x] = layout[x, y].ToChar();
            return layout.Width + "x" + layout.Height + ":" + new string(chars) + "|" + start.CanonicalKey;
        }

        private static int Between(Random rng, int min, int max) => max <= min ? min : rng.Next(min, max + 1);
    }
}
