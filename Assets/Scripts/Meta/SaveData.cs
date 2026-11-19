using System.Collections.Generic;

namespace TiltBox.Meta
{
    /// <summary>
    /// Everything the game remembers (GDD §15). Plain data; rules live in <see cref="MetaGame"/>,
    /// JSON in <see cref="SaveJson"/>, files in <see cref="SaveStore"/>.
    /// </summary>
    public sealed class SaveData
    {
        public const int CurrentVersion = 1;

        public int Version = CurrentVersion;

        /// <summary>Index of the next unplayed campaign level (0-based, across all worlds).</summary>
        public int Level;

        /// <summary>Best stars per level id ("w1_01": 3).</summary>
        public Dictionary<string, int> Stars = new Dictionary<string, int>();

        /// <summary>Levels skipped with the "Skip level" ad (§7); they give no stars until replayed.</summary>
        public HashSet<string> Skipped = new HashSet<string>();

        public int Coins;
        public int Hints = Economy.StartHints;
        public int Undos = Economy.StartUndos;

        /// <summary>New levels won since the last small chest (0-4).</summary>
        public int ChestProgress;

        /// <summary>Owned shelf items, in the order they were collected.</summary>
        public List<string> Shelf = new List<string>();

        public string EquippedSkin = "";
        public string EquippedBackground = "";

        // Daily puzzle (§9)
        public string DailyLastSolved = "";
        public int DaysSolved;

        // Gift calendar (§9): 1-7, the gift waiting to be claimed.
        public int CalendarNextGift = 1;
        public string CalendarLastClaim = "";
        public int CalendarCycle = 1;

        // Weekend Tower (§9)
        public string TowerWeekId = "";
        public int TowerReached;

        public bool NoForcedAds;

        // Settings (§12)
        public bool Sound = true;
        public bool Music = true;
        public bool Vibration = true;
        public bool Notifications = true;
        public bool SymbolsAlwaysOn = true;

        // Stats (§15) and the clock guard (§14)
        public int Wins;
        public int Losses;
        public int AdsWatched;
        public string FirstOpen = "";

        /// <summary>Latest local date the game has seen; used to spot the clock moving backwards.</summary>
        public string LastSeenDate = "";
    }
}
