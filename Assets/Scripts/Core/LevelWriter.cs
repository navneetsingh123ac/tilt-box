using System.Globalization;
using System.Text;

namespace TiltBox.Core
{
    /// <summary>
    /// Writes a level in the GDD §5 JSON format, one board row per line so diffs and hand edits stay readable.
    /// <see cref="LevelParser"/> reads back exactly what this writes.
    /// </summary>
    public static class LevelWriter
    {
        public static string ToJson(LevelDefinition level, string note = null)
        {
            BoardLayout layout = level.Layout;
            BoardState s = level.Start;
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"id\": \"").Append(level.Id).Append("\", \"world\": ").Append(Num(level.World))
              .Append(", \"w\": ").Append(Num(layout.Width)).Append(", \"h\": ").Append(Num(layout.Height)).Append(",\n");
            if (!string.IsNullOrEmpty(note)) sb.Append("  // ").Append(note.Replace('\n', ' ')).Append('\n');
            sb.Append("  \"cells\": [");
            for (int y = 0; y < layout.Height; y++)
            {
                if (y > 0) sb.Append(",\n            ");
                sb.Append('"');
                for (int x = 0; x < layout.Width; x++) sb.Append(layout[x, y].ToChar());
                sb.Append('"');
            }
            sb.Append("],\n  \"blocks\": [\n");
            for (int i = 0; i < s.BlockCount; i++)
            {
                GridPos p = s.PositionOf(i);
                sb.Append("    { \"x\": ").Append(Num(p.X)).Append(", \"y\": ").Append(Num(p.Y));
                if (s.IsKey(i)) sb.Append(", \"key\": true");
                else sb.Append(", \"c\": \"").Append(s.ColorOf(i).ToLetter()).Append('"');
                if (s.IsIced(i)) sb.Append(", \"ice\": true");
                if (s.LinkOf(i) != 0) sb.Append(", \"link\": ").Append(Num(s.LinkOf(i)));
                sb.Append(i < s.BlockCount - 1 ? " },\n" : " }\n");
            }
            sb.Append("  ],\n");
            RuleSet r = level.Rules;
            if (!r.IsStandard)
            {
                var parts = new System.Collections.Generic.List<string>();
                if (r.Forbidden != null) parts.Add("\"forbid\": \"" + r.Forbidden.Value.ToString().ToLowerInvariant() + "\"");
                if (r.Reversed) parts.Add("\"reversed\": true");
                if (r.ConveyorPasses != 1) parts.Add("\"conveyorPasses\": " + Num(r.ConveyorPasses));
                sb.Append("  \"rules\": { ").Append(string.Join(", ", parts)).Append(" },\n");
            }
            sb.Append("  \"par\": ").Append(Num(level.Par)).Append(", \"limit\": ").Append(Num(level.Limit))
              .Append(", \"kind\": \"").Append(KindName(level.Kind)).Append("\"\n}\n");
            return sb.ToString();
        }

        public static string KindName(LevelKind kind)
        {
            switch (kind)
            {
                case LevelKind.Easy: return "easy";
                case LevelKind.Hard: return "hard";
                case LevelKind.Teach: return "teach";
                case LevelKind.Daily: return "daily";
                default: return "normal";
            }
        }

        private static string Num(int n) => n.ToString(CultureInfo.InvariantCulture);
    }
}
