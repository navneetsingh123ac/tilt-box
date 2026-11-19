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
            SolveResult r = Solver.Solve(level.Layout, level.Start);
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

            SlotSpec spec = DifficultyPlan.SpecFor(level.World, number);
            Assert.That(level.Par, Is.InRange(spec.ParMin, spec.ParMax), "par band for " + spec);

            SolveResult r = Solver.Solve(level.Layout, level.Start);
            Assert.That(r.OptimalSolutionCount, Is.LessThanOrEqualTo(spec.MaxOptimalSolutions), "optimal solutions");

            // Random play is noisy; allow a little over the generator's limit for a different random seed.
            double randomWin = LevelMetrics.RandomWinRate(level.Layout, level.Start, level.Limit, 200, 12345);
            Assert.That(randomWin, Is.LessThanOrEqualTo(spec.MaxRandomWin + 0.05), "random-win rate");

            if (spec.RequireDeadEnd)
                Assert.That(LevelMetrics.DeadEndsFromStart(level.Layout, level.Start), Is.GreaterThan(0), "needs a dead end");

            if (spec.RequireMechanicUse)
            {
                MechanicUse use = LevelMetrics.Analyse(level.Layout, level.Start, r.Solution);
                if (level.World == 2) Assert.That(use.IceStops, Is.GreaterThan(0), "solution must use ice as a stopper");
                else Assert.That(use.GateUses, Is.GreaterThan(0), "solution must use a gate");
            }

            if (spec.RequireMechanicEssential)
                Assert.IsTrue(LevelMetrics.MechanicIsEssential(level.Layout, level.Start), "teach level: mechanic must be essential");
        }
    }
}
