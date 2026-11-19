using System;
using System.Collections.Generic;
using TiltBox.Core;
using UnityEngine;

namespace TiltBox.Game
{
    /// <summary>Loads the level JSON files that ship inside the game (Resources/Levels/World{n}).</summary>
    public static class LevelCatalog
    {
        public static List<LevelDefinition> LoadWorld(int world)
        {
            TextAsset[] files = Resources.LoadAll<TextAsset>("Levels/World" + world);
            Array.Sort(files, (a, b) => string.CompareOrdinal(a.name, b.name));
            var levels = new List<LevelDefinition>(files.Length);
            foreach (TextAsset file in files)
            {
                try
                {
                    levels.Add(LevelParser.Parse(file.text, file.name));
                }
                catch (LevelFormatException e)
                {
                    // Content tests should make this impossible; skip the level rather than break the game.
                    Debug.LogError("Skipping bad level file: " + e.Message);
                }
            }
            return levels;
        }

        /// <summary>Every world in order (World1, World2, ...), stopping at the first world with no levels.</summary>
        public static List<LevelDefinition> LoadAll()
        {
            var all = new List<LevelDefinition>();
            for (int world = 1; world < DifficultyPlan.WorldNames.Length; world++)
            {
                List<LevelDefinition> levels = LoadWorld(world);
                if (levels.Count == 0) break;
                all.AddRange(levels);
            }
            return all;
        }
    }
}
