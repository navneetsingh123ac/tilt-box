using System;

namespace TiltBox.Core
{
    /// <summary>
    /// What a cell is. Only World 1 kinds exist so far; later worlds (ice, gates, sticky pads, portals,
    /// paint, locks, conveyors) add kinds here and rules in <see cref="SwipeResolver"/>.
    /// </summary>
    public enum CellKind
    {
        Floor = 0,
        Wall = 1,
        Bin = 2,
    }

    public readonly struct Cell
    {
        public readonly CellKind Kind;

        /// <summary>Only meaningful when <see cref="Kind"/> is <see cref="CellKind.Bin"/>.</summary>
        public readonly BlockColor Color;

        public Cell(CellKind kind, BlockColor color = BlockColor.Red)
        {
            Kind = kind;
            Color = color;
        }

        public static readonly Cell Floor = new Cell(CellKind.Floor);
        public static readonly Cell Wall = new Cell(CellKind.Wall);
        public static Cell Bin(BlockColor color) => new Cell(CellKind.Bin, color);
    }

    /// <summary>The static part of a level: size and cells. Immutable; shared by every state of the level.</summary>
    public sealed class BoardLayout
    {
        private readonly Cell[] _cells;

        public int Width { get; }
        public int Height { get; }

        public BoardLayout(int width, int height, Cell[] cells)
        {
            if (width <= 0 || height <= 0) throw new ArgumentException("Board size must be positive.");
            if (cells == null || cells.Length != width * height)
                throw new ArgumentException("Expected " + (width * height) + " cells.", nameof(cells));
            Width = width;
            Height = height;
            _cells = (Cell[])cells.Clone();
        }

        public int CellCount => _cells.Length;

        public bool InBounds(GridPos p) => p.X >= 0 && p.Y >= 0 && p.X < Width && p.Y < Height;

        public int IndexOf(GridPos p) => p.Y * Width + p.X;

        public Cell this[GridPos p] => _cells[IndexOf(p)];

        public Cell this[int x, int y] => _cells[y * Width + x];

        public bool HasBin(BlockColor color)
        {
            for (int i = 0; i < _cells.Length; i++)
                if (_cells[i].Kind == CellKind.Bin && _cells[i].Color == color) return true;
            return false;
        }
    }
}
