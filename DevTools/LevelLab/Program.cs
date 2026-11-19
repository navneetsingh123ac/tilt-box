using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using TiltBox.Core;

// LevelLab: the designer's command-line companion to the level files. Uses the same Core code as the game.
//
//   check <file-or-folder>...                 par, optimal solutions, random-win rate, dead ends, mechanic use,
//                                             and whether each level fits its slot in the difficulty plan
//   slot <world> <number> [attempts] [seed]   generate candidates for one slot and print the best 5
//   fill <world> [from] [to] [attempts] [--teach] [--force]
//                                             generate every slot in the range, keep the 10 best per slot in
//                                             LevelCandidates/, and write the best new one as the level file.
//                                             Hand-made slots (W1 1-10, teach levels) are skipped unless --teach.
//                                             Existing level files are kept unless --force.
internal static class Program
{
    private static readonly string ProjectRoot = FindProjectRoot();
    private static string LevelsDir(int world) => Path.Combine(ProjectRoot, "Assets", "Resources", "Levels", "World" + world);
    private static string CandidatesDir => Path.Combine(ProjectRoot, "LevelCandidates");

    private static int Main(string[] args)
    {
        if (args.Length == 0) return Usage();
        try
        {
            switch (args[0])
            {
                case "check": return Check(args.Skip(1).ToArray());
                case "slot": return Slot(args.Skip(1).ToArray());
                case "fill": return Fill(args.Skip(1).ToArray());
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
        Console.WriteLine("usage: check <file|folder>... | slot <world> <n> [attempts] [seed] | fill <world> [from] [to] [attempts] [--teach] [--force]");
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
        foreach (string file in files)
        {
            LevelDefinition level = LevelParser.Parse(File.ReadAllText(file), Path.GetFileName(file));
            var sw = Stopwatch.StartNew();
            SolveResult r = Solver.Solve(level.Layout, level.Start);
            if (!r.Solved)
            {
                Console.WriteLine($"{level.Id,-7} UNSOLVABLE (states {r.StatesExplored})");
                bad++;
                continue;
            }
            double randomWin = LevelMetrics.RandomWinRate(level.Layout, level.Start, level.Limit);
            int deadEnds = LevelMetrics.DeadEndsFromStart(level.Layout, level.Start);
            MechanicUse use = LevelMetrics.Analyse(level.Layout, level.Start, r.Solution);
            sw.Stop();

            var problems = new List<string>();
            if (r.Par != level.Par) problems.Add("par is " + r.Par);
            int number = int.Parse(level.Id.Substring(level.Id.IndexOf('_') + 1));
            if (level.World <= DifficultyPlan.SupportedWorlds && !(level.World == 1 && number <= DifficultyPlan.HandMadeTutorialLevels))
            {
                SlotSpec spec = DifficultyPlan.SpecFor(level.World, number);
                if (r.Par < spec.ParMin || r.Par > spec.ParMax) problems.Add($"par outside {spec.ParMin}-{spec.ParMax}");
                if (randomWin > spec.MaxRandomWin) problems.Add("random win too high");
                if (spec.RequireDeadEnd && deadEnds == 0) problems.Add("no dead end");
                if (level.Kind != spec.Kind) problems.Add("kind should be " + spec.Kind);
            }
            if (problems.Count > 0) bad++;
            Console.WriteLine(
                $"{level.Id,-6} {level.Layout.Width}x{level.Layout.Height} {level.Kind,-6} par {r.Par,2} limit {level.Limit,2}  " +
                $"opt {r.OptimalSolutionCount,2}  rnd {randomWin,4:P0}  dead {deadEnds}  ice {use.IceStops} gate {use.GateUses} wrongBin {use.WrongBinStops}  " +
                $"{Moves(r.Solution),-12} {(problems.Count == 0 ? "ok" : "PROBLEM: " + string.Join(", ", problems))} [{sw.ElapsedMilliseconds} ms]");
        }
        return bad == 0 ? 0 : 3;
    }

    private static int Slot(string[] a)
    {
        if (a.Length < 2) return Usage();
        int world = int.Parse(a[0]), number = int.Parse(a[1]);
        int attempts = a.Length > 2 ? int.Parse(a[2]) : 4000;
        int seed = a.Length > 3 ? int.Parse(a[3]) : 1;
        SlotSpec spec = DifficultyPlan.SpecFor(world, number);
        Console.WriteLine(spec);
        var sw = Stopwatch.StartNew();
        List<Candidate> found = LevelGenerator.Generate(spec, seed, attempts, 10);
        Console.WriteLine($"{found.Count} kept from {attempts} attempts in {sw.Elapsed.TotalSeconds:F1} s");
        foreach (Candidate c in found.Take(5))
        {
            Console.WriteLine("--- " + c.Summary + "  " + Moves(c.Solution.Solution));
            Console.Write(Render(c.Level));
        }
        return 0;
    }

    private static int Fill(string[] a)
    {
        var flags = new HashSet<string>(a.Where(x => x.StartsWith("--")));
        var nums = a.Where(x => !x.StartsWith("--")).Select(int.Parse).ToArray();
        if (nums.Length < 1) return Usage();
        int world = nums[0];
        int from = nums.Length > 1 ? nums[1] : 1;
        int to = nums.Length > 2 ? nums[2] : DifficultyPlan.LevelsPerWorld;
        int attempts = nums.Length > 3 ? nums[3] : 6000;
        bool teach = flags.Contains("--teach"), force = flags.Contains("--force");

        Directory.CreateDirectory(LevelsDir(world));
        Directory.CreateDirectory(CandidatesDir);
        var taken = new HashSet<string>(AllLevelKeys());
        int failures = 0;

        for (int n = from; n <= to; n++)
        {
            SlotSpec spec = DifficultyPlan.SpecFor(world, n);
            string levelPath = Path.Combine(LevelsDir(world), spec.Id + ".json");
            bool handMade = DifficultyPlan.IsHandMade(world, n);
            if (handMade && !teach) { Console.WriteLine($"{spec.Id}: hand-made slot, skipped"); continue; }
            if (File.Exists(levelPath) && !force) { Console.WriteLine($"{spec.Id}: exists, kept"); continue; }

            var sw = Stopwatch.StartNew();
            List<Candidate> found = new List<Candidate>();
            // Rare slots (hard, teach) get more attempts with fresh seeds until something passes.
            for (int round = 0; round < 6 && found.Count(c => !taken.Contains(c.Key)) < 3; round++)
                found.AddRange(LevelGenerator.Generate(spec, world * 1000 + n + round * 7919, attempts, 10));
            found = found.GroupBy(c => c.Key).Select(g => g.First()).OrderByDescending(c => c.Score).Take(10).ToList();
            File.WriteAllText(Path.Combine(CandidatesDir, spec.Id + ".json"), CandidateFile.Write(spec.Id, found));

            Candidate pick = found.FirstOrDefault(c => !taken.Contains(c.Key));
            if (pick == null)
            {
                Console.WriteLine($"{spec.Id}: NO CANDIDATE ({spec}) [{sw.Elapsed.TotalSeconds:F0} s]");
                failures++;
                continue;
            }
            taken.Add(pick.Key);
            File.WriteAllText(levelPath, LevelWriter.ToJson(pick.Level,
                "Generated (seed " + pick.Seed + ") and auto-picked by score; " + pick.Summary.Replace("  ", ", ")));
            Console.WriteLine($"{spec.Id}: {spec.Kind,-6} {spec.Width}x{spec.Height} {pick.Summary}  {Moves(pick.Solution.Solution)}  ({found.Count} kept) [{sw.Elapsed.TotalSeconds:F0} s]");
        }
        return failures == 0 ? 0 : 4;
    }

    private static IEnumerable<string> AllLevelKeys()
    {
        string root = Path.Combine(ProjectRoot, "Assets", "Resources", "Levels");
        if (!Directory.Exists(root)) yield break;
        foreach (string f in Directory.GetFiles(root, "*.json", SearchOption.AllDirectories))
        {
            LevelDefinition l = LevelParser.Parse(File.ReadAllText(f), f);
            yield return LevelGenerator.KeyOf(l.Layout, l.Start);
        }
    }

    private static string Moves(IEnumerable<Direction> dirs) => string.Concat(dirs.Select(d => d.ToLetter()));

    private static string Render(LevelDefinition level)
    {
        var sb = new System.Text.StringBuilder();
        BoardLayout layout = level.Layout;
        BoardState s = level.Start;
        for (int y = 0; y < layout.Height; y++)
        {
            sb.Append("   ");
            for (int x = 0; x < layout.Width; x++)
            {
                int b = s.BlockAt(new GridPos(x, y));
                // Two characters per cell: the block letter (with * when iced) or the cell character.
                if (b >= 0) sb.Append(s.ColorOf(b).ToLetter()).Append(s.IsIced(b) ? '*' : ' ');
                else sb.Append(layout[x, y].ToChar()).Append(' ');
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }

    private static string FindProjectRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Assets"))) dir = dir.Parent;
        return dir?.FullName ?? Directory.GetCurrentDirectory();
    }
}
