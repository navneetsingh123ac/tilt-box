using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TiltBox.Core
{
    /// <summary>One stored generator candidate: the level plus the stats shown in the picker.</summary>
    public sealed class StoredCandidate
    {
        public LevelDefinition Level;
        public double Score;
        public string Summary;
    }

    /// <summary>
    /// The generator keeps the best candidates per slot (§5: "keep the 10 best per slot") in one JSON file per
    /// slot, so a person can later play them and pick one in the editor without generating again.
    /// </summary>
    public static class CandidateFile
    {
        public static string Write(string slotId, IList<Candidate> candidates)
        {
            var sb = new StringBuilder();
            sb.Append("{\n\"slot\": \"").Append(slotId).Append("\",\n\"candidates\": [\n");
            for (int i = 0; i < candidates.Count; i++)
            {
                Candidate c = candidates[i];
                sb.Append("{ \"score\": ").Append(c.Score.ToString("0.###", CultureInfo.InvariantCulture))
                  .Append(", \"summary\": \"").Append(c.Summary).Append("\",\n\"level\": ")
                  .Append(LevelWriter.ToJson(c.Level).TrimEnd())
                  .Append(i < candidates.Count - 1 ? " },\n" : " }\n");
            }
            sb.Append("]\n}\n");
            return sb.ToString();
        }

        public static List<StoredCandidate> Read(string json, string sourceName = null)
        {
            var root = MiniJson.Parse(json) as Dictionary<string, object>;
            var list = new List<StoredCandidate>();
            if (root == null || !(root.TryGetValue("candidates", out object arr) && arr is List<object> items))
                throw new LevelFormatException((sourceName ?? "candidates") + ": not a candidates file.");
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i] as Dictionary<string, object>;
                if (item == null) continue;
                list.Add(new StoredCandidate
                {
                    Level = LevelParser.FromObject(item["level"], (sourceName ?? "candidates") + "#" + i),
                    Score = item.TryGetValue("score", out object s) && s is double d ? d : 0,
                    Summary = item.TryGetValue("summary", out object m) ? m as string : "",
                });
            }
            return list;
        }
    }
}
