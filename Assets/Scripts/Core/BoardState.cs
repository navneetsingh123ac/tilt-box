using System;
using System.Text;

namespace TiltBox.Core
{
    /// <summary>
    /// The moving part of a level: block positions and colours (paint can change them), which blocks are binned,
    /// which are iced, which are linked, and whether the lock walls are open. Immutable. Block indices are
    /// stable for the whole level so views can track one block across swipes.
    /// </summary>
    public sealed class BoardState
    {
        private readonly GridPos[] _positions;
        private readonly BlockColor[] _colors;
        private readonly bool[] _binned;
        private readonly bool[] _iced;
        private readonly int[] _links;
        private string _key;

        public BoardState(GridPos[] positions, BlockColor[] colors, bool[] binned = null, bool[] iced = null,
            int[] links = null, bool locksOpen = false)
        {
            if (positions == null) throw new ArgumentNullException(nameof(positions));
            if (colors == null) throw new ArgumentNullException(nameof(colors));
            int n = positions.Length;
            if (colors.Length != n) throw new ArgumentException("positions and colors differ in length.");
            if (binned != null && binned.Length != n) throw new ArgumentException("binned has the wrong length.");
            if (iced != null && iced.Length != n) throw new ArgumentException("iced has the wrong length.");
            if (links != null && links.Length != n) throw new ArgumentException("links has the wrong length.");
            _positions = (GridPos[])positions.Clone();
            _colors = (BlockColor[])colors.Clone();
            _binned = binned == null ? new bool[n] : (bool[])binned.Clone();
            _iced = iced == null ? new bool[n] : (bool[])iced.Clone();
            _links = links == null ? new int[n] : (int[])links.Clone();
            LocksOpen = locksOpen;
        }

        public int BlockCount => _positions.Length;

        public GridPos PositionOf(int block) => _positions[block];
        public BlockColor ColorOf(int block) => _colors[block];
        public bool IsBinned(int block) => _binned[block];
        public bool IsKey(int block) => _colors[block] == BlockColor.Key;

        /// <summary>World 2 ice: an iced block skips one swipe (it cracks instead) and blocks others meanwhile.</summary>
        public bool IsIced(int block) => _iced[block];

        /// <summary>World 9 link id (0 = not linked). The link breaks once either block is binned.</summary>
        public int LinkOf(int block) => _links[block];

        /// <summary>The block's linked partner while both are still on the board, or -1.</summary>
        public int PartnerOf(int block)
        {
            int id = _links[block];
            if (id == 0 || _binned[block]) return -1;
            for (int i = 0; i < _links.Length; i++)
                if (i != block && _links[i] == id && !_binned[i]) return i;
            return -1;
        }

        /// <summary>World 7: true once any keyhole has received a key; lock walls are then open floor.</summary>
        public bool LocksOpen { get; }

        public bool AnyIced
        {
            get
            {
                for (int i = 0; i < _iced.Length; i++) if (_iced[i] && !_binned[i]) return true;
                return false;
            }
        }

        public int ActiveCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _binned.Length; i++) if (!_binned[i]) n++;
                return n;
            }
        }

        /// <summary>Win condition: every coloured block is in a bin (keys don't count, §3 step 8).</summary>
        public bool IsSolved
        {
            get
            {
                for (int i = 0; i < _binned.Length; i++)
                    if (!_binned[i] && _colors[i] != BlockColor.Key) return false;
                return true;
            }
        }

        /// <summary>Index of the active block at <paramref name="p"/>, or -1.</summary>
        public int BlockAt(GridPos p)
        {
            for (int i = 0; i < _positions.Length; i++)
                if (!_binned[i] && _positions[i] == p) return i;
            return -1;
        }

        internal BoardState With(GridPos[] positions, BlockColor[] colors, bool[] binned, bool[] iced, bool locksOpen) =>
            new BoardState(positions, colors, binned, iced, _links, locksOpen);

        internal GridPos[] CopyPositions() => (GridPos[])_positions.Clone();
        internal BlockColor[] CopyColors() => (BlockColor[])_colors.Clone();
        internal bool[] CopyBinned() => (bool[])_binned.Clone();
        internal bool[] CopyIced() => (bool[])_iced.Clone();

        /// <summary>A copy of this state with no ice, used to test whether ice matters in a level.</summary>
        public BoardState WithoutIce() => new BoardState(_positions, _colors, _binned, null, _links, LocksOpen);

        /// <summary>A copy with no links, used to test whether links matter in a level.</summary>
        public BoardState WithoutLinks() => new BoardState(_positions, _colors, _binned, _iced, null, LocksOpen);

        /// <summary>
        /// A string that is equal for two states the player cannot tell apart: same-colour, same-kind blocks are
        /// interchangeable and binned blocks are gone. Used by the solver's visited set.
        /// </summary>
        public string CanonicalKey
        {
            get
            {
                if (_key != null) return _key;
                // One char per active block: x (3 bits) | y (3 bits) | colour (3) | ice (1) | live link id (4).
                // Boards are at most 7x8, so x and y fit in 3 bits. Keys are only compared within one layout.
                var codes = new char[ActiveCount + 1];
                int n = 0;
                for (int i = 0; i < _positions.Length; i++)
                {
                    if (_binned[i]) continue;
                    int link = PartnerOf(i) >= 0 ? _links[i] & 15 : 0;
                    codes[n++] = (char)(_positions[i].X | (_positions[i].Y << 3) | ((int)_colors[i] << 6) |
                                        ((_iced[i] ? 1 : 0) << 9) | (link << 10));
                }
                Array.Sort(codes, 0, n);
                codes[n] = LocksOpen ? 'O' : 'L';
                _key = new string(codes);
                return _key;
            }
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < _positions.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(_colors[i].ToLetter());
                if (_iced[i]) sb.Append('*');
                if (_links[i] != 0) sb.Append('~').Append(_links[i]);
                sb.Append(_binned[i] ? "[binned]" : _positions[i].ToString());
            }
            if (LocksOpen) sb.Append(" locks-open");
            return sb.ToString();
        }
    }
}
