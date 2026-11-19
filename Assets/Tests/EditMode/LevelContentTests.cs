using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TiltBox.Core;

namespace TiltBox.Core.Tests
{
    /// <summary>
    /// Guards the shipped level files: every level parses, is solvable, its declared par is the true optimum,
    /// and its move limit follows the world's slack (GDD §6). Adding a level file adds test cases automatically.
    /// </summary>
    public class LevelContentTests
    {
        // GDD §6: slack per world (index 0 unused).
        private static readonly int[] Slack = { 0, 6, 5, 5, 4, 4, 4, 3, 3, 3, 3 };

        public static IEnumerable<string> LevelFiles()
        {
            string root = TestBoards.LevelsRoot();
            return Directory.GetFiles(root, "*.json", SearchOption.AllDirectories)
                .OrderBy(p => p, System.StringComparer.Ordinal)
                .Select(p => p.Substring(root.Length + 1).Replace('\\', '/'));
        }

        private static LevelDefinition Load(string relativePath)
        {
            string path = Path.Combine(TestBoards.LevelsRoot(), relativePath);
            return LevelParser.Parse(File.ReadAllText(path), relativePath);
        }

        [Test]
        public void World1_HasTenLevels()
        {
            Assert.AreEqual(10, LevelFiles().Count(f => f.StartsWith("World1/")));
        }

        [TestCaseSource(nameof(LevelFiles))]
        public void Level_IdMatchesFileName(string file)
        {
            Assert.AreEqual(Path.GetFileNameWithoutExtension(file), Load(file).Id);
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
        public void Level_LimitIsParPlusWorldSlack(string file)
        {
            LevelDefinition level = Load(file);
            Assert.That(level.World, Is.InRange(1, 10));
            Assert.AreEqual(level.Par + Slack[level.World], level.Limit);
        }
    }
}
