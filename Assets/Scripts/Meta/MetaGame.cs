using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TiltBox.Core;

namespace TiltBox.Meta
{
    public interface IClock
    {
        /// <summary>Local time on the device (the GDD uses the phone's local date for dailies and towers).</summary>
        DateTime Now { get; }
    }

    public sealed class SystemClock : IClock
    {
        public DateTime Now => DateTime.Now;
    }

    public enum ChestKind { Small, World, Calendar }

    public sealed class ChestResult
    {
        public ChestKind Kind;
        public int Coins;
        public int Hints;
        public int Undos;
        public string ItemId; // shelf item, or null
        public bool Doubled;
    }

    public sealed class WinReward
    {
        public int Coins;
        public bool NewLevel;
        public bool FirstThreeStars;
        public ChestResult SmallChest;
        public ChestResult WorldChest;

        /// <summary>New levels still needed for the next small chest (the HUD's "2 more levels").</summary>
        public int LevelsToChest;
    }

    public sealed class DailyStatus
    {
        public bool Unlocked;
        public int Number;          // days since launch, 1-based (the "#42" in the share text)
        public int PoolDay;         // 1-365, which pre-made puzzle to play
        public bool SolvedToday;
        public bool RewardsPaused;  // the clock went backwards (§14)
        public int DaysSolved;
    }

    public sealed class DailyResult
    {
        public int Coins;
        public string ShareText;
        public int? NewBadge;       // 7, 30 or 100 when just reached
    }

    public sealed class Gift
    {
        public int Day;
        public string Label;
        public int Coins;
        public int Hints;
        public int Undos;
        public string ItemId;
        public bool BigChest;
    }

    public sealed class CalendarStatus
    {
        public bool CanClaim;
        public bool Paused;
        public Gift Next;
        public int Cycle;
    }

    public sealed class TowerStatus
    {
        public bool Open;           // Saturday or Sunday, local time
        public int Tower;           // 1-8, rotating weekly
        public string WeekId;       // e.g. "2026-W47"
        public int Reached;         // levels cleared this weekend (0-10)
        public DateTime NextOpen;   // the coming Saturday when closed
    }

    public sealed class TowerReward
    {
        public int Coins;
        public bool Completed;
        public string ItemId;
    }

    /// <summary>
    /// The meta game (GDD §7-§9, §15): coins, chests, tokens, the shelf, the daily puzzle, the gift calendar and
    /// the Weekend Tower. Engine-free: time comes from <see cref="IClock"/>, randomness from a seeded Random,
    /// and every change is saved through the persist callback right away ("save after every level, purchase
    /// and claim").
    ///
    /// Choices where the GDD is silent:
    /// - Replaying an already-won level pays no base coins (only the +10 for a first 3 stars), so levels can't be farmed.
    /// - Calendar and tower skins are extra to the 40-item shop shelf (see <see cref="ShelfCatalog"/>).
    /// - Daily rewards pause while the date is more than a day behind the latest date seen (§14 clock guard).
    /// </summary>
    public sealed class MetaGame
    {
        /// <summary>Day 1 of the daily-puzzle numbering. Set to the store launch date before release.</summary>
        public static DateTime LaunchDate = new DateTime(2026, 11, 1);

        public const int LevelsPerWorld = 30;

        private readonly IClock _clock;
        private readonly Random _rng;
        private readonly Action<SaveData> _persist;

        public SaveData Save { get; }

        public MetaGame(SaveData save, IClock clock, Action<SaveData> persist, int seed = 0)
        {
            Save = save ?? throw new ArgumentNullException(nameof(save));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _persist = persist ?? (_ => { });
            _rng = seed == 0 ? new Random() : new Random(seed);
            Touch();
        }

        public DateTime Today => _clock.Now.Date;

        /// <summary>Records the date for the clock guard and first-open stat. Call on start and resume.</summary>
        public void Touch()
        {
            string today = Iso(Today);
            bool changed = false;
            if (string.IsNullOrEmpty(Save.FirstOpen)) { Save.FirstOpen = today; changed = true; }
            if (string.IsNullOrEmpty(Save.LastSeenDate) || string.CompareOrdinal(today, Save.LastSeenDate) > 0)
            {
                Save.LastSeenDate = today;
                changed = true;
            }
            if (changed) Persist();
        }

        /// <summary>§14: the clock went back by more than a day; daily rewards wait until it catches up.</summary>
        public bool TimeRewardsPaused => (Parse(Save.LastSeenDate) - Today).TotalDays > 1;

        // ------------------------------------------------------------------ campaign

        public int Coins => Save.Coins;
        public int LevelsToChest => Economy.LevelsPerSmallChest - Save.ChestProgress;

        public int StarsFor(string levelId) => Save.Stars.TryGetValue(levelId, out int s) ? s : 0;

        /// <summary>A campaign level was won. Pays coins (§8), advances progress and opens chests.</summary>
        public WinReward OnCampaignWin(int levelIndex, string levelId, bool hard, int stars)
        {
            int previous = StarsFor(levelId);
            bool newLevel = levelIndex >= Save.Level && previous == 0;
            var reward = new WinReward { NewLevel = newLevel };

            if (newLevel) reward.Coins = Economy.WinCoins(stars, hard);
            if (!newLevel && stars == 3 && previous > 0 && previous < 3)
            {
                reward.Coins += Economy.FirstThreeStarsOnReplay;
                reward.FirstThreeStars = true;
            }
            Save.Coins += reward.Coins;
            Save.Stars[levelId] = Math.Max(previous, stars);
            Save.Skipped.Remove(levelId);
            Save.Wins++;

            if (newLevel)
            {
                Save.Level = Math.Max(Save.Level, levelIndex + 1);
                Save.ChestProgress++;
                if (Save.ChestProgress >= Economy.LevelsPerSmallChest)
                {
                    Save.ChestProgress = 0;
                    reward.SmallChest = OpenSmallChest();
                }
                if ((levelIndex + 1) % LevelsPerWorld == 0)
                    reward.WorldChest = OpenWorldChest((levelIndex + 1) / LevelsPerWorld);
            }
            reward.LevelsToChest = LevelsToChest;
            Persist();
            return reward;
        }

        public void OnLevelLost()
        {
            Save.Losses++;
            Persist();
        }

        /// <summary>§7 skip (after 3 fails, via an ad in week 5): moves on without stars.</summary>
        public void SkipLevel(int levelIndex, string levelId)
        {
            Save.Skipped.Add(levelId);
            Save.Level = Math.Max(Save.Level, levelIndex + 1);
            Persist();
        }

        // ------------------------------------------------------------------ chests (§8)

        private ChestResult OpenSmallChest()
        {
            var chest = new ChestResult
            {
                Kind = ChestKind.Small,
                Coins = _rng.Next(Economy.SmallChestMinCoins / 5, Economy.SmallChestMaxCoins / 5 + 1) * 5,
            };
            if (_rng.NextDouble() < Economy.SmallChestTokenChance)
            {
                if (_rng.Next(2) == 0) chest.Hints = 1;
                else chest.Undos = 1;
            }
            Apply(chest);
            return chest;
        }

        private ChestResult OpenWorldChest(int world)
        {
            var chest = new ChestResult { Kind = ChestKind.World, Coins = Economy.WorldChestCoins, Hints = Economy.WorldChestHints };
            if (world >= 1 && world <= 10)
            {
                string fig = ShelfCatalog.WorldFigurine(world).Id;
                if (!Save.Shelf.Contains(fig)) chest.ItemId = fig;
            }
            Apply(chest);
            return chest;
        }

        /// <summary>"Double it (watch ad)" (§8): gives the coins and tokens again (not the item). Once per chest.</summary>
        public bool DoubleChest(ChestResult chest)
        {
            if (chest == null || chest.Doubled) return false;
            chest.Doubled = true;
            Save.Coins += chest.Coins;
            Save.Hints += chest.Hints;
            Save.Undos += chest.Undos;
            Save.AdsWatched++;
            Persist();
            return true;
        }

        private void Apply(ChestResult chest)
        {
            Save.Coins += chest.Coins;
            Save.Hints += chest.Hints;
            Save.Undos += chest.Undos;
            if (chest.ItemId != null && !Save.Shelf.Contains(chest.ItemId)) Save.Shelf.Add(chest.ItemId);
        }

        // ------------------------------------------------------------------ tokens (§8)

        public int Hints => Save.Hints;
        public int Undos => Save.Undos;

        public bool TryUseHint()
        {
            if (Save.Hints <= 0) return false;
            Save.Hints--;
            Persist();
            return true;
        }

        /// <summary>Uses the level's free undo first, then an undo token. False if neither is left.</summary>
        public bool TryUseUndo(ref int freeUndosLeft)
        {
            if (freeUndosLeft > 0)
            {
                freeUndosLeft--;
                return true;
            }
            if (Save.Undos <= 0) return false;
            Save.Undos--;
            Persist();
            return true;
        }

        public bool BuyHint() => Spend(Economy.HintPrice, () => Save.Hints++);
        public bool BuyUndo() => Spend(Economy.UndoPrice, () => Save.Undos++);

        // ------------------------------------------------------------------ shelf (§8)

        public bool Owns(string itemId) => Save.Shelf.Contains(itemId);

        public bool CanBuy(ShelfItem item) => item != null && item.Source == ShelfSource.Shop && !Owns(item.Id) && Save.Coins >= item.Price;

        public bool Buy(string itemId)
        {
            ShelfItem item = ShelfCatalog.Find(itemId);
            if (!CanBuy(item)) return false;
            return Spend(item.Price, () => Save.Shelf.Add(item.Id));
        }

        public bool Equip(string itemId)
        {
            ShelfItem item = ShelfCatalog.Find(itemId);
            if (item == null || !Owns(itemId) || item.Type == ShelfItemType.Figurine) return false;
            if (item.Type == ShelfItemType.Skin) Save.EquippedSkin = itemId;
            else Save.EquippedBackground = itemId;
            Persist();
            return true;
        }

        private bool Spend(int price, Action give)
        {
            if (Save.Coins < price) return false;
            Save.Coins -= price;
            give();
            Persist();
            return true;
        }

        // ------------------------------------------------------------------ daily puzzle (§9)

        public DailyStatus Daily()
        {
            int number = Math.Max(1, (int)(Today - LaunchDate.Date).TotalDays + 1);
            return new DailyStatus
            {
                Unlocked = Save.Level >= Economy.DailyUnlockLevel,
                Number = number,
                PoolDay = (number - 1) % DifficultyPlan.DailyCount + 1,
                SolvedToday = Save.DailyLastSolved == Iso(Today),
                RewardsPaused = TimeRewardsPaused,
                DaysSolved = Save.DaysSolved,
            };
        }

        /// <param name="swipes">The player's swipes, in order (for the share text).</param>
        public DailyResult OnDailySolved(IReadOnlyList<Direction> swipes, int stars)
        {
            DailyStatus status = Daily();
            var result = new DailyResult { ShareText = ShareText(status.Number, swipes, stars) };
            if (!status.Unlocked || status.SolvedToday || status.RewardsPaused) return result;

            result.Coins = Economy.DailyReward;
            Save.Coins += result.Coins;
            Save.DaysSolved++;
            Save.DailyLastSolved = Iso(Today);
            if (Array.IndexOf(Economy.DailyBadges, Save.DaysSolved) >= 0) result.NewBadge = Save.DaysSolved;
            Persist();
            return result;
        }

        /// <summary>"Tilt Box #42 ➡️⬆️⬅️⬇️➡️ 5 moves ⭐⭐⭐" (§9).</summary>
        public static string ShareText(int number, IReadOnlyList<Direction> swipes, int stars)
        {
            var sb = new StringBuilder("Tilt Box #").Append(number).Append(' ');
            foreach (Direction d in swipes)
            {
                switch (d)
                {
                    case Direction.Up: sb.Append("⬆️"); break;
                    case Direction.Down: sb.Append("⬇️"); break;
                    case Direction.Left: sb.Append("⬅️"); break;
                    default: sb.Append("➡️"); break;
                }
            }
            sb.Append(' ').Append(swipes.Count).Append(swipes.Count == 1 ? " move " : " moves ");
            for (int i = 0; i < stars; i++) sb.Append("⭐");
            return sb.ToString();
        }

        // ------------------------------------------------------------------ gift calendar (§9)

        public static Gift GiftFor(int day, int cycle)
        {
            switch (day)
            {
                case 1: return new Gift { Day = 1, Label = "50 coins", Coins = 50 };
                case 2: return new Gift { Day = 2, Label = "1 hint", Hints = 1 };
                case 3: return new Gift { Day = 3, Label = "100 coins", Coins = 100 };
                case 4: return new Gift { Day = 4, Label = "2 undos", Undos = 2 };
                case 5: return new Gift { Day = 5, Label = "150 coins", Coins = 150 };
                case 6: return new Gift { Day = 6, Label = "2 hints", Hints = 2 };
                default:
                {
                    ShelfItem skin = ShelfCatalog.CalendarSkin(cycle);
                    return new Gift
                    {
                        Day = 7, BigChest = true, Coins = 200, Hints = 1, Undos = 1, ItemId = skin?.Id,
                        Label = "Big chest" + (skin != null ? " + " + skin.Name : ""),
                    };
                }
            }
        }

        public CalendarStatus Calendar()
        {
            bool paused = TimeRewardsPaused;
            return new CalendarStatus
            {
                Paused = paused,
                CanClaim = !paused && Save.CalendarLastClaim != Iso(Today),
                Next = GiftFor(Save.CalendarNextGift, Save.CalendarCycle),
                Cycle = Save.CalendarCycle,
            };
        }

        /// <summary>Claims today's gift. A missed day just waits at the same gift; nothing resets (§9).</summary>
        public Gift ClaimGift()
        {
            CalendarStatus status = Calendar();
            if (!status.CanClaim) return null;
            Gift gift = status.Next;
            Save.Coins += gift.Coins;
            Save.Hints += gift.Hints;
            Save.Undos += gift.Undos;
            if (gift.ItemId != null && !Save.Shelf.Contains(gift.ItemId)) Save.Shelf.Add(gift.ItemId);
            Save.CalendarLastClaim = Iso(Today);
            Save.CalendarNextGift++;
            if (Save.CalendarNextGift > 7)
            {
                Save.CalendarNextGift = 1;
                Save.CalendarCycle++;
            }
            Persist();
            return gift;
        }

        // ------------------------------------------------------------------ Weekend Tower (§9)

        public TowerStatus Tower()
        {
            DateTime today = Today;
            bool weekend = today.DayOfWeek == DayOfWeek.Saturday || today.DayOfWeek == DayOfWeek.Sunday;
            DateTime saturday = today.AddDays(-(((int)today.DayOfWeek + 1) % 7)); // most recent Saturday (today if Saturday)
            if (!weekend) saturday = saturday.AddDays(7);                           // next one when closed
            string weekId = IsoWeekId(saturday);
            int weeks = (int)Math.Floor((saturday - LaunchDate.Date).TotalDays / 7.0);
            int tower = ((weeks % DifficultyPlan.TowerCount) + DifficultyPlan.TowerCount) % DifficultyPlan.TowerCount + 1;
            return new TowerStatus
            {
                Open = weekend,
                Tower = tower,
                WeekId = weekId,
                Reached = Save.TowerWeekId == weekId ? Save.TowerReached : 0,
                NextOpen = saturday,
            };
        }

        /// <summary>The next tower level was won (1-10). Pays 15 coins, and 200 + the tower skin at the top.</summary>
        public TowerReward OnTowerLevelWon(int level)
        {
            TowerStatus status = Tower();
            if (!status.Open || level != status.Reached + 1) return null;
            if (Save.TowerWeekId != status.WeekId)
            {
                Save.TowerWeekId = status.WeekId;
                Save.TowerReached = 0;
            }
            Save.TowerReached = level;
            var reward = new TowerReward { Coins = Economy.TowerLevelReward };
            if (level == DifficultyPlan.LevelsPerTower)
            {
                reward.Completed = true;
                reward.Coins += Economy.TowerCompleteReward;
                string skin = ShelfCatalog.TowerSkin(status.Tower).Id;
                if (!Save.Shelf.Contains(skin))
                {
                    Save.Shelf.Add(skin);
                    reward.ItemId = skin;
                }
            }
            Save.Coins += reward.Coins;
            Persist();
            return reward;
        }

        // ------------------------------------------------------------------ settings

        public void SetSettings(bool sound, bool music, bool vibration, bool notifications, bool symbols)
        {
            Save.Sound = sound;
            Save.Music = music;
            Save.Vibration = vibration;
            Save.Notifications = notifications;
            Save.SymbolsAlwaysOn = symbols;
            Persist();
        }

        // ------------------------------------------------------------------ helpers

        private void Persist() => _persist(Save);

        public static string Iso(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        private static DateTime Parse(string iso) =>
            DateTime.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime d) ? d : DateTime.MinValue;

        /// <summary>ISO-8601 week id such as "2026-W47" (week of the Thursday in the same Monday-Sunday week).</summary>
        public static string IsoWeekId(DateTime date)
        {
            int dayFromMonday = ((int)date.DayOfWeek + 6) % 7;
            DateTime thursday = date.Date.AddDays(3 - dayFromMonday);
            int week = (thursday.DayOfYear - 1) / 7 + 1;
            return thursday.Year.ToString(CultureInfo.InvariantCulture) + "-W" + week.ToString("D2", CultureInfo.InvariantCulture);
        }
    }
}
