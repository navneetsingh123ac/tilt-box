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
            "  ice " + Use.IceStops + "  gates " + Use.GateUses + "  wrongBin " + Use.WrongBinStops +
            "  score " + Score.ToString("0.0") + "  seed " + Seed;
    }

    /// <summary>
    /// GDD §5 generator: place walls, bins, gates and blocks at random, solve, and keep the boards that pass
    /// the slot's filters (par band, random-win rate, optimal-solution count, dead ends, mechanic use).
    /// Deterministic for a given seed. Runs on all cores. Pure C#, so the editor window, LevelLab and tests share it.
    /// </summary>
    public static class LevelGenerator
    {
        /// <summary>States the solver may visit per candidate before the board is skipped as too expensive.</summary>
        public const int StateBudget = 200000;

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
            int colours = Between(rng, spec.MinColours, spec.MaxColours);
            int blocks = Math.Max(colours, Between(rng, spec.MinBlocks, spec.MaxBlocks));
            int walls = Between(rng, spec.MinWalls, spec.MaxWalls);
            int gates = Between(rng, spec.MinGates, spec.MaxGates);
            int ice = Math.Min(blocks, Between(rng, spec.MinIce, spec.MaxIce));
            if (walls + colours + gates + blocks > w * h) return null;

            // Shuffle all cells, then hand them out: walls, bins, gates, block positions.
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

            var positions = new GridPos[blocks];
            var colors = new BlockColor[blocks];
            var iced = new bool[blocks];
            for (int b = 0; b < blocks; b++)
            {
                int idx = order[take++];
                positions[b] = new GridPos(idx % w, idx / w);
                colors[b] = b < colours ? (BlockColor)b : (BlockColor)rng.Next(colours);
            }
            for (int k = 0; k < ice; k++)
            {
                int b = rng.Next(blocks);
                while (iced[b]) b = (b + 1) % blocks;
                iced[b] = true;
            }

            var layout = new BoardLayout(w, h, cells);
            var start = new BoardState(positions, colors, null, iced);
            return Evaluate(spec, layout, start, seed);
        }

        /// <summary>Scores an existing board against a slot (also used to re-check hand-made boards).</summary>
        public static Candidate Evaluate(SlotSpec spec, BoardLayout layout, BoardState start, int seed = 0)
        {
            // Cheap checks first, expensive ones last.
            SolveResult solve = Solver.Solve(layout, start, spec.ParMax, StateBudget);
            if (!solve.Solved || solve.Par < spec.ParMin) return null;
            if (solve.OptimalSolutionCount > spec.MaxOptimalSolutions) return null;

            MechanicUse use = LevelMetrics.Analyse(layout, start, solve.Solution);
            if (spec.RequireMechanicUse)
            {
                bool used = spec.World == 2 ? use.IceStops > 0 : use.GateUses > 0;
                if (!used) return null;
            }

            int limit = solve.Par + spec.Slack;
            double randomWin = LevelMetrics.RandomWinRate(layout, start, limit, 200, seed);
            if (randomWin > spec.MaxRandomWin) return null;

            int deadEnds = LevelMetrics.DeadEndsFromStart(layout, start, StateBudget);
            if (spec.RequireDeadEnd && deadEnds == 0) return null;

            bool essential = spec.RequireMechanicEssential && LevelMetrics.MechanicIsEssential(layout, start, StateBudget);
            if (spec.RequireMechanicEssential && !essential) return null;

            var level = new LevelDefinition(spec.Id, spec.World, layout, start, solve.Par, limit, spec.Kind);
            int wallCount = 0;
            for (int i = 0; i < layout.CellCount; i++)
                if (layout[i % layout.Width, i / layout.Width].Kind == CellKind.Wall) wallCount++;

            // Higher is better: a clear single "aha" route, few random wins, real choices, the mechanic in play,
            // and a par near the middle of the slot's range.
            double mid = (spec.ParMin + spec.ParMax) / 2.0;
            double score = 0;
            score += solve.OptimalSolutionCount == 1 ? 3 : solve.OptimalSolutionCount == 2 ? 1.5 : 0;
            score -= randomWin * 20;
            score += Math.Min(deadEnds, 2);
            score += Math.Min(use.IceStops + use.GateUses + use.WrongBinStops, 3) * 0.7;
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
