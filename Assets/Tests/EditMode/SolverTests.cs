using System.Linq;
using NUnit.Framework;
using TiltBox.Core;

namespace TiltBox.Core.Tests
{
    public class SolverTests
    {
        private static BoardState Play(BoardLayout layout, BoardState state, System.Collections.Generic.IEnumerable<Direction> moves)
        {
            foreach (Direction d in moves) state = SwipeResolver.Resolve(layout, state, d).After;
            return state;
        }

        [Test]
        public void GddExample_HasParThree()
        {
            var (layout, state) = TestBoards.Parse(
                ".#..R",
                "r....",
                "..b..",
                ".#..B",
                ".....");
            SolveResult r = Solver.Solve(layout, state);
            Assert.IsTrue(r.Solved);
            Assert.AreEqual(3, r.Par);
            Assert.IsTrue(Play(layout, state, r.Solution).IsSolved, "returned solution must actually win");
        }

        [Test]
        public void OneSwipeLevel()
        {
            var (layout, state) = TestBoards.Parse("r.R", "...", "...");
            SolveResult r = Solver.Solve(layout, state);
            Assert.AreEqual(1, r.Par);
            Assert.AreEqual(Direction.Right, r.FirstMove);
            Assert.AreEqual(1, r.OptimalSolutionCount);
        }

        [Test]
        public void CountsDistinctOptimalSolutions()
        {
            // Bin in the far corner: Right,Down and Down,Right both win in 2.
            var (layout, state) = TestBoards.Parse("r..", "...", "..R");
            SolveResult r = Solver.Solve(layout, state);
            Assert.AreEqual(2, r.Par);
            Assert.AreEqual(2, r.OptimalSolutionCount);
        }

        [Test]
        public void AlreadySolved_HasParZero()
        {
            var (layout, _) = TestBoards.Parse("..R");
            var empty = new BoardState(new GridPos[0], new BlockColor[0]);
            Assert.AreEqual(0, Solver.Solve(layout, empty).Par);
        }

        [Test]
        public void Unsolvable_IsReported()
        {
            // The bin is boxed in by walls: no block can ever reach it.
            var (layout, state) = TestBoards.Parse("r.#R", "..##");
            SolveResult r = Solver.Solve(layout, state);
            Assert.IsFalse(r.Solved);
            Assert.AreEqual(-1, r.Par);
            Assert.IsFalse(r.HitStateCap);
            Assert.IsNull(r.FirstMove);
        }

        [Test]
        public void DepthLimit_IsRespected()
        {
            var (layout, state) = TestBoards.Parse(
                ".#..R",
                "r....",
                "..b..",
                ".#..B",
                ".....");
            Assert.IsFalse(Solver.Solve(layout, state, maxDepth: 2).Solved);
            Assert.IsTrue(Solver.Solve(layout, state, maxDepth: 3).Solved);
        }

        [Test]
        public void Hint_FromMidLevel_LeadsToWin()
        {
            var (layout, state) = TestBoards.Parse(
                ".#..R",
                "r....",
                "..b..",
                ".#..B",
                ".....");
            BoardState s = state;
            for (int i = 0; i < 10 && !s.IsSolved; i++)
            {
                Direction? hint = Solver.Hint(layout, s);
                Assert.IsTrue(hint.HasValue);
                s = SwipeResolver.Resolve(layout, s, hint.Value).After;
            }
            Assert.IsTrue(s.IsSolved);
        }

        [Test]
        public void SolutionUsesOnlyChangingSwipes()
        {
            var (layout, state) = TestBoards.Parse(
                ".#..R",
                "r....",
                "..b..",
                ".#..B",
                ".....");
            BoardState s = state;
            foreach (Direction d in Solver.Solve(layout, state).Solution.ToList())
            {
                SwipeResult r = SwipeResolver.Resolve(layout, s, d);
                Assert.IsTrue(r.Changed);
                s = r.After;
            }
        }
    }
}
