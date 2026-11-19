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
        public const int MaxPortalPairs = 2;

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

            return FromObject(root, label);
        }

        /// <summary>Builds a level from an already-parsed JSON value (e.g. one entry of a candidates file).</summary>
        public static LevelDefinition FromObject(object root, string sourceName = null)
        {
            string label = sourceName ?? "level";
            var obj = root as Dictionary<string, object>;
            if (obj == null) throw new LevelFormatException(label + ": the level must be a JSON object.");

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
            RuleSet rules = ParseRules(obj, label);
            return new LevelDefinition(id, world, layout, start, par, limit, kind, rules);
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
                    if (!Cell.TryParse(row[x], out Cell cell))
                        throw Fail(label, "unknown cell character '" + row[x] + "' at (" + x + "," + y + ").");
                    cells[y * w + x] = cell;
                }
            }

            var layout = new BoardLayout(w, h, cells);
            for (int id = 1; id <= MaxPortalPairs; id++)
            {
                int n = 0;
                foreach (Cell c in cells) if (c.Kind == CellKind.Portal && c.PortalId == id) n++;
                if (n != 0 && n != 2) throw Fail(label, "portal " + id + " appears " + n + " times; portals come in pairs.");
            }
            if (layout.HasKind(CellKind.Lock) && !layout.HasKind(CellKind.Keyhole))
                throw Fail(label, "has lock walls but no keyhole to open them.");
            return layout;
        }

        private static BoardState ParseBlocks(Dictionary<string, object> obj, BoardLayout layout, string label)
        {
            var list = GetList(obj, "blocks", label);
            if (list.Count == 0) throw Fail(label, "a level needs at least one block.");
            if (list.Count > MaxBlocks) throw Fail(label, "has " + list.Count + " blocks; the maximum is " + MaxBlocks + ".");

            var positions = new GridPos[list.Count];
            var colors = new BlockColor[list.Count];
            var iced = new bool[list.Count];
            var links = new int[list.Count];
            var seen = new HashSet<GridPos>();
            bool hasPaint = layout.HasKind(CellKind.Paint);
            bool anyKey = false, anyColoured = false;
            for (int i = 0; i < list.Count; i++)
            {
                var b = list[i] as Dictionary<string, object>;
                string where = "block " + i;
                if (b == null) throw Fail(label, where + " is not an object.");

                int x = GetInt(b, "x", label);
                int y = GetInt(b, "y", label);
                var pos = new GridPos(x, y);
                if (!layout.InBounds(pos)) throw Fail(label, where + " at " + pos + " is outside the board.");

                bool isKey = b.TryGetValue("key", out object keyValue) && GetBool(keyValue, where + " \"key\"", label);
                BlockColor color;
                if (isKey)
                {
                    if (b.ContainsKey("c")) throw Fail(label, where + " is a key, so it has no colour \"c\".");
                    color = BlockColor.Key;
                    anyKey = true;
                }
                else
                {
                    string c = GetString(b, "c", label);
                    if (c.Length != 1 || !char.IsLower(c[0]) || !BlockColors.TryFromLetter(c[0], out color))
                        throw Fail(label, where + " has unknown colour \"" + c + "\" (use r b y g p k).");
                    if (!hasPaint && !layout.HasBin(color))
                        throw Fail(label, where + " is " + color + " but the board has no " + color + " bin.");
                    anyColoured = true;
                }

                if (b.TryGetValue("ice", out object ice)) iced[i] = GetBool(ice, where + " \"ice\"", label);
                if (b.TryGetValue("link", out object linkValue))
                {
                    if (isKey) throw Fail(label, where + " is a key; keys can't be linked.");
                    if (!(linkValue is double ld) || ld != Math.Floor(ld) || ld < 1 || ld > 15)
                        throw Fail(label, where + " has \"link\" that is not a number from 1 to 15.");
                    links[i] = (int)ld;
                }

                switch (layout[pos].Kind)
                {
                    case CellKind.Wall:
                    case CellKind.Bin:
                    case CellKind.Keyhole:
                    case CellKind.Lock:
                    case CellKind.Portal:
                        throw Fail(label, where + " at " + pos + " is on a " + layout[pos].Kind.ToString().ToLowerInvariant() + " cell.");
                }
                if (!seen.Add(pos)) throw Fail(label, "two blocks share cell " + pos + ".");
                positions[i] = pos;
                colors[i] = color;
            }

            if (!anyColoured) throw Fail(label, "needs at least one coloured block.");
            if (anyKey && !layout.HasKind(CellKind.Keyhole)) throw Fail(label, "has a key block but no keyhole.");
            if (layout.HasKind(CellKind.Keyhole) && !anyKey) throw Fail(label, "has a keyhole but no key block.");

            // Each link id joins exactly two side-by-side blocks.
            for (int i = 0; i < links.Length; i++)
            {
                if (links[i] == 0) continue;
                int partner = -1, n = 0;
                for (int j = 0; j < links.Length; j++)
                {
                    if (links[j] != links[i]) continue;
                    n++;
                    if (j != i) partner = j;
                }
                if (n != 2) throw Fail(label, "link " + links[i] + " joins " + n + " blocks; a link joins exactly two.");
                int dist = Math.Abs(positions[i].X - positions[partner].X) + Math.Abs(positions[i].Y - positions[partner].Y);
                if (dist != 1) throw Fail(label, "link " + links[i] + " joins blocks that are not side by side.");
            }
            return new BoardState(positions, colors, null, iced, links);
        }

        private static RuleSet ParseRules(Dictionary<string, object> obj, string label)
        {
            if (!obj.TryGetValue("rules", out object r) || r == null) return RuleSet.Standard;
            var rules = r as Dictionary<string, object>;
            if (rules == null) throw Fail(label, "\"rules\" must be an object.");
            Direction? forbidden = null;
            if (rules.TryGetValue("forbid", out object f))
            {
                int idx = Array.IndexOf(new[] { "up", "down", "left", "right" }, f as string);
                if (idx < 0) throw Fail(label, "rules.forbid must be up, down, left or right.");
                forbidden = (Direction)idx;
            }
            bool reversed = rules.TryGetValue("reversed", out object rv) && GetBool(rv, "rules.reversed", label);
            int passes = 1;
            if (rules.TryGetValue("conveyorPasses", out object cp))
            {
                if (!(cp is double d) || d != Math.Floor(d) || d < 1 || d > 3)
                    throw Fail(label, "rules.conveyorPasses must be 1, 2 or 3.");
                passes = (int)d;
            }
            return new RuleSet(forbidden, reversed, passes);
        }

        private static LevelKind ParseKind(string s, string label)
        {
            switch (s)
            {
                case "normal": return LevelKind.Normal;
                case "easy": return LevelKind.Easy;
                case "hard": return LevelKind.Hard;
                case "teach": return LevelKind.Teach;
                case "daily": return LevelKind.Daily;
                default: throw Fail(label, "unknown kind \"" + s + "\" (use normal, easy, hard, teach or daily).");
            }
        }

        private static bool GetBool(object value, string what, string label)
        {
            if (!(value is bool v)) throw Fail(label, what + " is not true or false.");
            return v;
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
