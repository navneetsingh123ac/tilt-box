using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using TiltBox.Core;

namespace TiltBox.Meta
{
    /// <summary>
    /// Reads and writes <see cref="SaveData"/> in the GDD §15 JSON shape. Reading is forgiving about missing
    /// fields (older saves get defaults) but rejects a damaged file, so <see cref="SaveStore"/> can fall back to
    /// the backup.
    /// </summary>
    public static class SaveJson
    {
        public static string Write(SaveData s)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            Field(sb, "v", s.Version);
            Field(sb, "level", s.Level);
            sb.Append("  \"stars\": {");
            sb.Append(string.Join(", ", s.Stars.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => Str(k.Key) + ": " + Num(k.Value))));
            sb.Append("},\n");
            sb.Append("  \"skipped\": [").Append(string.Join(", ", s.Skipped.OrderBy(x => x, StringComparer.Ordinal).Select(Str))).Append("],\n");
            Field(sb, "coins", s.Coins);
            Field(sb, "hints", s.Hints);
            Field(sb, "undos", s.Undos);
            Field(sb, "chestProgress", s.ChestProgress);
            sb.Append("  \"shelf\": [").Append(string.Join(", ", s.Shelf.Select(Str))).Append("],\n");
            sb.Append("  \"equipped\": { \"skin\": ").Append(Str(s.EquippedSkin)).Append(", \"bg\": ").Append(Str(s.EquippedBackground)).Append(" },\n");
            sb.Append("  \"daily\": { \"lastSolved\": ").Append(Str(s.DailyLastSolved)).Append(", \"daysSolved\": ").Append(Num(s.DaysSolved)).Append(" },\n");
            sb.Append("  \"calendar\": { \"nextGift\": ").Append(Num(s.CalendarNextGift)).Append(", \"lastClaim\": ").Append(Str(s.CalendarLastClaim))
              .Append(", \"cycle\": ").Append(Num(s.CalendarCycle)).Append(" },\n");
            sb.Append("  \"tower\": { \"weekId\": ").Append(Str(s.TowerWeekId)).Append(", \"reached\": ").Append(Num(s.TowerReached)).Append(" },\n");
            sb.Append("  \"noForcedAds\": ").Append(Bool(s.NoForcedAds)).Append(",\n");
            sb.Append("  \"settings\": { \"sound\": ").Append(Bool(s.Sound)).Append(", \"music\": ").Append(Bool(s.Music))
              .Append(", \"vibration\": ").Append(Bool(s.Vibration)).Append(", \"notifications\": ").Append(Bool(s.Notifications))
              .Append(", \"symbols\": ").Append(Bool(s.SymbolsAlwaysOn)).Append(" },\n");
            sb.Append("  \"stats\": { \"wins\": ").Append(Num(s.Wins)).Append(", \"losses\": ").Append(Num(s.Losses))
              .Append(", \"adsWatched\": ").Append(Num(s.AdsWatched)).Append(", \"firstOpen\": ").Append(Str(s.FirstOpen)).Append(" },\n");
            sb.Append("  \"lastSeen\": ").Append(Str(s.LastSeenDate)).Append("\n}\n");
            return sb.ToString();
        }

        /// <summary>Parses a save. Throws <see cref="FormatException"/> if the text is damaged or not a save.</summary>
        public static SaveData Read(string json)
        {
            if (!(MiniJson.Parse(json) is Dictionary<string, object> o)) throw new FormatException("save is not a JSON object");
            if (!o.ContainsKey("v")) throw new FormatException("save has no version");
            int version = Int(o, "v", 0);
            if (version < 1 || version > SaveData.CurrentVersion) throw new FormatException("unsupported save version " + version);

            var s = new SaveData
            {
                Version = version,
                Level = Int(o, "level", 0),
                Coins = Int(o, "coins", 0),
                Hints = Int(o, "hints", Economy.StartHints),
                Undos = Int(o, "undos", Economy.StartUndos),
                ChestProgress = Int(o, "chestProgress", 0),
                NoForcedAds = Bool(o, "noForcedAds", false),
                LastSeenDate = Str(o, "lastSeen"),
            };
            if (o.TryGetValue("stars", out object stars) && stars is Dictionary<string, object> sd)
                foreach (var kv in sd) if (kv.Value is double d) s.Stars[kv.Key] = (int)d;
            foreach (object x in List(o, "skipped")) if (x is string id) s.Skipped.Add(id);
            foreach (object x in List(o, "shelf")) if (x is string id && !s.Shelf.Contains(id)) s.Shelf.Add(id);

            var eq = Obj(o, "equipped");
            s.EquippedSkin = Str(eq, "skin");
            s.EquippedBackground = Str(eq, "bg");
            var daily = Obj(o, "daily");
            s.DailyLastSolved = Str(daily, "lastSolved");
            s.DaysSolved = Int(daily, "daysSolved", 0);
            var cal = Obj(o, "calendar");
            s.CalendarNextGift = Int(cal, "nextGift", 1);
            s.CalendarLastClaim = Str(cal, "lastClaim");
            s.CalendarCycle = Int(cal, "cycle", 1);
            var tower = Obj(o, "tower");
            s.TowerWeekId = Str(tower, "weekId");
            s.TowerReached = Int(tower, "reached", 0);
            var set = Obj(o, "settings");
            s.Sound = Bool(set, "sound", true);
            s.Music = Bool(set, "music", true);
            s.Vibration = Bool(set, "vibration", true);
            s.Notifications = Bool(set, "notifications", true);
            s.SymbolsAlwaysOn = Bool(set, "symbols", true);
            var stats = Obj(o, "stats");
            s.Wins = Int(stats, "wins", 0);
            s.Losses = Int(stats, "losses", 0);
            s.AdsWatched = Int(stats, "adsWatched", 0);
            s.FirstOpen = Str(stats, "firstOpen");

            if (s.Level < 0 || s.Coins < 0 || s.Hints < 0 || s.Undos < 0) throw new FormatException("save has negative values");
            if (s.CalendarNextGift < 1 || s.CalendarNextGift > 7) s.CalendarNextGift = 1;
            return s;
        }

        // --- writing helpers ---
        private static void Field(StringBuilder sb, string key, int v) => sb.Append("  ").Append(Str(key)).Append(": ").Append(Num(v)).Append(",\n");
        private static string Num(int v) => v.ToString(CultureInfo.InvariantCulture);
        private static string Bool(bool b) => b ? "true" : "false";

        private static string Str(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s ?? "")
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < 32) sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        // --- reading helpers (missing keys give defaults) ---
        private static Dictionary<string, object> Obj(Dictionary<string, object> o, string key) =>
            o.TryGetValue(key, out object v) && v is Dictionary<string, object> d ? d : new Dictionary<string, object>();

        private static List<object> List(Dictionary<string, object> o, string key) =>
            o.TryGetValue(key, out object v) && v is List<object> l ? l : new List<object>();

        private static int Int(Dictionary<string, object> o, string key, int fallback) =>
            o.TryGetValue(key, out object v) && v is double d ? (int)d : fallback;

        private static bool Bool(Dictionary<string, object> o, string key, bool fallback) =>
            o.TryGetValue(key, out object v) && v is bool b ? b : fallback;

        private static string Str(Dictionary<string, object> o, string key) =>
            o.TryGetValue(key, out object v) && v is string s ? s : "";
    }
}
