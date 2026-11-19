using NUnit.Framework;
using TiltBox.Core;

namespace TiltBox.Core.Tests
{
    public class SwipeResolverTests
    {
        private static string[] After(Direction dir, params string[] rows)
        {
            var (layout, state) = TestBoards.Parse(rows);
            return TestBoards.Render(layout, SwipeResolver.Resolve(layout, state, dir).After);
        }

        [Test]
        public void Block_SlidesUntilBoardEdge()
        {
            CollectionAssert.AreEqual(new[] { "...r" }, After(Direction.Right, "r..."));
            CollectionAssert.AreEqual(new[] { "r..." }, After(Direction.Left, "...r"));
        }

        [Test]
        public void Up_IsTowardsRowZero()
        {
            CollectionAssert.AreEqual(new[] { "r", ".", "." }, After(Direction.Up, ".", ".", "r"));
            CollectionAssert.AreEqual(new[] { ".", ".", "r" }, After(Direction.Down, "r", ".", "."));
        }

        [Test]
        public void Block_StopsBeforeWall()
        {
            CollectionAssert.AreEqual(new[] { ".r#.." }, After(Direction.Right, "r.#.."));
        }

        [Test]
        public void Blocks_StackAgainstEachOther_InOneSwipe()
        {
            // Everything moves at once: the back block ends next to the front block, not where it would stop alone.
            CollectionAssert.AreEqual(new[] { "...br" }, After(Direction.Right, "b.r.."));
            CollectionAssert.AreEqual(new[] { "rb..." }, After(Direction.Left, "..r.b"));
        }

        [Test]
        public void Block_DropsIntoBinOfItsColour()
        {
            var (layout, state) = TestBoards.Parse("r..R");
            SwipeResult r = SwipeResolver.Resolve(layout, state, Direction.Right);
            Assert.IsTrue(r.After.IsBinned(0));
            Assert.IsTrue(r.After.IsSolved);
            Assert.AreEqual(1, r.Moves.Count);
            Assert.AreEqual(new GridPos(3, 0), r.Moves[0].To);
            Assert.IsTrue(r.Moves[0].Binned);
            Assert.AreEqual(3, r.Moves[0].Distance);
        }

        [Test]
        public void WrongColourBin_ActsAsWall()
        {
            CollectionAssert.AreEqual(new[] { "..bR" }, After(Direction.Right, "b..R"));
        }

        [Test]
        public void BlockBehind_FollowsIntoSpaceLeftByBinnedBlock()
        {
            // Red bins first (front-first), so the red that follows also reaches the bin in the same swipe.
            var (layout, state) = TestBoards.Parse("r.r.R");
            SwipeResult res = SwipeResolver.Resolve(layout, state, Direction.Right);
            Assert.IsTrue(res.After.IsSolved);
            Assert.AreEqual(2, res.BinnedCount);
        }

        [Test]
        public void BlockBehindBinnedBlock_StopsAtWrongBin()
        {
            // Red drops into R, blue then slides up to R and stops: R is a wall for blue.
            CollectionAssert.AreEqual(new[] { "...bR" }, After(Direction.Right, "b.r.R"));
        }

        [Test]
        public void BinnedBlocks_AreIgnoredByLaterSwipes()
        {
            var (layout, state) = TestBoards.Parse("r.R", "...");
            BoardState s = SwipeResolver.Resolve(layout, state, Direction.Right).After;
            SwipeResult r = SwipeResolver.Resolve(layout, s, Direction.Left);
            Assert.IsFalse(r.Changed);
        }

        [Test]
        public void SwipeThatMovesNothing_IsIgnored()
        {
            var (layout, state) = TestBoards.Parse("r..", "...", "..R");
            SwipeResult r = SwipeResolver.Resolve(layout, state, Direction.Left);
            Assert.IsFalse(r.Changed);
            Assert.AreSame(state, r.After);
            Assert.AreEqual(0, r.Moves.Count);
        }

        [Test]
        public void Moves_ListOnlyBlocksThatMoved_WithStableIndices()
        {
            var (layout, state) = TestBoards.Parse("r..", "..b", "RB.");
            SwipeResult r = SwipeResolver.Resolve(layout, state, Direction.Right);
            Assert.AreEqual(1, r.Moves.Count);
            Assert.AreEqual(0, r.Moves[0].Block);
            Assert.AreEqual(new GridPos(0, 0), r.Moves[0].From);
            Assert.AreEqual(new GridPos(2, 0), r.Moves[0].To);
        }

        [Test]
        public void Resolve_DoesNotMutateInputState()
        {
            var (layout, state) = TestBoards.Parse("r..R");
            SwipeResolver.Resolve(layout, state, Direction.Right);
            Assert.AreEqual(new GridPos(0, 0), state.PositionOf(0));
            Assert.IsFalse(state.IsBinned(0));
        }

        [Test]
        public void GddExample_ThreeSwipeSolution_PlaysOutAsDocumented()
        {
            // GDD §2 example board.
            var (layout, state) = TestBoards.Parse(
                ".#..R",
                "r....",
                "..b..",
                ".#..B",
                ".....");

            BoardState s1 = SwipeResolver.Resolve(layout, state, Direction.Right).After;
            Assert.AreEqual(new GridPos(4, 1), s1.PositionOf(0), "red after right");
            Assert.AreEqual(new GridPos(4, 2), s1.PositionOf(1), "blue after right");

            BoardState s2 = SwipeResolver.Resolve(layout, s1, Direction.Up).After;
            Assert.IsTrue(s2.IsBinned(0), "red in red bin");
            Assert.AreEqual(new GridPos(4, 1), s2.PositionOf(1), "blue stops under the red bin");

            BoardState s3 = SwipeResolver.Resolve(layout, s2, Direction.Down).After;
            Assert.IsTrue(s3.IsSolved, "blue in blue bin");
        }

        [Test]
        public void CanonicalKey_TreatsSameColourBlocksAsInterchangeable()
        {
            var a = new BoardState(new[] { new GridPos(0, 0), new GridPos(1, 0) }, new[] { BlockColor.Red, BlockColor.Red });
            var b = new BoardState(new[] { new GridPos(1, 0), new GridPos(0, 0) }, new[] { BlockColor.Red, BlockColor.Red });
            var c = new BoardState(new[] { new GridPos(1, 0), new GridPos(0, 0) }, new[] { BlockColor.Red, BlockColor.Blue });
            var d = new BoardState(new[] { new GridPos(0, 0), new GridPos(1, 0) }, new[] { BlockColor.Red, BlockColor.Blue });
            Assert.AreEqual(a.CanonicalKey, b.CanonicalKey);
            Assert.AreNotEqual(c.CanonicalKey, d.CanonicalKey);
        }

        [Test]
        public void CanonicalKey_IgnoresBinnedBlocks()
        {
            var a = new BoardState(new[] { new GridPos(0, 0), new GridPos(2, 0) }, new[] { BlockColor.Red, BlockColor.Blue },
                new[] { false, true });
            var b = new BoardState(new[] { new GridPos(0, 0), new GridPos(3, 0) }, new[] { BlockColor.Red, BlockColor.Blue },
                new[] { false, true });
            Assert.AreEqual(a.CanonicalKey, b.CanonicalKey);
        }
    }
}
