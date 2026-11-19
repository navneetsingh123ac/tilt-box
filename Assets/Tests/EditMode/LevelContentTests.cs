using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TiltBox.Core;

namespace TiltBox.Core.Tests
{
    /// <summary>
    /// Guards the shipped level files against the difficulty plan (GDD §4-§6): every level parses, is solvable,
    /// its declared par is the true optimum, its limit, kind and par band match its slot, and generated levels
    /// pass the generator's quality filters. Adding a level file adds test cases automatically.
    /// </summary>
    public class LevelContentTests
    {
        public static IEnumerable<string> LevelFiles()
        {
            string root = TestBoards.LevelsRoot();
            return Directory.GetFiles(root, "*.json", SearchOption.AllDirectories)
                .OrderBy(p => p, System.StringComparer.Ordinal)
                .Select(p => p.Substring(root.Length + 1).Replace('\\', '/'));
        }

        private static readonly Dictionary<string, LevelDefinition> Cache = new Dictionary<string, LevelDefinition>();

        private static LevelDefinition Load(string relativePath)
        {
            lock (Cache)
            {
                if (Cache.TryGetValue(relativePath, out LevelDefinition l)) return l;
                string path = Path.Combine(TestBoards.LevelsRoot(), relativePath);
                l = LevelParser.Parse(File.ReadAllText(path), relativePath);
                Cache[relativePath] = l;
                return l;
            }
        }

        private static int NumberOf(LevelDefinition level) => int.Parse(level.Id.Substring(level.Id.IndexOf('_') + 1));

        [Test]
        public void EachSupportedWorld_HasThirtyLevels_NumberedInOrder()
        {
            for (int world = 1; world <= DifficultyPlan.SupportedWorlds; world++)
            {
                var ids = LevelFiles().Where(f => f.StartsWith("World" + world + "/"))
                    .Select(f => Path.GetFileNameWithoutExtension(f)).ToList();
                var expected = Enumerable.Range(1, DifficultyPlan.LevelsPerWorld).Select(n => DifficultyPlan.IdFor(world, n)).ToList();
                CollectionAssert.AreEqual(expected, ids, "World " + world);
            }
        }

        [Test]
        public void NoTwoLevelsAreTheSamePuzzle()
        {
            var seen = new Dictionary<string, string>();
            foreach (string f in LevelFiles())
            {
                LevelDefinition l = Load(f);
                string key = LevelGenerator.KeyOf(l.Layout, l.Start);
                Assert.IsFalse(seen.ContainsKey(key), f + " duplicates " + (seen.ContainsKey(key) ? seen[key] : ""));
                seen[key] = f;
            }
        }

        [TestCaseSource(nameof(LevelFiles))]
        public void Level_IdMatchesFileAndFolder(string file)
        {
            LevelDefinition level = Load(file);
            Assert.AreEqual(Path.GetFileNameWithoutExtension(file), level.Id);
            Assert.AreEqual("World" + level.World, file.Split('/')[0]);
        }

        [TestCaseSource(nameof(LevelFiles))]
        public void Level_DeclaredParIsSolverPar(string file)
        {
            LevelDefinition level = Load(file);
            SolveResult r = Solver.Solve(level.Layout, level.Start, Solver.DefaultMaxDepth, Solver.DefaultMaxStates, level.Rules);
            Assert.IsTrue(r.Solved, file + " has no solution within " + Solver.DefaultMaxDepth + " swipes");
            Assert.AreEqual(r.Par, level.Par, file + " declares par " + level.Par + " but the solver finds " + r.Par +
                                              " (" + string.Concat(r.Solution.Select(d => d.ToLetter())) + ")");
        }

        [TestCaseSource(nameof(LevelFiles))]
        public void Level_LimitAndKindFollowThePlan(string file)
        {
            LevelDefinition level = Load(file);
            Assert.AreEqual(DifficultyPlan.Limit(level.World, level.Par), level.Limit, "limit = par + world slack");
            Assert.AreEqual(DifficultyPlan.KindFor(level.World, NumberOf(level)), level.Kind, "kind follows the wave");
        }

        [TestCaseSource(nameof(LevelFiles))]
        public void Level_MeetsItsSlotQuality(string file)
        {
            LevelDefinition level = Load(file);
            int number = NumberOf(level);
            if (level.World == 1 && number <= DifficultyPlan.HandMadeTutorialLevels)
                Assert.Pass("hand-made first session (§11)");
            CheckQuality(level, DifficultyPlan.SpecFor(level.World, number), Path.Combine(TestBoards.LevelsRoot(), file));
        }

        /// <summary>
        /// The generator's filters, re-checked on the shipped file. A level may sit up to 2 below its planned par
        /// only when the generator couldn't reach the plan and said so in the file ("BELOW PLANNED PAR").
        /// </summary>
        public static void CheckQuality(LevelDefinition level, SlotSpec spec, string path)
        {
            RuleSet rules = level.Rules;
            bool relaxed = File.ReadAllText(path).Contains("BELOW PLANNED PAR");
            int parMin = relaxed ? spec.ParMin - 2 : spec.ParMin;
            Assert.That(level.Par, Is.InRange(parMin, spec.ParMax), "par band for " + spec);

            SolveResult r = Solver.Solve(level.Layout, level.Start, Solver.DefaultMaxDepth, Solver.DefaultMaxStates, rules);
            Assert.That(r.OptimalSolutionCount, Is.LessThanOrEqualTo(spec.MaxOptimalSolutions), "optimal solutions");

            // Random play is noisy; allow a little over the generator's limit for a different random seed.
            double randomWin = LevelMetrics.RandomWinRate(level.Layout, level.Start, level.Limit, 200, 12345, rules);
            Assert.That(randomWin, Is.LessThanOrEqualTo(spec.MaxRandomWin + 0.05), "random-win rate");

            if (spec.RequireDeadEnd)
                Assert.That(LevelMetrics.DeadEndsFromStart(level.Layout, level.Start, level.Limit, 60000, rules), Is.GreaterThan(0), "needs a dead end");

            if (spec.RequireMechanicUse)
                Assert.IsTrue(LevelMetrics.Analyse(level.Layout, level.Start, r.Solution, rules).UsesWorldMechanic(spec.World),
                    "the solution must use World " + spec.World + "'s mechanic");

            if (spec.RequireMechanicEssential)
                Assert.IsTrue(LevelMetrics.MechanicIsEssential(level.Layout, level.Start, 200000, rules), "teach level: mechanic must be essential");

            if (!rules.IsStandard && !rules.Reversed)
                Assert.IsTrue(LevelGenerator.RuleMatters(level.Layout, level.Start, r, rules), "the tower rule must change the puzzle");
        }
    }

    /// <summary>Daily puzzles (Resources/Daily) and Weekend Towers (Resources/Towers), GDD §9.</summary>
    public class DailyAndTowerContentTests
    {
        private static string ResourcesRoot => Directory.GetParent(TestBoards.LevelsRoot()).FullName;

        public static IEnumerable<string> DailyFiles()
        {
            string dir = Path.Combine(ResourcesRoot, "Daily");
            return Directory.Exists(dir)
                ? Directory.GetFiles(dir, "*.json").OrderBy(f => f, System.StringComparer.Ordinal).Select(f => Path.GetFileName(f))
                : Enumerable.Empty<string>();
        }

        public static IEnumerable<string> TowerFiles()
        {
            string dir = Path.Combine(ResourcesRoot, "Towers");
            return Directory.Exists(dir)
                ? Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories).OrderBy(f => f, System.StringComparer.Ordinal)
                    .Select(f => f.Substring(dir.Length + 1).Replace(Path.DirectorySeparatorChar, '/'))
                : Enumerable.Empty<string>();
        }

        [Test]
        public void ThereAre365DailyPuzzles()
        {
            var expected = Enumerable.Range(1, DifficultyPlan.DailyCount).Select(d => DifficultyPlan.DailyId(d) + ".json").ToList();
            CollectionAssert.AreEqual(expected, DailyFiles().ToList());
        }

        [Test]
        public void ThereAreEightTowersOfTenLevels()
        {
            var expected = new List<string>();
            for (int t = 1; t <= DifficultyPlan.TowerCount; t++)
            for (int l = 1; l <= DifficultyPlan.LevelsPerTower; l++)
                expected.Add("Tower" + t + "/" + DifficultyPlan.TowerId(t, l) + ".json");
            CollectionAssert.AreEqual(expected, TowerFiles().ToList());
        }

        [Test]
        public void NoDailyOrTowerRepeatsAnotherLevel()
        {
            var seen = new HashSet<string>();
            foreach (string f in Directory.GetFiles(ResourcesRoot, "*.json", SearchOption.AllDirectories))
            {
                LevelDefinition l = LevelParser.Parse(File.ReadAllText(f), f);
                Assert.IsTrue(seen.Add(LevelGenerator.KeyOf(l.Layout, l.Start)), f + " repeats another level");
            }
        }

        [TestCaseSource(nameof(DailyFiles))]
        public void Daily_IsAValidDailyPuzzle(string file)
        {
            string path = Path.Combine(ResourcesRoot, "Daily", file);
            LevelDefinition level = LevelParser.Parse(File.ReadAllText(path), file);
            int day = int.Parse(level.Id.Substring(1));
            SlotSpec spec = DifficultyPlan.DailySpec(day);
            Assert.AreEqual(Path.GetFileNameWithoutExtension(file), level.Id);
            Assert.AreEqual(LevelKind.Daily, level.Kind);
            Assert.IsFalse(level.HasMoveLimit);
            Assert.AreEqual(spec.World, level.World);
            Assert.AreEqual(Solver.Solve(level.Layout, level.Start).Par, level.Par, "declared par is the solver's par");
            LevelContentTests.CheckQuality(level, spec, path);
        }

        [TestCaseSource(nameof(TowerFiles))]
        public void Tower_IsAValidTowerLevel(string file)
        {
            string path = Path.Combine(ResourcesRoot, "Towers", file);
            LevelDefinition level = LevelParser.Parse(File.ReadAllText(path), file);
            int tower = int.Parse(level.Id.Substring(1, level.Id.IndexOf('_') - 1));
            int number = int.Parse(level.Id.Substring(level.Id.IndexOf('_') + 1));
            SlotSpec spec = DifficultyPlan.TowerSpec(tower, number);
            RuleSet expected = DifficultyPlan.TowerRules(tower);
            Assert.AreEqual(expected.Describe(), level.Rules.Describe(), "tower rule");
            Assert.AreEqual(LevelKind.Hard, level.Kind);
            Assert.AreEqual(DifficultyPlan.Limit(level.World, level.Par), level.Limit);
            Assert.AreEqual(Solver.Solve(level.Layout, level.Start, Solver.DefaultMaxDepth, Solver.DefaultMaxStates, level.Rules).Par,
                level.Par, "declared par is the solver's par under the tower rule");
            LevelContentTests.CheckQuality(level, spec, path);
        }
    }
}
