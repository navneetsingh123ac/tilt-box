using System.Linq;
using NUnit.Framework;
using TiltBox.Core;

namespace TiltBox.Core.Tests
{
    public class LevelWriterTests
    {
        [Test]
        public void RoundTrip_PreservesEverything()
        {
            var (layout, state) = TestBoards.Parse(
                "r.>#",
                ".b^.",
                "<..v",
                "R.B.");
            state = TestBoards.Ice(state, 1);
            var level = new LevelDefinition("w3_07", 3, layout, state, 4, 9, LevelKind.Normal);
            LevelDefinition back = LevelParser.Parse(LevelWriter.ToJson(level, "a note"));
            Assert.AreEqual(LevelWriter.ToJson(level, "a note"), LevelWriter.ToJson(back, "a note"));
            Assert.AreEqual(LevelGenerator.KeyOf(layout, state), LevelGenerator.KeyOf(back.Layout, back.Start));
            Assert.AreEqual(4, back.Par);
            Assert.AreEqual(9, back.Limit);
            Assert.AreEqual(LevelKind.Normal, back.Kind);
            Assert.IsTrue(back.Start.IsIced(1));
            Assert.IsFalse(back.Start.IsIced(0));
        }
    }

    public class DifficultyPlanTests
    {
        [TestCase(1, LevelKind.Teach)]
        [TestCase(3, LevelKind.Teach)]
        [TestCase(4, LevelKind.Normal)]
        [TestCase(7, LevelKind.Normal)]
        [TestCase(8, LevelKind.Easy)]
        [TestCase(9, LevelKind.Easy)]
        [TestCase(10, LevelKind.Hard)]
        [TestCase(11, LevelKind.Normal)]
        [TestCase(20, LevelKind.Hard)]
        [TestCase(28, LevelKind.Easy)]
        [TestCase(30, LevelKind.Hard)]
        public void WavePattern(int number, LevelKind expected)
        {
            Assert.AreEqual(expected, DifficultyPlan.KindFor(2, number));
        }

        [Test]
        public void HardIsBandMaxPlusTwo()
        {
            DifficultyPlan.ParRange(2, 10, out int min, out int max);
            Assert.AreEqual(10, min);
            Assert.AreEqual(10, max);
        }

        [Test]
        public void NormalParsRiseAcrossTheWorldAndStayInBand()
        {
            int previousMid = 0;
            foreach (int n in Enumerable.Range(1, 30).Where(n => DifficultyPlan.KindFor(3, n) == LevelKind.Normal))
            {
                DifficultyPlan.ParRange(3, n, out int min, out int max);
                Assert.That(min, Is.GreaterThanOrEqualTo(5));
                Assert.That(max, Is.LessThanOrEqualTo(9));
                Assert.That(min + max, Is.GreaterThanOrEqualTo(previousMid));
                previousMid = min + max;
            }
        }

        [Test]
        public void LimitIsParPlusSlack()
        {
            Assert.AreEqual(9, DifficultyPlan.Limit(1, 3));
            Assert.AreEqual(9, DifficultyPlan.Limit(2, 4));
            Assert.AreEqual(14, DifficultyPlan.Limit(3, 9));
        }

        [Test]
        public void SpecsAreConsistent()
        {
            for (int world = 1; world <= DifficultyPlan.SupportedWorlds; world++)
            for (int n = 1; n <= DifficultyPlan.LevelsPerWorld; n++)
            {
                SlotSpec s = DifficultyPlan.SpecFor(world, n);
                Assert.That(s.ParMin, Is.LessThanOrEqualTo(s.ParMax), s.ToString());
                Assert.That(s.Width, Is.InRange(LevelParser.MinSize, LevelParser.MaxWidth), s.ToString());
                Assert.That(s.Height, Is.InRange(LevelParser.MinSize, LevelParser.MaxHeight), s.ToString());
                Assert.That(s.MaxBlocks, Is.LessThanOrEqualTo(LevelParser.MaxBlocks), s.ToString());
                Assert.That(s.MinBlocks, Is.GreaterThanOrEqualTo(s.MinColours), s.ToString());
            }
        }
    }

    public class LevelGeneratorTests
    {
        private static SlotSpec SmallSpec()
        {
            SlotSpec s = DifficultyPlan.SpecFor(1, 14);
            s.Width = 4;
            s.Height = 4;
            s.MinWalls = 1;
            s.MaxWalls = 3;
            s.ParMin = 2;
            s.ParMax = 4;
            return s;
        }

        [Test]
        public void SameSeed_GivesSameBoard()
        {
            SlotSpec spec = SmallSpec();
            for (int seed = 1; seed < 200; seed++)
            {
                Candidate a = LevelGenerator.TryBuild(spec, seed);
                Candidate b = LevelGenerator.TryBuild(spec, seed);
                Assert.AreEqual(a == null, b == null);
                if (a != null) Assert.AreEqual(a.Key, b.Key);
            }
        }

        [Test]
        public void Candidates_SatisfyTheirSlot()
        {
            SlotSpec spec = SmallSpec();
            var found = LevelGenerator.Generate(spec, 7, 600, 10);
            Assert.That(found.Count, Is.GreaterThan(0), "a 4x4 par 2-4 slot should be easy to fill");
            Assert.AreEqual(found.Count, found.Select(c => c.Key).Distinct().Count(), "no duplicates");
            for (int i = 0; i < found.Count; i++)
            {
                Candidate c = found[i];
                if (i > 0) Assert.That(c.Score, Is.LessThanOrEqualTo(found[i - 1].Score), "sorted best first");
                SolveResult check = Solver.Solve(c.Level.Layout, c.Level.Start);
                Assert.AreEqual(check.Par, c.Level.Par);
                Assert.That(c.Level.Par, Is.InRange(spec.ParMin, spec.ParMax));
                Assert.AreEqual(c.Level.Par + spec.Slack, c.Level.Limit);
                Assert.That(c.RandomWin, Is.LessThanOrEqualTo(spec.MaxRandomWin));
                Assert.That(c.Solution.OptimalSolutionCount, Is.LessThanOrEqualTo(spec.MaxOptimalSolutions));
                if (spec.RequireDeadEnd) Assert.That(c.DeadEnds, Is.GreaterThan(0));
                // Every generated level must survive the strict parser.
                LevelParser.Parse(LevelWriter.ToJson(c.Level));
            }
        }

        [Test]
        public void IceSlot_CandidatesUseIce()
        {
            SlotSpec spec = DifficultyPlan.SpecFor(2, 5);
            spec.Width = 5;
            spec.Height = 5;
            var found = LevelGenerator.Generate(spec, 1, 4000, 3);
            Assert.That(found.Count, Is.GreaterThan(0));
            foreach (Candidate c in found)
            {
                Assert.IsTrue(c.Level.Start.AnyIced);
                Assert.That(c.Use.IceStops, Is.GreaterThan(0));
            }
        }
    }
}
