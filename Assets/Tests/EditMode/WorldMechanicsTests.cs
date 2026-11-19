using System.Linq;
using NUnit.Framework;
using TiltBox.Core;

namespace TiltBox.Core.Tests
{
    /// <summary>Worlds 4-9 mechanics (GDD §3, §4) and the choices the GDD leaves open.</summary>
    public class WorldMechanicsTests
    {
        private static string[] After(Direction dir, params string[] rows)
        {
            var (layout, state) = TestBoards.Parse(rows);
            return TestBoards.Render(layout, SwipeResolver.Resolve(layout, state, dir).After);
        }

        // --- Sticky pads (World 4) ---

        [Test]
        public void Sticky_StopsABlockOnIt()
        {
            CollectionAssert.AreEqual(new[] { "..r.." }, After(Direction.Right, "r.s.."));
        }

        [Test]
        public void Sticky_NextSwipeMovesOffNormally()
        {
            var (layout, state) = TestBoards.Parse("r.s..");
            BoardState s = SwipeResolver.Resolve(layout, state, Direction.Right).After;
            Assert.AreEqual(new GridPos(2, 0), s.PositionOf(0));
            s = SwipeResolver.Resolve(layout, s, Direction.Right).After;
            Assert.AreEqual(new GridPos(4, 0), s.PositionOf(0));
        }

        // --- Portals (World 5) ---

        [Test]
        public void Portal_ExitsFromPartnerAndKeepsSliding()
        {
            // Red enters portal 1 at x=1 of row 0, comes out at row 1 x=1 and slides on to the bin.
            var (layout, state) = TestBoards.Parse("r1..", ".1.R");
            SwipeResult r = SwipeResolver.Resolve(layout, state, Direction.Right);
            Assert.IsTrue(r.After.IsSolved);
            Assert.IsTrue(r.Moves[0].Teleported);
            Assert.AreEqual(2, r.Moves[0].Segments.Count);
            Assert.AreEqual(new GridPos(1, 0), r.Moves[0].Segments[0].To);
            Assert.AreEqual(new GridPos(1, 1), r.Moves[0].Segments[1].From);
        }

        [Test]
        public void Portal_IsAWallWhenItsExitIsBlocked()
        {
            // Blue rests on the exit portal and can't move right (wall), so red stops before the entry portal.
            var (layout, _) = TestBoards.Parse("r1..", ".1#R");
            var state = new BoardState(new[] { new GridPos(0, 0), new GridPos(1, 1) }, new[] { BlockColor.Red, BlockColor.Blue });
            BoardState s = SwipeResolver.Resolve(layout, state, Direction.Right).After;
            Assert.AreEqual(new GridPos(0, 0), s.PositionOf(0));
        }

        [Test]
        public void Portal_LoopStopsInsteadOfSpinningForever()
        {
            // Both portals in one row facing each other: entering the same portal twice in one slide is refused.
            var (layout, state) = TestBoards.Parse("1r1");
            SwipeResult r = SwipeResolver.Resolve(layout, state, Direction.Right);
            Assert.That(r.After.PositionOf(0).X, Is.InRange(0, 2));
        }

        // --- Paint (World 6) ---

        [Test]
        public void Paint_RecoloursAndKeepsSliding()
        {
            var (layout, state) = TestBoards.Parse("r...B");
            layout = TestBoards.WithCell(layout, 2, 0, Cell.PaintOf(BlockColor.Blue));
            SwipeResult r = SwipeResolver.Resolve(layout, state, Direction.Right);
            Assert.IsTrue(r.After.IsSolved, "red becomes blue on the paint and drops into the blue bin");
        }

        [Test]
        public void Paint_DoesNotRecolourKeys()
        {
            var (layout, state) = TestBoards.Parse("x...O", "r.R..");
            layout = TestBoards.WithCell(layout, 2, 0, Cell.PaintOf(BlockColor.Blue));
            BoardState s = SwipeResolver.Resolve(layout, state, Direction.Right).After;
            Assert.IsTrue(s.IsBinned(0), "the key passes the paint and fills the keyhole");
        }

        // --- Keys and locks (World 7) ---

        [Test]
        public void Key_FillsKeyhole_AndLocksOpenAtTheEndOfTheSwipe()
        {
            var (layout, state) = TestBoards.Parse(
                "x..O",
                "r.LR");
            SwipeResult r = SwipeResolver.Resolve(layout, state, Direction.Right);
            Assert.IsTrue(r.After.IsBinned(0), "key is used up");
            Assert.IsTrue(r.LocksOpened);
            Assert.IsTrue(r.After.LocksOpen);
            Assert.AreEqual(new GridPos(1, 1), r.After.PositionOf(1), "the lock was still closed during this swipe");
            Assert.IsTrue(SwipeResolver.Resolve(layout, r.After, Direction.Right).After.IsSolved, "next swipe passes the open lock");
        }

        [Test]
        public void Keyhole_StopsColouredBlocks_AndBinsStopKeys()
        {
            CollectionAssert.AreEqual(new[] { "..rO", "..xR" }, After(Direction.Right, "r..O", "x..R"));
        }

        [Test]
        public void Keys_DontNeedToBeUsedToWin()
        {
            var (layout, state) = TestBoards.Parse("r.R", "x.O");
            BoardState s = SwipeResolver.Resolve(layout, state, Direction.Right).After;
            Assert.IsTrue(s.IsSolved);
        }

        // --- Conveyors (World 8) ---

        [Test]
        public void Conveyor_MovesARestingBlockOneCellAfterTheSwipe()
        {
            // Red slides right onto the conveyor at x=3 (wall behind), then the down-conveyor moves it to row 1.
            var (layout, state) = TestBoards.Parse("r..]#", "....R");
            SwipeResult r = SwipeResolver.Resolve(layout, state, Direction.Right);
            Assert.AreEqual(1, r.ConveyorMoves.Count);
            Assert.AreEqual(new GridPos(3, 1), r.After.PositionOf(0));
        }

        [Test]
        public void Conveyor_NoChaining_AndBlockedTargetStays()
        {
            // Two right-conveyors in a row: a block moves one cell per swipe, not two.
            var (layout, state) = TestBoards.Parse("b}}..", "#####");
            SwipeResult r = SwipeResolver.Resolve(layout, state, Direction.Up); // nothing slides; conveyors act
            Assert.AreEqual(0, r.ConveyorMoves.Count, "block is on floor, not on a conveyor");
            var (l2, s2) = TestBoards.Parse("}}b..");
            // Put the block on the first conveyor instead.
            s2 = new BoardState(new[] { new GridPos(0, 0) }, new[] { BlockColor.Blue });
            l2 = TestBoards.WithCell(l2, 4, 0, Cell.Bin(BlockColor.Blue));
            SwipeResult r2 = SwipeResolver.Resolve(l2, s2, Direction.Up);
            Assert.IsTrue(r2.Changed, "a conveyor step alone counts as a change");
            Assert.AreEqual(new GridPos(1, 0), r2.After.PositionOf(0));
        }

        [Test]
        public void Conveyor_CanDropABlockIntoItsBin()
        {
            var (layout, state) = TestBoards.Parse("r}R");
            BoardState s = new BoardState(new[] { new GridPos(1, 0) }, new[] { BlockColor.Red });
            Assert.IsTrue(SwipeResolver.Resolve(layout, s, Direction.Up).After.IsSolved);
        }

        // --- Linked blocks (World 9) ---

        [Test]
        public void Linked_MoveTogether_AndStopWhenEitherWould()
        {
            var (layout, state) = TestBoards.Parse(
                "r....",
                "b..#.");
            state = TestBoards.Link(state, 0, 1);
            BoardState s = SwipeResolver.Resolve(layout, state, Direction.Right).After;
            Assert.AreEqual(new GridPos(2, 0), s.PositionOf(0), "red stops with blue, which hits the wall");
            Assert.AreEqual(new GridPos(2, 1), s.PositionOf(1));
        }

        [Test]
        public void Linked_BinSeparately_ThenTheOtherIsFree()
        {
            var (layout, state) = TestBoards.Parse(
                "r.R..",
                "b....",
                "....B");
            state = TestBoards.Link(state, 0, 1);
            SwipeResult r = SwipeResolver.Resolve(layout, state, Direction.Right);
            Assert.IsTrue(r.After.IsBinned(0), "red drops into its bin");
            Assert.AreEqual(new GridPos(4, 1), r.After.PositionOf(1), "blue, now free, slides on alone");
            Assert.AreEqual(-1, r.After.PartnerOf(1));
        }

        [Test]
        public void Linked_SameLane_MoveAsOne()
        {
            var (layout, state) = TestBoards.Parse("rb...");
            state = TestBoards.Link(state, 0, 1);
            CollectionAssert.AreEqual(new[] { "...rb" }, TestBoards.Render(layout, SwipeResolver.Resolve(layout, state, Direction.Right).After));
        }

        [Test]
        public void Linked_PairTreatsPortalsAsWalls()
        {
            var (layout, state) = TestBoards.Parse("r1..", "b...", ".1..");
            state = TestBoards.Link(state, 0, 1);
            BoardState s = SwipeResolver.Resolve(layout, state, Direction.Right).After;
            Assert.AreEqual(new GridPos(0, 0), s.PositionOf(0));
        }

        [Test]
        public void CanonicalKey_DistinguishesLinkedFromFree()
        {
            var (_, state) = TestBoards.Parse("rb.");
            Assert.AreNotEqual(state.CanonicalKey, TestBoards.Link(state, 0, 1).CanonicalKey);
        }

        // --- Tower rules and dailies (§9) ---

        [Test]
        public void ForbiddenDirection_IsIgnoredAndCostsNothing()
        {
            var (layout, state) = TestBoards.Parse("r.R");
            var level = new LevelDefinition("t", 4, layout, state, 1, 5, LevelKind.Hard, new RuleSet(Direction.Right, false, 1));
            var session = new LevelSession(level);
            Assert.IsFalse(session.Swipe(Direction.Right).Changed);
            Assert.AreEqual(0, session.MovesUsed);
            Assert.IsFalse(Solver.Solve(layout, state, 22, 1000, level.Rules).Solved, "no other way to reach the bin");
        }

        [Test]
        public void ReversedRules_MoveTheBoardTheOtherWay()
        {
            var (layout, state) = TestBoards.Parse("r.R");
            var level = new LevelDefinition("t", 5, layout, state, 1, 5, LevelKind.Hard, new RuleSet(null, true, 1));
            var session = new LevelSession(level);
            session.Swipe(Direction.Left);
            Assert.AreEqual(SessionStatus.Won, session.Status);
            Assert.AreEqual(Direction.Left, level.Rules.SwipeFor(Solver.Hint(layout, state, level.Rules).Value));
        }

        [Test]
        public void ConveyorPasses_RunConveyorsTwice()
        {
            var (layout, state) = TestBoards.Parse("}}.R");
            state = new BoardState(new[] { new GridPos(0, 0) }, new[] { BlockColor.Red });
            BoardState once = SwipeResolver.Resolve(layout, state, Direction.Up, 1).After;
            BoardState twice = SwipeResolver.Resolve(layout, state, Direction.Up, 2).After;
            Assert.AreEqual(new GridPos(1, 0), once.PositionOf(0));
            Assert.AreEqual(new GridPos(2, 0), twice.PositionOf(0));
        }

        [Test]
        public void Daily_HasNoMoveLimit()
        {
            var (layout, state) = TestBoards.Parse("r..", "...", "..R");
            var session = new LevelSession(new LevelDefinition("d001", 2, layout, state, 2, 99, LevelKind.Daily));
            for (int i = 0; i < 200; i++) session.Swipe(i % 2 == 0 ? Direction.Down : Direction.Up);
            Assert.AreEqual(SessionStatus.Playing, session.Status);
        }

        // --- Parser and writer for everything ---

        private const string Full = @"{
  ""id"": ""w10_05"", ""world"": 10, ""w"": 5, ""h"": 5,
  ""cells"": ["".s1.R"",
            ""b..1."",
            ""}.#LB"",
            "".^.O."",
            ""....."" ],
  ""blocks"": [
    { ""x"": 0, ""y"": 0, ""c"": ""r"", ""link"": 3 },
    { ""x"": 0, ""y"": 1, ""c"": ""b"", ""link"": 3 },
    { ""x"": 4, ""y"": 4, ""key"": true },
    { ""x"": 2, ""y"": 4, ""c"": ""r"", ""ice"": true }
  ],
  ""rules"": { ""forbid"": ""up"", ""conveyorPasses"": 2 },
  ""par"": 6, ""limit"": 9, ""kind"": ""hard""
}";

        [Test]
        public void ParsesAndRoundTripsEveryFeature()
        {
            LevelDefinition l = LevelParser.Parse(Full);
            Assert.AreEqual(CellKind.Sticky, l.Layout[1, 0].Kind);
            Assert.AreEqual(CellKind.Portal, l.Layout[2, 0].Kind);
            Assert.AreEqual(new GridPos(3, 1), l.Layout.PartnerOf(new GridPos(2, 0)));
            Assert.AreEqual(CellKind.Paint, l.Layout[0, 1].Kind);
            Assert.AreEqual(BlockColor.Blue, l.Layout[0, 1].Color);
            Assert.AreEqual(CellKind.Conveyor, l.Layout[0, 2].Kind);
            Assert.AreEqual(Direction.Right, l.Layout[0, 2].Dir);
            Assert.AreEqual(CellKind.Lock, l.Layout[3, 2].Kind);
            Assert.AreEqual(CellKind.Keyhole, l.Layout[3, 3].Kind);
            Assert.AreEqual(1, l.Start.PartnerOf(0));
            Assert.IsTrue(l.Start.IsKey(2));
            Assert.IsTrue(l.Start.IsIced(3));
            Assert.AreEqual(Direction.Up, l.Rules.Forbidden);
            Assert.AreEqual(2, l.Rules.ConveyorPasses);

            LevelDefinition back = LevelParser.Parse(LevelWriter.ToJson(l));
            Assert.AreEqual(LevelWriter.ToJson(l), LevelWriter.ToJson(back));
            Assert.AreEqual(LevelGenerator.KeyOf(l.Layout, l.Start), LevelGenerator.KeyOf(back.Layout, back.Start));
        }

        private static void Rejects(string find, string replace, string fragment)
        {
            StringAssert.Contains(find, Full, "test setup");
            var e = Assert.Throws<LevelFormatException>(() => LevelParser.Parse(Full.Replace(find, replace)));
            StringAssert.Contains(fragment, e.Message);
        }

        [Test] public void RejectsUnpairedPortal() => Rejects(@""".s1.R""", @""".s..R""", "portals come in pairs");
        [Test] public void RejectsLinkThatIsNotSideBySide() => Rejects(@"""x"": 0, ""y"": 1, ""c"": ""b""", @"""x"": 1, ""y"": 3, ""c"": ""b""", "not side by side");
        [Test] public void RejectsLinkWithOneBlock() => Rejects(@"""c"": ""b"", ""link"": 3", @"""c"": ""b""", "joins 1 blocks");
        [Test] public void RejectsLocksWithoutKeyhole() => Rejects(@""".^.O.""", @""".^...""", "no keyhole");
        [Test] public void RejectsKeyholeWithoutKey() => Rejects(@"{ ""x"": 4, ""y"": 4, ""key"": true },", "", "no key block");
        [Test] public void RejectsBlockOnPortal() => Rejects(@"""x"": 2, ""y"": 4, ""c"": ""r""", @"""x"": 2, ""y"": 0, ""c"": ""r""", "portal cell");
        [Test] public void RejectsBadRule() => Rejects(@"""forbid"": ""up""", @"""forbid"": ""sideways""", "rules.forbid");
        [Test] public void RejectsKeyWithColour() => Rejects(@"""key"": true", @"""key"": true, ""c"": ""r""", "no colour");
    }
}
