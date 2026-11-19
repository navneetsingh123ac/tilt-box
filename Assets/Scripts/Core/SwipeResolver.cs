using System;
using System.Collections.Generic;

namespace TiltBox.Core
{
    /// <summary>A straight slide from one cell to another. A portal jump starts a new segment.</summary>
    public readonly struct Segment
    {
        public readonly GridPos From;
        public readonly GridPos To;

        public Segment(GridPos from, GridPos to)
        {
            From = from;
            To = to;
        }

        public int Length => Math.Abs(To.X - From.X) + Math.Abs(To.Y - From.Y);
    }

    /// <summary>What happened to one block during a swipe (or a conveyor step). Views animate from these.</summary>
    public sealed class BlockMove
    {
        public int Block { get; }
        public GridPos From { get; }

        /// <summary>Final cell. For a binned block this is the bin or keyhole it dropped into.</summary>
        public GridPos To { get; }

        public bool Binned { get; }

        /// <summary>The slides in order; more than one means the block went through a portal.</summary>
        public IReadOnlyList<Segment> Segments { get; }

        public BlockMove(int block, GridPos from, GridPos to, bool binned, IReadOnlyList<Segment> segments = null)
        {
            Block = block;
            From = from;
            To = to;
            Binned = binned;
            Segments = segments ?? new[] { new Segment(from, to) };
        }

        /// <summary>Cells travelled, counting each segment (portal jumps are free).</summary>
        public int Distance
        {
            get
            {
                int d = 0;
                for (int i = 0; i < Segments.Count; i++) d += Segments[i].Length;
                return d;
            }
        }

        public bool Teleported => Segments.Count > 1;
    }

    public sealed class SwipeResult
    {
        public Direction Direction { get; }
        public BoardState Before { get; }
        public BoardState After { get; }

        /// <summary>Slides of this swipe (blocks that moved or were binned).</summary>
        public IReadOnlyList<BlockMove> Moves { get; }

        /// <summary>Blocks whose ice cracked this swipe (they did not move).</summary>
        public IReadOnlyList<int> Cracked { get; }

        /// <summary>One-cell conveyor steps after the slides (§3 step 5), in order.</summary>
        public IReadOnlyList<BlockMove> ConveyorMoves { get; }

        /// <summary>True when a key reached a keyhole this swipe and the lock walls opened (§3 step 6).</summary>
        public bool LocksOpened { get; }

        /// <summary>
        /// False when nothing moved, nothing was binned and no ice cracked: the swipe is ignored and must not
        /// cost a move (GDD §2, §3 step 7).
        /// </summary>
        public bool Changed => Moves.Count > 0 || Cracked.Count > 0 || ConveyorMoves.Count > 0;

        public int BinnedCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Moves.Count; i++) if (Moves[i].Binned) n++;
                for (int i = 0; i < ConveyorMoves.Count; i++) if (ConveyorMoves[i].Binned) n++;
                return n;
            }
        }

        public SwipeResult(Direction direction, BoardState before, BoardState after, IReadOnlyList<BlockMove> moves,
            IReadOnlyList<int> cracked = null, IReadOnlyList<BlockMove> conveyorMoves = null, bool locksOpened = false)
        {
            Direction = direction;
            Before = before;
            After = after;
            Moves = moves;
            Cracked = cracked ?? Array.Empty<int>();
            ConveyorMoves = conveyorMoves ?? Array.Empty<BlockMove>();
            LocksOpened = locksOpened;
        }
    }

    /// <summary>
    /// GDD §3. Pure function: (layout, state, direction) -> result. Shared by the game, the solver and the tests.
    ///
    /// 1. Iced blocks (and any linked pair containing one) don't move; they act as obstacles and crack.
    /// 2. Units (a block, or a linked pair) are resolved front-first along the swipe. Because blocks never
    ///    merge or push, moving the front unit first and the next ones up to it equals everything moving at once.
    /// 3. A block stops at the edge, a wall, a closed lock, another block, a gate pointing another way, a bin of
    ///    another colour, or a keyhole (unless it is a key). Its own bin swallows it; a key fills a keyhole.
    ///    A portal sends it to the partner portal (if free) and it keeps sliding; paint recolours it and it keeps
    ///    sliding; a sticky pad stops it. A linked pair stops when either block would stop; once one of the pair
    ///    is binned the other slides on alone.
    /// 4. Cracked blocks lose their ice. 5. Conveyors move resting blocks one cell. 6. A filled keyhole opens
    ///    every lock. 7. If nothing changed the swipe is ignored.
    ///
    /// Choices where the GDD is silent (tests pin them down):
    /// - A linked pair treats portals as walls, and conveyors don't move linked blocks (the bar holds them).
    /// - Conveyors push into floor-like cells and matching bins/keyholes, never into portals.
    /// - A block that would enter the same portal twice in one slide stops instead (no endless loops).
    /// - Paint does not recolour keys.
    /// </summary>
    public static class SwipeResolver
    {
        private enum StepKind { Blocked, Move, Bin, Sticky, Teleport }

        public static SwipeResult Resolve(BoardLayout layout, BoardState state, Direction dir, int conveyorPasses = 1)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            if (state == null) throw new ArgumentNullException(nameof(state));

            int count = state.BlockCount;
            var pos = state.CopyPositions();
            var col = state.CopyColors();
            var binned = state.CopyBinned();
            var iced = state.CopyIced();
            bool locksOpen = state.LocksOpen;
            bool keyUsed = false;

            var occupant = new int[layout.CellCount];
            for (int i = 0; i < occupant.Length; i++) occupant[i] = -1;
            for (int i = 0; i < count; i++)
                if (!binned[i]) occupant[layout.IndexOf(pos[i])] = i;

            // Units: single blocks or live linked pairs. A unit with any iced member stays put and cracks.
            var units = new List<int[]>();
            var seen = new bool[count];
            List<int> cracked = null;
            for (int i = 0; i < count; i++)
            {
                if (binned[i] || seen[i]) continue;
                int partner = state.PartnerOf(i);
                int[] unit = partner >= 0 ? new[] { i, partner } : new[] { i };
                bool frozen = false;
                foreach (int m in unit)
                {
                    seen[m] = true;
                    frozen |= iced[m];
                }
                if (!frozen) { units.Add(unit); continue; }
                if (cracked == null) cracked = new List<int>();
                foreach (int m in unit) if (iced[m]) cracked.Add(m);
            }

            GridPos off = dir.Offset();
            units.Sort((a, b) => Lead(b, pos, off).CompareTo(Lead(a, pos, off)));

            List<BlockMove> moves = null;
            foreach (int[] unit in units)
            {
                int n = unit.Length;
                foreach (int m in unit) occupant[layout.IndexOf(pos[m])] = -1;

                var start = new GridPos[n];
                var segStart = new GridPos[n];
                var segments = new List<Segment>[n];
                var portalsUsed = new List<int>[n];
                var live = new bool[n];
                var kind = new StepKind[n];
                var target = new GridPos[n];
                for (int k = 0; k < n; k++)
                {
                    start[k] = segStart[k] = pos[unit[k]];
                    live[k] = true;
                }

                while (true)
                {
                    int liveCount = 0;
                    for (int k = 0; k < n; k++) if (live[k]) liveCount++;
                    if (liveCount == 0) break;
                    bool together = liveCount == 2;

                    // Decide every live member's step first; a linked pair moves only if both can.
                    bool blocked = false;
                    for (int k = 0; k < n; k++)
                    {
                        if (!live[k]) continue;
                        kind[k] = Step(layout, occupant, pos[unit[k]], off, dir, col[unit[k]], locksOpen, together,
                            portalsUsed[k], out target[k]);
                        blocked |= kind[k] == StepKind.Blocked;
                    }
                    if (blocked) break;

                    bool sticky = false;
                    for (int k = 0; k < n; k++)
                    {
                        if (!live[k]) continue;
                        int b = unit[k];
                        switch (kind[k])
                        {
                            case StepKind.Teleport:
                                // Close the slide at the entry portal, jump, then continue from the exit portal.
                                GridPos entry = pos[b] + off;
                                (segments[k] ?? (segments[k] = new List<Segment>())).Add(new Segment(segStart[k], entry));
                                (portalsUsed[k] ?? (portalsUsed[k] = new List<int>())).Add(layout.IndexOf(entry));
                                segStart[k] = target[k];
                                break;
                            case StepKind.Bin:
                                binned[b] = true;
                                live[k] = false;
                                if (col[b] == BlockColor.Key) keyUsed = true;
                                break;
                            case StepKind.Sticky:
                                sticky = true;
                                break;
                        }
                        pos[b] = target[k];
                        Cell c = layout[pos[b]];
                        if (c.Kind == CellKind.Paint && col[b] != BlockColor.Key) col[b] = c.Color;
                    }
                    if (sticky) break; // a sticky pad stops the block, and a linked pair with it
                }

                for (int k = 0; k < n; k++)
                {
                    int b = unit[k];
                    if (!binned[b]) occupant[layout.IndexOf(pos[b])] = b;
                    if (pos[b] == start[k] && !binned[b] && segments[k] == null) continue;
                    var segs = segments[k] ?? new List<Segment>();
                    segs.Add(new Segment(segStart[k], pos[b]));
                    if (moves == null) moves = new List<BlockMove>();
                    moves.Add(new BlockMove(b, start[k], pos[b], binned[b], segs));
                }
            }

            if (cracked != null)
                foreach (int i in cracked) iced[i] = false;

            List<BlockMove> conveyorMoves = null;
            for (int pass = 0; pass < conveyorPasses; pass++)
                RunConveyors(layout, state, occupant, pos, col, binned, locksOpen, ref conveyorMoves, ref keyUsed);

            bool opened = keyUsed && !locksOpen;
            if (keyUsed) locksOpen = true;

            if (moves == null && cracked == null && conveyorMoves == null)
                return new SwipeResult(dir, state, state, Array.Empty<BlockMove>());
            return new SwipeResult(dir, state, state.With(pos, col, binned, iced, locksOpen),
                (IReadOnlyList<BlockMove>)moves ?? Array.Empty<BlockMove>(), cracked, conveyorMoves, opened);
        }

        /// <summary>One step of one block from <paramref name="at"/> in the swipe direction.</summary>
        private static StepKind Step(BoardLayout layout, int[] occupant, GridPos at, GridPos off, Direction dir,
            BlockColor color, bool locksOpen, bool linked, List<int> portalsUsed, out GridPos to)
        {
            to = at;
            GridPos next = at + off;
            if (!layout.InBounds(next)) return StepKind.Blocked;
            if (occupant[layout.IndexOf(next)] >= 0) return StepKind.Blocked;
            Cell cell = layout[next];
            switch (cell.Kind)
            {
                case CellKind.Wall:
                    return StepKind.Blocked;
                case CellKind.Lock:
                    if (!locksOpen) return StepKind.Blocked;
                    break;
                case CellKind.Gate:
                    if (cell.Dir != dir) return StepKind.Blocked;
                    break;
                case CellKind.Bin:
                    if (cell.Color != color) return StepKind.Blocked;
                    to = next;
                    return StepKind.Bin;
                case CellKind.Keyhole:
                    if (color != BlockColor.Key) return StepKind.Blocked;
                    to = next;
                    return StepKind.Bin;
                case CellKind.Portal:
                {
                    if (linked) return StepKind.Blocked;
                    if (portalsUsed != null && portalsUsed.Contains(layout.IndexOf(next))) return StepKind.Blocked;
                    GridPos? exit = layout.PartnerOf(next);
                    if (exit == null || occupant[layout.IndexOf(exit.Value)] >= 0) return StepKind.Blocked;
                    to = exit.Value;
                    return StepKind.Teleport;
                }
                case CellKind.Sticky:
                    to = next;
                    return StepKind.Sticky;
            }
            to = next;
            return StepKind.Move;
        }

        /// <summary>§3 step 5: each block resting on a conveyor moves one cell its way if free. Front-first, no chaining.</summary>
        private static void RunConveyors(BoardLayout layout, BoardState state, int[] occupant, GridPos[] pos, BlockColor[] col,
            bool[] binned, bool locksOpen, ref List<BlockMove> moves, ref bool keyUsed)
        {
            var riders = new List<int>();
            for (int i = 0; i < pos.Length; i++)
                if (!binned[i] && layout[pos[i]].Kind == CellKind.Conveyor && LiveLinkPartner(state, binned, i) < 0) riders.Add(i);
            if (riders.Count == 0) return;
            riders.Sort((a, b) =>
            {
                GridPos da = layout[pos[a]].Dir.Offset(), db = layout[pos[b]].Dir.Offset();
                return (pos[b].X * db.X + pos[b].Y * db.Y).CompareTo(pos[a].X * da.X + pos[a].Y * da.Y);
            });

            foreach (int b in riders)
            {
                Direction d = layout[pos[b]].Dir;
                GridPos from = pos[b];
                GridPos next = from + d.Offset();
                if (!layout.InBounds(next) || occupant[layout.IndexOf(next)] >= 0) continue;
                Cell c = layout[next];
                bool drop = false;
                switch (c.Kind)
                {
                    case CellKind.Wall:
                    case CellKind.Portal:
                        continue;
                    case CellKind.Lock:
                        if (!locksOpen) continue;
                        break;
                    case CellKind.Gate:
                        if (c.Dir != d) continue;
                        break;
                    case CellKind.Bin:
                        if (c.Color != col[b]) continue;
                        drop = true;
                        break;
                    case CellKind.Keyhole:
                        if (col[b] != BlockColor.Key) continue;
                        drop = true;
                        keyUsed = true;
                        break;
                    case CellKind.Paint:
                        if (col[b] != BlockColor.Key) col[b] = c.Color;
                        break;
                }
                occupant[layout.IndexOf(from)] = -1;
                pos[b] = next;
                if (drop) binned[b] = true;
                else occupant[layout.IndexOf(next)] = b;
                if (moves == null) moves = new List<BlockMove>();
                moves.Add(new BlockMove(b, from, next, drop));
            }
        }

        private static int LiveLinkPartner(BoardState state, bool[] binned, int block)
        {
            int id = state.LinkOf(block);
            if (id == 0) return -1;
            for (int i = 0; i < binned.Length; i++)
                if (i != block && !binned[i] && state.LinkOf(i) == id) return i;
            return -1;
        }

        /// <summary>Front-most projection of a unit onto the swipe direction.</summary>
        private static int Lead(int[] unit, GridPos[] pos, GridPos off)
        {
            int best = int.MinValue;
            foreach (int m in unit) best = Math.Max(best, pos[m].X * off.X + pos[m].Y * off.Y);
            return best;
        }
    }
}
