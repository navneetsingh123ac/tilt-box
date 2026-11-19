using System;
using System.Text;

namespace TiltBox.Core
{
    /// <summary>
    /// The moving part of a level: where each block is, which blocks are already in a bin, and which are iced.
    /// Immutable. Block indices are stable for the whole level so views can track one block across swipes.
    /// </summary>
    public sealed class BoardState
    {
        private readonly GridPos[] _positions;
        private readonly BlockColor[] _colors;
        private readonly bool[] _binned;
        private readonly bool[] _iced;
        private string _key;

        public BoardState(GridPos[] positions, BlockColor[] colors, bool[] binned = null, bool[] iced = null)
        {
            if (positions == null) throw new ArgumentNullException(nameof(positions));
            if (colors == null) throw new ArgumentNullException(nameof(colors));
            if (positions.Length != colors.Length) throw new ArgumentException("positions and colors differ in length.");
            if (binned != null && binned.Length != positions.Length) throw new ArgumentException("binned has the wrong length.");
            if (iced != null && iced.Length != positions.Length) throw new ArgumentException("iced has the wrong length.");
            _positions = (GridPos[])positions.Clone();
            _colors = (BlockColor[])colors.Clone();
            _binned = binned == null ? new bool[positions.Length] : (bool[])binned.Clone();
            _iced = iced == null ? new bool[positions.Length] : (bool[])iced.Clone();
        }

        public int BlockCount => _positions.Length;

        public GridPos PositionOf(int block) => _positions[block];
        public BlockColor ColorOf(int block) => _colors[block];
        public bool IsBinned(int block) => _binned[block];

        /// <summary>World 2 ice: an iced block skips one swipe (it cracks instead) and blocks others meanwhile.</summary>
        public bool IsIced(int block) => _iced[block];

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

        /// <summary>Win condition: every coloured block is in a bin.</summary>
        public bool IsSolved => ActiveCount == 0;

        /// <summary>Index of the active block at <paramref name="p"/>, or -1.</summary>
        public int BlockAt(GridPos p)
        {
            for (int i = 0; i < _positions.Length; i++)
                if (!_binned[i] && _positions[i] == p) return i;
            return -1;
        }

        internal BoardState With(GridPos[] positions, bool[] binned, bool[] iced) => new BoardState(positions, _colors, binned, iced);

        internal GridPos[] CopyPositions() => (GridPos[])_positions.Clone();
        internal bool[] CopyBinned() => (bool[])_binned.Clone();
        internal bool[] CopyIced() => (bool[])_iced.Clone();

        /// <summary>A copy of this state with no ice, used to test whether ice matters in a level.</summary>
        public BoardState WithoutIce() => new BoardState(_positions, _colors, _binned, null);

        /// <summary>
        /// A string that is equal for two states the player cannot tell apart: same-colour blocks are
        /// interchangeable and binned blocks are gone. Used by the solver's visited set.
        /// </summary>
        public string CanonicalKey
        {
            get
            {
                if (_key != null) return _key;
                // One char per active block packing ice (bit 15), colour (bits 12-14), y and x (6 bits each).
                // Keys are only compared between states of the same layout.
                var codes = new char[ActiveCount];
                int n = 0;
                for (int i = 0; i < _positions.Length; i++)
                {
                    if (_binned[i]) continue;
                    codes[n++] = (char)((_iced[i] ? 32768 : 0) + (int)_colors[i] * 4096 + _positions[i].Y * 64 + _positions[i].X);
                }
                Array.Sort(codes);
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
                sb.Append(_binned[i] ? "[binned]" : _positions[i].ToString());
            }
            return sb.ToString();
        }
    }
}
