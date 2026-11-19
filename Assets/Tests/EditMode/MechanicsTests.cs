using NUnit.Framework;
using TiltBox.Core;

namespace TiltBox.Core.Tests
{
    /// <summary>World 2 ice and World 3 one-way gates (GDD §3 steps 1, 3, 4, 7 and §4).</summary>
    public class MechanicsTests
    {
        // --- Ice ---

        [Test]
        public void IcedBlock_DoesNotMoveOnFirstSwipe_ThenCracks()
        {
            var (layout, state) = TestBoards.Parse("r..R");
            state = TestBoards.Ice(state, 0);
            SwipeResult first = SwipeResolver.Resolve(layout, state, Direction.Right);
            Assert.IsTrue(first.Changed, "cracking ice counts as a change (costs a move)");
            Assert.AreEqual(0, first.Moves.Count);
            CollectionAssert.AreEqual(new[] { 0 }, first.Cracked);
            Assert.AreEqual(new GridPos(0, 0), first.After.PositionOf(0));
            Assert.IsFalse(first.After.IsIced(0));

            SwipeResult second = SwipeResolver.Resolve(layout, first.After, Direction.Right);
            Assert.IsTrue(second.After.IsSolved);
        }

        [Test]
        public void IcedBlock_ActsAsWallForOthers_ThatSwipe()
        {
            var (layout, state) = TestBoards.Parse("b.r..");
            state = TestBoards.Ice(state, 1); // red is iced
            SwipeResult r = SwipeResolver.Resolve(layout, state, Direction.Right);
            Assert.AreEqual(new GridPos(1, 0), r.After.PositionOf(0), "blue stops against the iced red");
            Assert.AreEqual(new GridPos(2, 0), r.After.PositionOf(1), "red stays put while cracking");
        }

        [Test]
        public void IceCracks_WhicheverWayYouSwipe()
        {
            var (layout, state) = TestBoards.Parse("r..", "...", "..R");
            state = TestBoards.Ice(state, 0);
            SwipeResult r = SwipeResolver.Resolve(layout, state, Direction.Left); // left would not move it anyway
            Assert.IsTrue(r.Changed);
            Assert.IsFalse(r.After.IsIced(0));
        }

        [Test]
        public void IgnoredSwipe_WhenNothingMovesAndNoIce()
        {
            var (layout, state) = TestBoards.Parse("r..", "...", "..R");
            Assert.IsFalse(SwipeResolver.Resolve(layout, state, Direction.Left).Changed);
        }

        [Test]
        public void CanonicalKey_DistinguishesIcedFromPlain()
        {
            var (_, state) = TestBoards.Parse("r.R");
            Assert.AreNotEqual(state.CanonicalKey, TestBoards.Ice(state, 0).CanonicalKey);
        }

        [Test]
        public void Solver_CountsTheCrackingSwipe()
        {
            var (layout, state) = TestBoards.Parse("r.R", "...", "...");
            Assert.AreEqual(1, Solver.Solve(layout, state).Par);
            Assert.AreEqual(2, Solver.Solve(layout, TestBoards.Ice(state, 0)).Par);
        }

        [Test]
        public void Session_IceCrackCostsAMoveAndUndoRestoresIce()
        {
            var (layout, state) = TestBoards.Parse("r.R", "...", "...");
            state = TestBoards.Ice(state, 0);
            var session = new LevelSession(new LevelDefinition("ice", 2, layout, state, 2, 7, LevelKind.Teach));
            session.Swipe(Direction.Up);
            Assert.AreEqual(1, session.MovesUsed);
            Assert.IsFalse(session.State.IsIced(0));
            session.Undo();
            Assert.IsTrue(session.State.IsIced(0));
        }

        // --- One-way gates ---

        [Test]
        public void Gate_LetsBlocksPassInItsDirection()
        {
            var (layout, state) = TestBoards.Parse("r.>.R");
            Assert.IsTrue(SwipeResolver.Resolve(layout, state, Direction.Right).After.IsSolved);
        }

        [Test]
        public void Gate_IsAWallFromOtherDirections()
        {
            var (layout, state) = TestBoards.Parse("R.>.r");
            CollectionAssert.AreEqual(new[] { "R.>r." },
                TestBoards.Render(layout, SwipeResolver.Resolve(layout, state, Direction.Left).After));
        }

        [Test]
        public void Gate_BlocksVerticalEntryWhenPointingSideways()
        {
            var (layout, state) = TestBoards.Parse("r", ">", ".", "R");
            SwipeResult r = SwipeResolver.Resolve(layout, state, Direction.Down);
            Assert.IsFalse(r.Changed, "a right-pointing gate is a wall for a block moving down");
        }

        [Test]
        public void BlockCanStopOnAGate_AndLeaveAnyWay()
        {
            // Red passes the gate and stops on it (wall behind); then it can leave downwards.
            var (layout, state) = TestBoards.Parse("r>#", "...", ".R.");
            BoardState s = SwipeResolver.Resolve(layout, state, Direction.Right).After;
            Assert.AreEqual(new GridPos(1, 0), s.PositionOf(0));
            Assert.IsTrue(SwipeResolver.Resolve(layout, s, Direction.Down).After.IsSolved);
        }

        // --- Metrics that power the generator ---

        [Test]
        public void Analyse_CountsIceStopsAndGateUse()
        {
            var (layout, state) = TestBoards.Parse("b.r..");
            state = TestBoards.Ice(state, 1);
            MechanicUse use = LevelMetrics.Analyse(layout, state, new[] { Direction.Right });
            Assert.AreEqual(1, use.IceStops);

            var (gl, gs) = TestBoards.Parse("r.>.R");
            Assert.AreEqual(1, LevelMetrics.Analyse(gl, gs, new[] { Direction.Right }).GatePasses);
        }

        [Test]
        public void MechanicIsEssential_WhenRemovingItMakesTheLevelUnwinnable()
        {
            // Blue must stop against the iced red to end up under its bin; without ice it slides past forever.
            var (layout, state) = TestBoards.Parse(
                "..B.",
                "b..r",
                "#..#",
                "R...");
            BoardState iced = TestBoards.Ice(state, 1);
            SolveResult withIce = Solver.Solve(layout, iced);
            Assert.IsTrue(withIce.Solved, "the iced version must be winnable");
            // Essential exactly when the ice-free version cannot be won.
            Assert.AreEqual(!Solver.Solve(layout, state).Solved, LevelMetrics.MechanicIsEssential(layout, iced));
        }

        [Test]
        public void MechanicIsEssential_FalseWithoutMechanics()
        {
            var (layout, state) = TestBoards.Parse("r.R");
            Assert.IsFalse(LevelMetrics.MechanicIsEssential(layout, state));
        }
    }
}
