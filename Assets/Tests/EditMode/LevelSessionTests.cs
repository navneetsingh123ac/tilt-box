using NUnit.Framework;
using TiltBox.Core;

namespace TiltBox.Core.Tests
{
    public class LevelSessionTests
    {
        // One red block in the top-left, its bin bottom-right: Right then Down wins (par 2).
        private static LevelDefinition MakeLevel(int par = 2, int limit = 4)
        {
            var (layout, state) = TestBoards.Parse(
                "r..",
                "...",
                "..R");
            return new LevelDefinition("s_01", 1, layout, state, par, limit, LevelKind.Normal);
        }

        [Test]
        public void StartsPlayingWithFullMoves()
        {
            var s = new LevelSession(MakeLevel());
            Assert.AreEqual(SessionStatus.Playing, s.Status);
            Assert.AreEqual(0, s.MovesUsed);
            Assert.AreEqual(4, s.MovesLeft);
            Assert.AreEqual(2, s.MovesLeftForThreeStars);
            Assert.IsFalse(s.CanUndo);
        }

        [Test]
        public void SwipeCostsOneMove()
        {
            var s = new LevelSession(MakeLevel());
            Assert.IsTrue(s.Swipe(Direction.Right).Changed);
            Assert.AreEqual(1, s.MovesUsed);
            Assert.AreEqual(3, s.MovesLeft);
            Assert.AreEqual(1, s.MovesLeftForThreeStars);
        }

        [Test]
        public void IgnoredSwipe_CostsNothing()
        {
            var s = new LevelSession(MakeLevel());
            SwipeResult r = s.Swipe(Direction.Left);
            Assert.IsFalse(r.Changed);
            Assert.AreEqual(0, s.MovesUsed);
            Assert.IsFalse(s.CanUndo);
        }

        [Test]
        public void WinningAtPar_GivesThreeStars()
        {
            var s = new LevelSession(MakeLevel());
            s.Swipe(Direction.Right);
            s.Swipe(Direction.Down);
            Assert.AreEqual(SessionStatus.Won, s.Status);
            Assert.AreEqual(3, s.Stars);
            Assert.IsNull(s.Swipe(Direction.Up), "no input after a win");
            Assert.IsFalse(s.CanUndo, "no undo after a win");
        }

        [TestCase(2, 2, 3)]
        [TestCase(1, 2, 3)]
        [TestCase(3, 2, 2)]
        [TestCase(4, 2, 2)]
        [TestCase(5, 2, 1)]
        [TestCase(20, 2, 1)]
        public void StarRules(int moves, int par, int expected)
        {
            Assert.AreEqual(expected, LevelSession.StarsFor(moves, par));
        }

        [Test]
        public void UsingAllMovesWithoutWinning_IsOutOfMoves()
        {
            var s = new LevelSession(MakeLevel(par: 2, limit: 2));
            s.Swipe(Direction.Down);
            s.Swipe(Direction.Up); // back where it started: 2 swipes used, not won
            Assert.AreEqual(SessionStatus.OutOfMoves, s.Status);
            Assert.AreEqual(0, s.MovesLeft);
            Assert.IsNull(s.Swipe(Direction.Right));
        }

        [Test]
        public void AddMoves_ResumesAfterOutOfMoves()
        {
            var s = new LevelSession(MakeLevel(par: 2, limit: 2));
            s.Swipe(Direction.Down);
            s.Swipe(Direction.Up);
            s.AddMoves(5);
            Assert.AreEqual(SessionStatus.Playing, s.Status);
            Assert.AreEqual(5, s.MovesLeft);
        }

        [Test]
        public void Undo_RestoresStateAndRefundsMove()
        {
            var level = MakeLevel();
            var s = new LevelSession(level);
            s.Swipe(Direction.Right);
            Assert.IsTrue(s.Undo());
            Assert.AreSame(level.Start, s.State);
            Assert.AreEqual(0, s.MovesUsed);
            Assert.IsFalse(s.Undo());
        }

        [Test]
        public void Undo_LeavesOutOfMoves()
        {
            var s = new LevelSession(MakeLevel(par: 2, limit: 2));
            s.Swipe(Direction.Down);
            s.Swipe(Direction.Up);
            Assert.IsTrue(s.Undo());
            Assert.AreEqual(SessionStatus.Playing, s.Status);
        }

        [Test]
        public void Restart_ResetsEverything()
        {
            var level = MakeLevel(par: 2, limit: 2);
            var s = new LevelSession(level);
            s.Swipe(Direction.Down);
            s.AddMoves(5);
            s.Restart();
            Assert.AreSame(level.Start, s.State);
            Assert.AreEqual(0, s.MovesUsed);
            Assert.AreEqual(2, s.MovesLeft, "bonus moves do not survive a restart");
            Assert.IsFalse(s.CanUndo);
        }
    }
}
