using System;
using System.Collections.Generic;

namespace TiltBox.Core
{
    public sealed class LevelFormatException : Exception
    {
        public LevelFormatException(string message) : base(message) { }
        public LevelFormatException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// Turns level JSON (GDD §5) into a <see cref="LevelDefinition"/>. All validation happens here, at the
    /// boundary, so the rest of the game can trust a LevelDefinition. Errors name the level and the problem.
    /// </summary>
    public static class LevelParser
    {
        public const int MinSize = 3;
        public const int MaxWidth = 7;
        public const int MaxHeight = 8;
        public const int MaxBlocks = 10;

        // Cell characters from later worlds. Recognised so the error says "not supported yet"
        // instead of "unknown character".
        private const string FutureCellChars = "><^vs12rbygpkLO}{][";

        public static LevelDefinition Parse(string json, string sourceName = null)
        {
            string label = sourceName ?? "level";
            object root;
            try
            {
                root = MiniJson.Parse(json);
            }
            catch (FormatException e)
            {
                throw new LevelFormatException(label + ": invalid JSON: " + e.Message, e);
            }

            var obj = root as Dictionary<string, object>;
            if (obj == null) throw new LevelFormatException(label + ": the root must be a JSON object.");

            string id = GetString(obj, "id", label);
            label = id;
            int world = GetInt(obj, "world", label);
            int w = GetInt(obj, "w", label);
            int h = GetInt(obj, "h", label);
            int par = GetInt(obj, "par", label);
            int limit = GetInt(obj, "limit", label);
            LevelKind kind = ParseKind(obj.TryGetValue("kind", out object k) ? k as string : "normal", label);

            if (w < MinSize || w > MaxWidth || h < MinSize || h > MaxHeight)
                throw Fail(label, "board is " + w + "x" + h + "; allowed sizes are " + MinSize + "x" + MinSize +
                                  " to " + MaxWidth + "x" + MaxHeight + ".");
            if (par < 1) throw Fail(label, "par must be at least 1.");
            if (limit < par) throw Fail(label, "limit (" + limit + ") is lower than par (" + par + ").");

            BoardLayout layout = ParseCells(obj, w, h, label);
            BoardState start = ParseBlocks(obj, layout, label);
            return new LevelDefinition(id, world, layout, start, par, limit, kind);
        }

        private static BoardLayout ParseCells(Dictionary<string, object> obj, int w, int h, string label)
        {
            var rows = GetList(obj, "cells", label);
            if (rows.Count != h) throw Fail(label, "cells has " + rows.Count + " rows but h is " + h + ".");
            var cells = new Cell[w * h];
            for (int y = 0; y < h; y++)
            {
                var row = rows[y] as string;
                if (row == null) throw Fail(label, "cells row " + y + " is not a string.");
                if (row.Length != w) throw Fail(label, "cells row " + y + " has " + row.Length + " characters but w is " + w + ".");
                for (int x = 0; x < w; x++)
                {
                    char c = row[x];
                    Cell cell;
                    if (c == '.') cell = Cell.Floor;
                    else if (c == '#') cell = Cell.Wall;
                    else if (char.IsUpper(c) && BlockColors.TryFromLetter(c, out BlockColor color))
                        cell = Cell.Bin(color);
                    else if (char.IsLower(c) && BlockColors.TryFromLetter(c, out _))
                        throw Fail(label, "cell '" + c + "' at (" + x + "," + y + ") is a paint cell (later world, not supported yet). " +
                                          "Blocks go in \"blocks\", not in \"cells\".");
                    else if (FutureCellChars.IndexOf(c) >= 0)
                        throw Fail(label, "cell '" + c + "' at (" + x + "," + y + ") belongs to a later world and is not supported yet.");
                    else
                        throw Fail(label, "unknown cell character '" + c + "' at (" + x + "," + y + ").");
                    cells[y * w + x] = cell;
                }
            }
            return new BoardLayout(w, h, cells);
        }

        private static BoardState ParseBlocks(Dictionary<string, object> obj, BoardLayout layout, string label)
        {
            var list = GetList(obj, "blocks", label);
            if (list.Count == 0) throw Fail(label, "a level needs at least one block.");
            if (list.Count > MaxBlocks) throw Fail(label, "has " + list.Count + " blocks; the maximum is " + MaxBlocks + ".");

            var positions = new GridPos[list.Count];
            var colors = new BlockColor[list.Count];
            var seen = new HashSet<GridPos>();
            for (int i = 0; i < list.Count; i++)
            {
                var b = list[i] as Dictionary<string, object>;
                string where = "block " + i;
                if (b == null) throw Fail(label, where + " is not an object.");
                foreach (string unsupported in new[] { "ice", "link", "key" })
                    if (b.ContainsKey(unsupported))
                        throw Fail(label, where + " uses \"" + unsupported + "\", which belongs to a later world and is not supported yet.");

                int x = GetInt(b, "x", label);
                int y = GetInt(b, "y", label);
                string c = GetString(b, "c", label);
                var pos = new GridPos(x, y);
                if (!layout.InBounds(pos)) throw Fail(label, where + " at " + pos + " is outside the board.");
                if (c.Length != 1 || !char.IsLower(c[0]) || !BlockColors.TryFromLetter(c[0], out BlockColor color))
                    throw Fail(label, where + " has unknown colour \"" + c + "\" (use r b y g p k).");
                Cell cell = layout[pos];
                if (cell.Kind == CellKind.Wall) throw Fail(label, where + " at " + pos + " is on a wall.");
                if (cell.Kind == CellKind.Bin) throw Fail(label, where + " at " + pos + " is on a bin.");
                if (!seen.Add(pos)) throw Fail(label, "two blocks share cell " + pos + ".");
                if (!layout.HasBin(color)) throw Fail(label, where + " is " + color + " but the board has no " + color + " bin.");
                positions[i] = pos;
                colors[i] = color;
            }
            return new BoardState(positions, colors);
        }

        private static LevelKind ParseKind(string s, string label)
        {
            switch (s)
            {
                case "normal": return LevelKind.Normal;
                case "easy": return LevelKind.Easy;
                case "hard": return LevelKind.Hard;
                case "teach": return LevelKind.Teach;
                default: throw Fail(label, "unknown kind \"" + s + "\" (use normal, easy, hard or teach).");
            }
        }

        private static LevelFormatException Fail(string label, string message) => new LevelFormatException(label + ": " + message);

        private static object Get(Dictionary<string, object> obj, string key, string label)
        {
            if (!obj.TryGetValue(key, out object v) || v == null) throw Fail(label, "missing \"" + key + "\".");
            return v;
        }

        private static string GetString(Dictionary<string, object> obj, string key, string label)
        {
            if (!(Get(obj, key, label) is string s)) throw Fail(label, "\"" + key + "\" must be a string.");
            return s;
        }

        private static int GetInt(Dictionary<string, object> obj, string key, string label)
        {
            if (!(Get(obj, key, label) is double d) || d != Math.Floor(d) || d < int.MinValue || d > int.MaxValue)
                throw Fail(label, "\"" + key + "\" must be a whole number.");
            return (int)d;
        }

        private static List<object> GetList(Dictionary<string, object> obj, string key, string label)
        {
            if (!(Get(obj, key, label) is List<object> l)) throw Fail(label, "\"" + key + "\" must be an array.");
            return l;
        }
    }
}
