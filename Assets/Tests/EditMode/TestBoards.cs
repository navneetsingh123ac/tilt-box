using System;
using System.Collections.Generic;
using System.IO;
using TiltBox.Core;

namespace TiltBox.Core.Tests
{
    /// <summary>
    /// Test-only ASCII boards: level cell characters, lowercase colour letters for blocks standing on floor,
    /// and 'x' for a key block. Example: "r.R" = red block, floor, red bin. Paint cells (lowercase in level files)
    /// clash with block letters here, so tests add them with <see cref="WithCell"/>.
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
                    if (c == 'x' || (char.IsLower(c) && c != 's' && c != 'v' && BlockColors.TryFromLetter(c, out _)))
                    {
                        cells[y * w + x] = Cell.Floor;
                        positions.Add(new GridPos(x, y));
                        colors.Add(c == 'x' ? BlockColor.Key : (BlockColors.TryFromLetter(c, out BlockColor bc) ? bc : BlockColor.Red));
                    }
                    else if (Cell.TryParse(c, out Cell cell)) cells[y * w + x] = cell;
                    else throw new ArgumentException("bad test char " + c);
                }
            }
            return (new BoardLayout(w, h, cells), new BoardState(positions.ToArray(), colors.ToArray()));
        }

        /// <summary>Same state with the given block indices iced.</summary>
        public static BoardState Ice(BoardState s, params int[] blocks)
        {
            var ice = new bool[s.BlockCount];
            foreach (int b in blocks) ice[b] = true;
            return Rebuild(s, ice, null);
        }

        /// <summary>Same state with blocks a and b joined by link id.</summary>
        public static BoardState Link(BoardState s, int a, int b, int id = 1)
        {
            var links = new int[s.BlockCount];
            for (int i = 0; i < s.BlockCount; i++) links[i] = s.LinkOf(i);
            links[a] = links[b] = id;
            return Rebuild(s, null, links);
        }

        private static BoardState Rebuild(BoardState s, bool[] ice, int[] links)
        {
            var pos = new GridPos[s.BlockCount];
            var col = new BlockColor[s.BlockCount];
            var oldIce = new bool[s.BlockCount];
            var oldLinks = new int[s.BlockCount];
            for (int i = 0; i < s.BlockCount; i++)
            {
                pos[i] = s.PositionOf(i);
                col[i] = s.ColorOf(i);
                oldIce[i] = s.IsIced(i);
                oldLinks[i] = s.LinkOf(i);
            }
            return new BoardState(pos, col, null, ice ?? oldIce, links ?? oldLinks, s.LocksOpen);
        }

        /// <summary>A copy of the layout with one cell replaced (e.g. a paint cell).</summary>
        public static BoardLayout WithCell(BoardLayout layout, int x, int y, Cell cell)
        {
            var target = new GridPos(x, y);
            int i = 0;
            return layout.Map(c => layout.PosOf(i++) == target ? cell : c);
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
                    chars[x] = cell.ToChar();
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
