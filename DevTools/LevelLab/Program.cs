using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TiltBox.Core;

// LevelLab: designer's command-line companion to the level files.
//
//   dotnet run -- check <file-or-folder>...   par, optimal solutions, random-win rate, dead ends for each level
//   dotnet run -- search <w> <h> <colours> <blocks> <minPar> <maxPar> [walls] [seed] [tries] [--wrongbin]
//                                              random boards whose par is in range, best-scored first (design aid)
//
// The real generator + picker is a Unity editor tool planned for week 2; this is the minimum to hand-make World 1.
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0) return Usage();
        try
        {
            switch (args[0])
            {
                case "check": return Check(args.Skip(1).ToArray());
                case "search": return Search(args.Skip(1).ToArray());
                default: return Usage();
            }
        }
        catch (LevelFormatException e)
        {
            Console.Error.WriteLine("Level error: " + e.Message);
            return 2;
        }
    }

    private static int Usage()
    {
        Console.WriteLine("usage: check <file|folder>... | search w h colours blocks minPar maxPar [walls] [seed] [tries]");
        return 1;
    }

    private static int Check(string[] paths)
    {
        var files = new List<string>();
        foreach (string p in paths)
        {
            if (Directory.Exists(p)) files.AddRange(Directory.GetFiles(p, "*.json").OrderBy(f => f, StringComparer.Ordinal));
            else files.Add(p);
        }

        int bad = 0;
        var rng = new Random(1);
        foreach (string file in files)
        {
            LevelDefinition level = LevelParser.Parse(File.ReadAllText(file), Path.GetFileName(file));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            SolveResult r = Solver.Solve(level.Layout, level.Start);
            sw.Stop();
            if (!r.Solved)
            {
                Console.WriteLine($"{level.Id,-7} UNSOLVABLE (states {r.StatesExplored})");
                bad++;
                continue;
            }
            double randomWin = RandomWinRate(level.Layout, level.Start, level.Limit, 200, rng);
            int deadEnds = DeadEndsFromStart(level.Layout, level.Start);
            string flag = r.Par == level.Par ? "ok " : "PAR MISMATCH";
            if (r.Par != level.Par) bad++;
            Console.WriteLine(
                $"{level.Id,-7} {level.Layout.Width}x{level.Layout.Height} {level.Kind,-6} par {r.Par,2} (file {level.Par,2}) " +
                $"limit {level.Limit,2}  optimal {r.OptimalSolutionCount,3}  randomWin {randomWin,5:P0}  deadEnds {deadEnds}  " +
                $"wrongBinStops {WrongBinStops(level.Layout, level.Start, r.Solution)}  " +
                $"{Moves(r.Solution),-14} {flag} [{r.StatesExplored} states, {sw.ElapsedMilliseconds} ms]");
        }
        return bad == 0 ? 0 : 3;
    }

    /// <summary>How often the given solution parks a block against a bin of another colour (the level 4 lesson).</summary>
    private static int WrongBinStops(BoardLayout layout, BoardState s, IEnumerable<Direction> solution)
    {
        int n = 0;
        foreach (Direction d in solution)
        {
            SwipeResult r = SwipeResolver.Resolve(layout, s, d);
            foreach (BlockMove m in r.Moves)
            {
                if (m.Binned) continue;
                GridPos next = m.To.Step(d);
                if (layout.InBounds(next) && layout[next].Kind == CellKind.Bin && layout[next].Color != s.ColorOf(m.Block)) n++;
            }
            s = r.After;
        }
        return n;
    }


    private static string Moves(IEnumerable<Direction> dirs) => string.Concat(dirs.Select(d => d.ToLetter()));

    /// <summary>GDD §5: share of 200 random swipe sequences (within the limit) that win.</summary>
    private static double RandomWinRate(BoardLayout layout, BoardState start, int limit, int runs, Random rng)
    {
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

    /// <summary>Number of first swipes after which the level can no longer be won (§5 wants at least one).</summary>
    private static int DeadEndsFromStart(BoardLayout layout, BoardState start)
    {
        int n = 0;
        foreach (Direction d in DirectionExtensions.All)
        {
            SwipeResult r = SwipeResolver.Resolve(layout, start, d);
            if (r.Changed && !Solver.Solve(layout, r.After).Solved) n++;
        }
        return n;
    }

    private static int Search(string[] a)
    {
        if (a.Length < 6) return Usage();
        int w = int.Parse(a[0]), h = int.Parse(a[1]), colours = int.Parse(a[2]), blocks = int.Parse(a[3]);
        int minPar = int.Parse(a[4]), maxPar = int.Parse(a[5]);
        int walls = a.Length > 6 ? int.Parse(a[6]) : w * h / 6;
        int seed = a.Length > 7 ? int.Parse(a[7]) : Environment.TickCount;
        int tries = a.Length > 8 ? int.Parse(a[8]) : 3000;
        bool wantWrongBin = a.Contains("--wrongbin");
        var rng = new Random(seed);
        var found = new List<(double score, string text)>();

        for (int t = 0; t < tries; t++)
        {
            var cells = new Cell[w * h];
            var free = Enumerable.Range(0, w * h).OrderBy(_ => rng.Next()).ToList();
            int take = 0;
            for (int i = 0; i < walls; i++) cells[free[take++]] = Cell.Wall;
            for (int c = 0; c < colours; c++) cells[free[take++]] = Cell.Bin((BlockColor)c);
            var pos = new GridPos[blocks];
            var col = new BlockColor[blocks];
            for (int b = 0; b < blocks; b++)
            {
                int idx = free[take++];
                pos[b] = new GridPos(idx % w, idx / w);
                col[b] = (BlockColor)(b % colours);
            }
            var layout = new BoardLayout(w, h, cells);
            var start = new BoardState(pos, col);
            SolveResult r = Solver.Solve(layout, start, maxDepth: maxPar);
            if (!r.Solved || r.Par < minPar) continue;
            double rw = RandomWinRate(layout, start, r.Par + 6, 200, rng);
            int dead = DeadEndsFromStart(layout, start);
            // Prefer: few optimal routes, low random-win rate, at least one dead end, higher par.
            int wrongBin = WrongBinStops(layout, start, r.Solution);
            if (wantWrongBin && wrongBin == 0) continue;
            double score = r.Par * 2 - Math.Min(r.OptimalSolutionCount, 10) - rw * 20 + (dead > 0 ? 3 : 0);
            found.Add((score, Render(layout, start) +
                $"   par {r.Par}  optimal {r.OptimalSolutionCount}  randomWin {rw:P0}  deadEnds {dead}  wrongBinStops {wrongBin}  {Moves(r.Solution)}\n" +
                ToJson(layout, start, r.Par)));
        }

        foreach (var f in found.OrderByDescending(f => f.score).Take(5))
        {
            Console.WriteLine($"--- score {f.score:F1}");
            Console.WriteLine(f.text);
        }
        Console.WriteLine($"{found.Count} candidates in range out of {tries} (seed {seed})");
        return 0;
    }

    private static string Render(BoardLayout layout, BoardState s)
    {
        var sb = new System.Text.StringBuilder();
        for (int y = 0; y < layout.Height; y++)
        {
            sb.Append("   ");
            for (int x = 0; x < layout.Width; x++)
            {
                int b = s.BlockAt(new GridPos(x, y));
                Cell c = layout[x, y];
                sb.Append(b >= 0 ? s.ColorOf(b).ToLetter()
                    : c.Kind == CellKind.Wall ? '#'
                    : c.Kind == CellKind.Bin ? char.ToUpperInvariant(c.Color.ToLetter()) : '.');
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }

    private static string ToJson(BoardLayout layout, BoardState s, int par)
    {
        var rows = new List<string>();
        for (int y = 0; y < layout.Height; y++)
        {
            var chars = new char[layout.Width];
            for (int x = 0; x < layout.Width; x++)
            {
                Cell c = layout[x, y];
                chars[x] = c.Kind == CellKind.Wall ? '#' : c.Kind == CellKind.Bin ? char.ToUpperInvariant(c.Color.ToLetter()) : '.';
            }
            rows.Add("\"" + new string(chars) + "\"");
        }
        var blocks = Enumerable.Range(0, s.BlockCount)
            .Select(i => $"{{ \"x\": {s.PositionOf(i).X}, \"y\": {s.PositionOf(i).Y}, \"c\": \"{s.ColorOf(i).ToLetter()}\" }}");
        return "   \"cells\": [" + string.Join(", ", rows) + "],\n   \"blocks\": [ " + string.Join(", ", blocks) + " ], \"par\": " + par;
    }
}
