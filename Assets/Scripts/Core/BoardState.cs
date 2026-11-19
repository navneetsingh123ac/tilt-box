using System;
using System.Text;

namespace TiltBox.Core
{
    /// <summary>
    /// The moving part of a level: where each block is, and which blocks are already in a bin.
    /// Immutable. Block indices are stable for the whole level so views can track one block across swipes.
    /// </summary>
    public sealed class BoardState
    {
        private readonly GridPos[] _positions;
        private readonly BlockColor[] _colors;
        private readonly bool[] _binned;
        private string _key;

        public BoardState(GridPos[] positions, BlockColor[] colors, bool[] binned = null)
        {
            if (positions == null) throw new ArgumentNullException(nameof(positions));
            if (colors == null) throw new ArgumentNullException(nameof(colors));
            if (positions.Length != colors.Length) throw new ArgumentException("positions and colors differ in length.");
            if (binned != null && binned.Length != positions.Length) throw new ArgumentException("binned has the wrong length.");
            _positions = (GridPos[])positions.Clone();
            _colors = (BlockColor[])colors.Clone();
            _binned = binned == null ? new bool[positions.Length] : (bool[])binned.Clone();
        }

        public int BlockCount => _positions.Length;

        public GridPos PositionOf(int block) => _positions[block];
        public BlockColor ColorOf(int block) => _colors[block];
        public bool IsBinned(int block) => _binned[block];

        public int ActiveCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _binned.Length; i++) if (!_binned[i]) n++;
                return n;
            }
        }

        /// <summary>Win condition for World 1: every coloured block is in a bin.</summary>
        public bool IsSolved => ActiveCount == 0;

        /// <summary>Index of the active block at <paramref name="p"/>, or -1.</summary>
        public int BlockAt(GridPos p)
        {
            for (int i = 0; i < _positions.Length; i++)
                if (!_binned[i] && _positions[i] == p) return i;
            return -1;
        }

        internal BoardState With(GridPos[] positions, bool[] binned) => new BoardState(positions, _colors, binned);

        internal GridPos[] CopyPositions() => (GridPos[])_positions.Clone();
        internal bool[] CopyBinned() => (bool[])_binned.Clone();

        /// <summary>
        /// A string that is equal for two states the player cannot tell apart: same-colour blocks are
        /// interchangeable and binned blocks are gone. Used by the solver's visited set.
        /// </summary>
        public string CanonicalKey
        {
            get
            {
                if (_key != null) return _key;
                // One char per active block, packing colour, y and x (each < 64). Keys are only compared
                // between states of the same layout.
                var codes = new char[ActiveCount];
                int n = 0;
                for (int i = 0; i < _positions.Length; i++)
                {
                    if (_binned[i]) continue;
                    codes[n++] = (char)((int)_colors[i] * 4096 + _positions[i].Y * 64 + _positions[i].X);
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
                sb.Append(_binned[i] ? "[binned]" : _positions[i].ToString());
            }
            return sb.ToString();
        }
    }
}
