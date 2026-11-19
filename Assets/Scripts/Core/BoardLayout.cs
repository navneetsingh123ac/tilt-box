using System;

namespace TiltBox.Core
{
    /// <summary>What a cell is (GDD §4, §5). Rules for each kind live in <see cref="SwipeResolver"/>.</summary>
    public enum CellKind
    {
        Floor = 0,
        Wall = 1,
        Bin = 2,

        /// <summary>World 3: blocks may enter only moving in <see cref="Cell.Dir"/>.</summary>
        Gate = 3,

        /// <summary>World 4: a block that enters stops on it; the next swipe moves it off normally.</summary>
        Sticky = 4,

        /// <summary>World 5: entering one portal of a pair exits from the other and keeps sliding.</summary>
        Portal = 5,

        /// <summary>World 6: a block passing over it takes <see cref="Cell.Color"/> and keeps sliding.</summary>
        Paint = 6,

        /// <summary>World 7: a wall until any keyhole receives a key, then open floor for the rest of the level.</summary>
        Lock = 7,

        /// <summary>World 7: only a key block may enter (it is used up); everything else stops.</summary>
        Keyhole = 8,

        /// <summary>World 8: after each swipe, a block resting here moves one cell in <see cref="Cell.Dir"/>.</summary>
        Conveyor = 9,
    }

    public readonly struct Cell
    {
        public readonly CellKind Kind;

        /// <summary>Bin colour or paint colour.</summary>
        public readonly BlockColor Color;

        /// <summary>Gate or conveyor direction.</summary>
        public readonly Direction Dir;

        /// <summary>Portal pair number (1 or 2).</summary>
        public readonly int PortalId;

        public Cell(CellKind kind, BlockColor color = BlockColor.Red, Direction dir = Direction.Up, int portalId = 0)
        {
            Kind = kind;
            Color = color;
            Dir = dir;
            PortalId = portalId;
        }

        /// <summary>Gate direction (same as <see cref="Dir"/>).</summary>
        public Direction Gate => Dir;

        public static readonly Cell Floor = new Cell(CellKind.Floor);
        public static readonly Cell Wall = new Cell(CellKind.Wall);
        public static readonly Cell Sticky = new Cell(CellKind.Sticky);
        public static readonly Cell Lock = new Cell(CellKind.Lock);
        public static readonly Cell Keyhole = new Cell(CellKind.Keyhole);
        public static Cell Bin(BlockColor color) => new Cell(CellKind.Bin, color);
        public static Cell OneWay(Direction dir) => new Cell(CellKind.Gate, BlockColor.Red, dir);
        public static Cell Portal(int id) => new Cell(CellKind.Portal, BlockColor.Red, Direction.Up, id);
        public static Cell PaintOf(BlockColor color) => new Cell(CellKind.Paint, color);
        public static Cell ConveyorTo(Direction dir) => new Cell(CellKind.Conveyor, BlockColor.Red, dir);

        /// <summary>Gate characters indexed by <see cref="Direction"/> (Up, Down, Left, Right).</summary>
        public const string GateChars = "^v<>";

        /// <summary>Conveyor characters indexed by <see cref="Direction"/>: up [, down ], left {, right }.</summary>
        public const string ConveyorChars = "[]{}";

        /// <summary>The level-file character for this cell (GDD §5).</summary>
        public char ToChar()
        {
            switch (Kind)
            {
                case CellKind.Floor: return '.';
                case CellKind.Wall: return '#';
                case CellKind.Bin: return char.ToUpperInvariant(Color.ToLetter());
                case CellKind.Gate: return GateChars[(int)Dir];
                case CellKind.Sticky: return 's';
                case CellKind.Portal: return (char)('0' + PortalId);
                case CellKind.Paint: return Color.ToLetter();
                case CellKind.Lock: return 'L';
                case CellKind.Keyhole: return 'O';
                case CellKind.Conveyor: return ConveyorChars[(int)Dir];
                default: throw new InvalidOperationException("Unknown cell kind " + Kind);
            }
        }

        /// <summary>Parses one level-file cell character. Returns false for unknown characters.</summary>
        public static bool TryParse(char c, out Cell cell)
        {
            cell = Floor;
            if (c == '.') return true;
            if (c == '#') { cell = Wall; return true; }
            if (c == 's') { cell = Sticky; return true; }
            if (c == 'L') { cell = Lock; return true; }
            if (c == 'O') { cell = Keyhole; return true; }
            if (c == '1' || c == '2') { cell = Portal(c - '0'); return true; }
            int g = GateChars.IndexOf(c);
            if (g >= 0) { cell = OneWay((Direction)g); return true; }
            int k = ConveyorChars.IndexOf(c);
            if (k >= 0) { cell = ConveyorTo((Direction)k); return true; }
            if (BlockColors.TryFromLetter(c, out BlockColor color))
            {
                cell = char.IsUpper(c) ? Bin(color) : PaintOf(color);
                return true;
            }
            return false;
        }
    }

    /// <summary>The static part of a level: size and cells. Immutable; shared by every state of the level.</summary>
    public sealed class BoardLayout
    {
        private readonly Cell[] _cells;
        private readonly int[] _partner; // portal partner cell index, -1 if none

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

            _partner = new int[_cells.Length];
            for (int i = 0; i < _cells.Length; i++)
            {
                _partner[i] = -1;
                if (_cells[i].Kind != CellKind.Portal) continue;
                for (int j = 0; j < _cells.Length; j++)
                    if (j != i && _cells[j].Kind == CellKind.Portal && _cells[j].PortalId == _cells[i].PortalId) _partner[i] = j;
            }
        }

        public int CellCount => _cells.Length;

        public bool InBounds(GridPos p) => p.X >= 0 && p.Y >= 0 && p.X < Width && p.Y < Height;

        public int IndexOf(GridPos p) => p.Y * Width + p.X;

        public GridPos PosOf(int index) => new GridPos(index % Width, index / Width);

        public Cell this[GridPos p] => _cells[IndexOf(p)];

        public Cell this[int x, int y] => _cells[y * Width + x];

        /// <summary>The other portal of the pair at <paramref name="p"/>, or null.</summary>
        public GridPos? PartnerOf(GridPos p)
        {
            int j = _partner[IndexOf(p)];
            return j < 0 ? (GridPos?)null : PosOf(j);
        }

        public bool HasKind(CellKind kind) => CountKind(kind) > 0;

        public int CountKind(CellKind kind)
        {
            int n = 0;
            for (int i = 0; i < _cells.Length; i++) if (_cells[i].Kind == kind) n++;
            return n;
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
