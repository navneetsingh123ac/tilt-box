using System;
using System.Collections.Generic;
using System.IO;
using TiltBox.Core;

namespace TiltBox.Core.Tests
{
    /// <summary>
    /// Test-only ASCII boards: level cell characters plus lowercase letters for blocks standing on floor.
    /// Example: "r.R" = red block, floor, red bin.
    /// </summary>
    internal static class TestBoards
    {
        public static (BoardLayout layout, BoardState state) Parse(params string[] rows)
        {
            int h = rows.Length;
            int w = rows[0].Length;
            var cells = new Cell[w * h];
            var positions = new List<GridPos>();
            var colors = new List<BlockColor>();
            for (int y = 0; y < h; y++)
            {
                if (rows[y].Length != w) throw new ArgumentException("ragged test board");
                for (int x = 0; x < w; x++)
                {
                    char c = rows[y][x];
                    if (c == '.') cells[y * w + x] = Cell.Floor;
                    else if (c == '#') cells[y * w + x] = Cell.Wall;
                    else if (BlockColors.TryFromLetter(c, out BlockColor color))
                    {
                        if (char.IsUpper(c)) cells[y * w + x] = Cell.Bin(color);
                        else
                        {
                            cells[y * w + x] = Cell.Floor;
                            positions.Add(new GridPos(x, y));
                            colors.Add(color);
                        }
                    }
                    else throw new ArgumentException("bad test char " + c);
                }
            }
            return (new BoardLayout(w, h, cells), new BoardState(positions.ToArray(), colors.ToArray()));
        }

        /// <summary>Renders a state back to the same notation so tests can compare whole boards.</summary>
        public static string[] Render(BoardLayout layout, BoardState state)
        {
            var rows = new string[layout.Height];
            for (int y = 0; y < layout.Height; y++)
            {
                var chars = new char[layout.Width];
                for (int x = 0; x < layout.Width; x++)
                {
                    Cell cell = layout[x, y];
                    chars[x] = cell.Kind == CellKind.Wall ? '#'
                        : cell.Kind == CellKind.Bin ? char.ToUpperInvariant(cell.Color.ToLetter())
                        : '.';
                    int b = state.BlockAt(new GridPos(x, y));
                    if (b >= 0) chars[x] = state.ColorOf(b).ToLetter();
                }
                rows[y] = new string(chars);
            }
            return rows;
        }

        /// <summary>
        /// Finds Assets/Resources/Levels by walking up from the working directory (Unity: project root) and from
        /// the test binary (dotnet test: DevTools/CoreTests/bin/...). Keeps the tests engine-free.
        /// </summary>
        public static string LevelsRoot()
        {
            foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                var dir = new DirectoryInfo(start);
                while (dir != null)
                {
                    string candidate = Path.Combine(dir.FullName, "Assets", "Resources", "Levels");
                    if (Directory.Exists(candidate)) return candidate;
                    dir = dir.Parent;
                }
            }
            throw new DirectoryNotFoundException("Could not find Assets/Resources/Levels");
        }
    }
}
