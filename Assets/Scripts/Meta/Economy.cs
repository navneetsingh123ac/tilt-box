namespace TiltBox.Meta
{
    /// <summary>GDD §8 numbers in one place, so balance changes are a one-line edit.</summary>
    public static class Economy
    {
        public const int StartHints = 3;
        public const int StartUndos = 3;

        public const int WinBase = 10;
        public const int WinPerExtraStar = 5;
        public const int HardMultiplier = 2;
        public const int FirstThreeStarsOnReplay = 10;

        public const int DailyReward = 30;
        public const int TowerLevelReward = 15;
        public const int TowerCompleteReward = 200;

        public const int LevelsPerSmallChest = 5;
        public const int SmallChestMinCoins = 40;
        public const int SmallChestMaxCoins = 80;
        public const double SmallChestTokenChance = 0.5;

        public const int WorldChestCoins = 300;
        public const int WorldChestHints = 2;

        public const int HintPrice = 100;
        public const int UndoPrice = 50;

        /// <summary>Free undos per level attempt before tokens are used (§6).</summary>
        public const int FreeUndosPerLevel = 1;

        /// <summary>Daily puzzle unlocks once this many campaign levels are done (§9).</summary>
        public const int DailyUnlockLevel = 10;

        public static readonly int[] DailyBadges = { 7, 30, 100 };

        /// <summary>Coins for a campaign win (§8): 10, +5 per star above one, ×2 on hard levels.</summary>
        public static int WinCoins(int stars, bool hard)
        {
            int coins = WinBase + WinPerExtraStar * (stars - 1);
            return hard ? coins * HardMultiplier : coins;
        }
    }
}
