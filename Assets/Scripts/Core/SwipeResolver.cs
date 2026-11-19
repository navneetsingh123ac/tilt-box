using System;
using System.Collections.Generic;

namespace TiltBox.Core
{
    /// <summary>What happened to one block during a swipe. Views animate from these.</summary>
    public readonly struct BlockMove
    {
        public readonly int Block;
        public readonly GridPos From;

        /// <summary>Final cell. For a binned block this is the bin cell it dropped into.</summary>
        public readonly GridPos To;

        public readonly bool Binned;

        public BlockMove(int block, GridPos from, GridPos to, bool binned)
        {
            Block = block;
            From = from;
            To = to;
            Binned = binned;
        }

        /// <summary>Number of cells travelled (including the step into a bin).</summary>
        public int Distance => Math.Abs(To.X - From.X) + Math.Abs(To.Y - From.Y);
    }

    public sealed class SwipeResult
    {
        public Direction Direction { get; }
        public BoardState Before { get; }
        public BoardState After { get; }

        /// <summary>Only blocks that moved or were binned. Empty when the swipe was ignored.</summary>
        public IReadOnlyList<BlockMove> Moves { get; }

        /// <summary>Blocks whose ice cracked this swipe (they did not move).</summary>
        public IReadOnlyList<int> Cracked { get; }

        /// <summary>
        /// False when nothing moved, nothing was binned and no ice cracked: the swipe is ignored and must not
        /// cost a move (GDD §2, §3 step 7).
        /// </summary>
        public bool Changed => Moves.Count > 0 || Cracked.Count > 0;

        public int BinnedCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Moves.Count; i++) if (Moves[i].Binned) n++;
                return n;
            }
        }

        public SwipeResult(Direction direction, BoardState before, BoardState after, IReadOnlyList<BlockMove> moves,
            IReadOnlyList<int> cracked = null)
        {
            Direction = direction;
            Before = before;
            After = after;
            Moves = moves;
            Cracked = cracked ?? Array.Empty<int>();
        }
    }

    /// <summary>
    /// GDD §3. Pure function: (layout, state, direction) -> result. Shared by the game, the solver and the tests.
    ///
    /// 1. Iced blocks don't move this swipe; they act as obstacles and crack (lose their ice) at the end.
    /// 2. Other blocks are resolved front-first along the swipe direction. Because blocks never merge or push,
    ///    sliding the front block first and then each following block up to it gives the same result as
    ///    everything moving at once.
    /// 3. A block stops at the board edge, a wall, another block, a gate pointing another way, or a bin of
    ///    another colour. A bin of its own colour swallows it.
    /// </summary>
    public static class SwipeResolver
    {
        public static SwipeResult Resolve(BoardLayout layout, BoardState state, Direction dir)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            if (state == null) throw new ArgumentNullException(nameof(state));

            int count = state.BlockCount;
            var positions = state.CopyPositions();
            var binned = state.CopyBinned();
            var iced = state.CopyIced();

            // Occupancy of active blocks, by cell index. -1 = empty. Iced blocks occupy but don't move.
            var occupant = new int[layout.CellCount];
            for (int i = 0; i < occupant.Length; i++) occupant[i] = -1;
            var order = new List<int>(count);
            List<int> cracked = null;
            for (int i = 0; i < count; i++)
            {
                if (binned[i]) continue;
                occupant[layout.IndexOf(positions[i])] = i;
                if (iced[i])
                {
                    if (cracked == null) cracked = new List<int>();
                    cracked.Add(i);
                }
                else order.Add(i);
            }

            // Front-first: larger projection onto the direction goes first. Ties are blocks in different
            // lanes, which cannot affect each other, so their relative order does not matter.
            GridPos off = dir.Offset();
            order.Sort((a, b) => Project(positions[b], off).CompareTo(Project(positions[a], off)));

            List<BlockMove> moves = null;
            foreach (int block in order)
            {
                GridPos from = positions[block];
                GridPos pos = from;
                bool dropped = false;
                occupant[layout.IndexOf(pos)] = -1;

                while (true)
                {
                    GridPos next = pos + off;
                    if (!layout.InBounds(next)) break;
                    Cell cell = layout[next];
                    if (cell.Kind == CellKind.Wall) break;
                    if (occupant[layout.IndexOf(next)] >= 0) break;
                    if (cell.Kind == CellKind.Gate && cell.Gate != dir) break; // one-way: wrong way is a wall
                    if (cell.Kind == CellKind.Bin)
                    {
                        if (cell.Color == state.ColorOf(block))
                        {
                            pos = next;
                            dropped = true;
                        }
                        break; // a bin of another colour acts as a wall
                    }
                    pos = next;
                }

                positions[block] = pos;
                if (dropped) binned[block] = true;
                else occupant[layout.IndexOf(pos)] = block;

                if (pos != from)
                {
                    if (moves == null) moves = new List<BlockMove>();
                    moves.Add(new BlockMove(block, from, pos, dropped));
                }
            }

            if (cracked != null)
                foreach (int i in cracked) iced[i] = false;

            if (moves == null && cracked == null)
                return new SwipeResult(dir, state, state, Array.Empty<BlockMove>());
            return new SwipeResult(dir, state, state.With(positions, binned, iced),
                (IReadOnlyList<BlockMove>)moves ?? Array.Empty<BlockMove>(), cracked);
        }

        private static int Project(GridPos p, GridPos dir) => p.X * dir.X + p.Y * dir.Y;
    }
}
