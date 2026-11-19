using System;

namespace TiltBox.Core
{
    /// <summary>
    /// What a cell is. Worlds 1-3 kinds so far; later worlds (sticky pads, portals, paint, locks, conveyors)
    /// add kinds here and rules in <see cref="SwipeResolver"/>.
    /// </summary>
    public enum CellKind
    {
        Floor = 0,
        Wall = 1,
        Bin = 2,

        /// <summary>One-way gate (World 3): blocks may enter it only moving in <see cref="Cell.Gate"/>.</summary>
        Gate = 3,
    }

    public readonly struct Cell
    {
        public readonly CellKind Kind;

        /// <summary>Only meaningful when <see cref="Kind"/> is <see cref="CellKind.Bin"/>.</summary>
        public readonly BlockColor Color;

        /// <summary>Only meaningful when <see cref="Kind"/> is <see cref="CellKind.Gate"/>: the allowed direction.</summary>
        public readonly Direction Gate;

        public Cell(CellKind kind, BlockColor color = BlockColor.Red, Direction gate = Direction.Up)
        {
            Kind = kind;
            Color = color;
            Gate = gate;
        }

        public static readonly Cell Floor = new Cell(CellKind.Floor);
        public static readonly Cell Wall = new Cell(CellKind.Wall);
        public static Cell Bin(BlockColor color) => new Cell(CellKind.Bin, color);
        public static Cell OneWay(Direction dir) => new Cell(CellKind.Gate, BlockColor.Red, dir);

        /// <summary>True for cells a block can stand on (floor and gates).</summary>
        public bool IsWalkable => Kind == CellKind.Floor || Kind == CellKind.Gate;

        /// <summary>The level-file character for this cell (GDD §5).</summary>
        public char ToChar()
        {
            switch (Kind)
            {
                case CellKind.Floor: return '.';
                case CellKind.Wall: return '#';
                case CellKind.Bin: return char.ToUpperInvariant(Color.ToLetter());
                case CellKind.Gate: return GateChars[(int)Gate];
                default: throw new InvalidOperationException("Unknown cell kind " + Kind);
            }
        }

        /// <summary>Gate characters indexed by <see cref="Direction"/>: Up ^, Down v, Left &lt;, Right &gt;.</summary>
        public const string GateChars = "^v<>";
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

        public bool HasKind(CellKind kind)
        {
            for (int i = 0; i < _cells.Length; i++) if (_cells[i].Kind == kind) return true;
            return false;
        }

        /// <summary>A copy with every cell passed through <paramref name="map"/> (e.g. gates turned to floor).</summary>
        public BoardLayout Map(Func<Cell, Cell> map)
        {
            var cells = new Cell[_cells.Length];
            for (int i = 0; i < cells.Length; i++) cells[i] = map(_cells[i]);
            return new BoardLayout(Width, Height, cells);
        }

        public bool HasBin(BlockColor color)
        {
            for (int i = 0; i < _cells.Length; i++)
                if (_cells[i].Kind == CellKind.Bin && _cells[i].Color == color) return true;
            return false;
        }
    }
}
