using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TiltBox.Core;
using TiltBox.Meta;

namespace TiltBox.Core.Tests
{
    internal sealed class FakeClock : IClock
    {
        public DateTime Now { get; set; }
        public FakeClock(DateTime now) => Now = now;
        public void AddDays(double days) => Now = Now.AddDays(days);
    }

    internal sealed class MemoryStorage : ISaveStorage
    {
        public readonly Dictionary<string, string> Files = new Dictionary<string, string>();
        public int Writes;
        public string Read(string name) => Files.TryGetValue(name, out string t) ? t : null;
        public void Write(string name, string text) { Files[name] = text; Writes++; }
    }

    public class MetaTests
    {
        // 2026-11-19 is a Thursday; 2026-11-21 a Saturday.
        private static readonly DateTime Thursday = new DateTime(2026, 11, 19, 9, 0, 0);
        private static readonly DateTime Saturday = new DateTime(2026, 11, 21, 9, 0, 0);

        private static MetaGame NewGame(out FakeClock clock, out MemoryStorage storage, DateTime? now = null)
        {
            clock = new FakeClock(now ?? Thursday);
            storage = new MemoryStorage();
            var store = new SaveStore(storage);
            return new MetaGame(store.Load(), clock, store.Save, seed: 42);
        }

        private static void WinLevels(MetaGame g, int count, int stars = 3)
        {
            for (int i = 0; i < count; i++)
            {
                int index = g.Save.Level;
                g.OnCampaignWin(index, "w" + (index / 30 + 1) + "_" + (index % 30 + 1).ToString("D2"), false, stars);
            }
        }

        // ---------------------------------------------------------------- save

        [Test]
        public void Save_RoundTripsEveryField()
        {
            var s = new SaveData
            {
                Level = 23, Coins = 1240, Hints = 3, Undos = 2, ChestProgress = 3, NoForcedAds = true,
                EquippedSkin = "skin_wood", EquippedBackground = "bg_toyroom", DailyLastSolved = "2026-11-17", DaysSolved = 12,
                CalendarNextGift = 4, CalendarLastClaim = "2026-11-16", CalendarCycle = 2, TowerWeekId = "2026-W47", TowerReached = 6,
                Sound = false, Music = true, Vibration = false, Notifications = true, SymbolsAlwaysOn = false,
                Wins = 140, Losses = 22, AdsWatched = 31, FirstOpen = "2026-11-01", LastSeenDate = "2026-11-19",
            };
            s.Stars["w1_01"] = 3;
            s.Stars["w1_02"] = 2;
            s.Skipped.Add("w2_07");
            s.Shelf.Add("fig_w1");
            s.Shelf.Add("skin_wood");
            string json = SaveJson.Write(s);
            Assert.AreEqual(json, SaveJson.Write(SaveJson.Read(json)));
            SaveData back = SaveJson.Read(json);
            Assert.AreEqual(23, back.Level);
            Assert.AreEqual(2, back.Stars["w1_02"]);
            CollectionAssert.AreEqual(new[] { "fig_w1", "skin_wood" }, back.Shelf);
            Assert.IsFalse(back.Sound);
        }

        [Test]
        public void Store_FallsBackToBackupWhenMainIsDamaged()
        {
            var storage = new MemoryStorage();
            var store = new SaveStore(storage);
            store.Save(new SaveData { Coins = 77 });
            storage.Files[SaveStore.MainName] = "{ \"v\": 1, \"coins\": 9"; // cut off mid-write
            SaveData loaded = store.Load();
            Assert.AreEqual(77, loaded.Coins);
            Assert.AreEqual("backup", store.LastLoadSource);
        }

        [Test]
        public void Store_StartsFreshWhenBothCopiesAreDamagedOrMissing()
        {
            var storage = new MemoryStorage();
            var store = new SaveStore(storage);
            store.Load();
            Assert.AreEqual("new", store.LastLoadSource, "nothing saved yet");
            storage.Files[SaveStore.MainName] = "garbage";
            storage.Files[SaveStore.BackupName] = "{\"v\": 99}";
            SaveData s = store.Load();
            Assert.AreEqual("new", store.LastLoadSource);
            Assert.AreEqual(Economy.StartHints, s.Hints);
        }

        [Test]
        public void EveryChangeIsSavedImmediately()
        {
            MetaGame g = NewGame(out _, out MemoryStorage storage);
            int before = storage.Writes;
            g.OnCampaignWin(0, "w1_01", false, 3);
            Assert.That(storage.Writes, Is.GreaterThan(before), "a win saves");
            before = storage.Writes;
            g.BuyUndo();
            Assert.AreEqual(before, storage.Writes, "not enough coins: nothing changed, nothing saved");
        }

        // ---------------------------------------------------------------- coins and chests

        [TestCase(1, false, 10)]
        [TestCase(2, false, 15)]
        [TestCase(3, false, 20)]
        [TestCase(3, true, 40)]
        public void WinCoins(int stars, bool hard, int expected) => Assert.AreEqual(expected, Economy.WinCoins(stars, hard));

        [Test]
        public void Replays_PayOnlyTheFirstThreeStarBonus()
        {
            MetaGame g = NewGame(out _, out _);
            Assert.AreEqual(10, g.OnCampaignWin(0, "w1_01", false, 1).Coins);
            Assert.AreEqual(0, g.OnCampaignWin(0, "w1_01", false, 2).Coins, "replay: no farming");
            WinReward third = g.OnCampaignWin(0, "w1_01", false, 3);
            Assert.AreEqual(10, third.Coins);
            Assert.IsTrue(third.FirstThreeStars);
            Assert.AreEqual(0, g.OnCampaignWin(0, "w1_01", false, 3).Coins, "only the first time");
            Assert.AreEqual(3, g.StarsFor("w1_01"));
            Assert.AreEqual(1, g.Save.Level, "replays don't advance progress");
        }

        [Test]
        public void SmallChest_EveryFiveNewLevels()
        {
            MetaGame g = NewGame(out _, out _);
            for (int i = 0; i < 4; i++)
            {
                WinReward r = g.OnCampaignWin(i, "w1_0" + (i + 1), false, 3);
                Assert.IsNull(r.SmallChest);
                Assert.AreEqual(4 - i, r.LevelsToChest);
            }
            WinReward fifth = g.OnCampaignWin(4, "w1_05", false, 3);
            Assert.IsNotNull(fifth.SmallChest);
            Assert.That(fifth.SmallChest.Coins, Is.InRange(40, 80));
            Assert.AreEqual(0, fifth.SmallChest.Coins % 5);
            Assert.That(fifth.SmallChest.Hints + fifth.SmallChest.Undos, Is.LessThanOrEqualTo(1));
            Assert.AreEqual(5, fifth.LevelsToChest);
        }

        [Test]
        public void SmallChest_TokenChanceIsAboutHalf()
        {
            int withToken = 0;
            for (int seed = 1; seed <= 400; seed++)
            {
                var g = new MetaGame(new SaveData(), new FakeClock(Thursday), null, seed);
                WinReward r = null;
                for (int i = 0; i < 5; i++) r = g.OnCampaignWin(i, "w1_0" + (i + 1), false, 1);
                if (r.SmallChest.Hints + r.SmallChest.Undos > 0) withToken++;
            }
            Assert.That(withToken, Is.InRange(160, 240));
        }

        [Test]
        public void WorldChest_AfterLevel30_GivesTheWorldFigurine()
        {
            MetaGame g = NewGame(out _, out _);
            WinLevels(g, 29);
            int coins = g.Coins, hints = g.Hints;
            WinReward r = g.OnCampaignWin(29, "w1_30", true, 3);
            Assert.IsNotNull(r.WorldChest);
            Assert.AreEqual("fig_w1", r.WorldChest.ItemId);
            Assert.IsTrue(g.Owns("fig_w1"));
            Assert.That(g.Hints, Is.GreaterThanOrEqualTo(hints + 2));
            Assert.That(g.Coins - coins, Is.GreaterThanOrEqualTo(40 + 300));
        }

        [Test]
        public void DoubleChest_OnlyOnce_AndNotTheItem()
        {
            MetaGame g = NewGame(out _, out _);
            WinLevels(g, 29);
            WinReward r = g.OnCampaignWin(29, "w1_30", false, 3);
            int coins = g.Coins;
            Assert.IsTrue(g.DoubleChest(r.WorldChest));
            Assert.AreEqual(coins + 300, g.Coins);
            Assert.IsFalse(g.DoubleChest(r.WorldChest));
            Assert.AreEqual(1, g.Save.Shelf.Count(id => id == "fig_w1"));
        }

        // ---------------------------------------------------------------- tokens and shop

        [Test]
        public void Tokens_StartAtThree_FreeUndoFirst()
        {
            MetaGame g = NewGame(out _, out _);
            Assert.AreEqual(3, g.Hints);
            Assert.AreEqual(3, g.Undos);
            int free = Economy.FreeUndosPerLevel;
            Assert.IsTrue(g.TryUseUndo(ref free));
            Assert.AreEqual(3, g.Undos, "the free undo costs no token");
            Assert.IsTrue(g.TryUseUndo(ref free));
            Assert.AreEqual(2, g.Undos);
            g.Save.Undos = 0;
            Assert.IsFalse(g.TryUseUndo(ref free));
            Assert.IsTrue(g.TryUseHint());
            Assert.AreEqual(2, g.Hints);
        }

        [Test]
        public void Shop_TokenPrices()
        {
            MetaGame g = NewGame(out _, out _);
            g.Save.Coins = 160;
            Assert.IsTrue(g.BuyHint());
            Assert.AreEqual(60, g.Coins);
            Assert.IsTrue(g.BuyUndo());
            Assert.AreEqual(10, g.Coins);
            Assert.IsFalse(g.BuyUndo());
            Assert.AreEqual(4, g.Hints);
            Assert.AreEqual(4, g.Undos);
        }

        [Test]
        public void Shelf_HasTheFortyLaunchItemsFromTheGdd()
        {
            var launch = ShelfCatalog.LaunchShelf.ToList();
            Assert.AreEqual(40, launch.Count);
            Assert.AreEqual(launch.Count, launch.Select(i => i.Id).Distinct().Count());
            var skins = launch.Where(i => i.Type == ShelfItemType.Skin).ToList();
            var bgs = launch.Where(i => i.Type == ShelfItemType.Background).ToList();
            var figs = launch.Where(i => i.Type == ShelfItemType.Figurine).ToList();
            Assert.AreEqual(12, skins.Count);
            Assert.AreEqual(8, bgs.Count);
            Assert.AreEqual(20, figs.Count);
            Assert.That(skins.Select(i => i.Price), Is.All.InRange(150, 400));
            Assert.AreEqual(150, skins.Min(i => i.Price));
            Assert.AreEqual(400, skins.Max(i => i.Price));
            Assert.That(bgs.Select(i => i.Price), Is.All.InRange(300, 800));
            Assert.That(figs.Where(i => i.Source == ShelfSource.Shop).Select(i => i.Price), Is.All.InRange(200, 1200));
            Assert.AreEqual(10, figs.Count(i => i.Source == ShelfSource.WorldChest));
        }

        [Test]
        public void Shelf_BuyAndEquip()
        {
            MetaGame g = NewGame(out _, out _);
            g.Save.Coins = 1000;
            Assert.IsTrue(g.Buy("skin_wood"));
            Assert.AreEqual(850, g.Coins);
            Assert.IsFalse(g.Buy("skin_wood"), "already owned");
            Assert.IsFalse(g.Buy("fig_w1"), "world figurines can't be bought");
            Assert.IsFalse(g.Buy("skin_tower1"), "tower skins can't be bought");
            Assert.IsTrue(g.Equip("skin_wood"));
            Assert.AreEqual("skin_wood", g.Save.EquippedSkin);
            Assert.IsFalse(g.Equip("skin_gold"), "not owned");
        }

        // ---------------------------------------------------------------- daily

        [Test]
        public void Daily_UnlocksAfterLevelTen_AndPaysOncePerDay()
        {
            MetaGame g = NewGame(out FakeClock clock, out _);
            Assert.IsFalse(g.Daily().Unlocked);
            WinLevels(g, 10);
            DailyStatus d = g.Daily();
            Assert.IsTrue(d.Unlocked);
            Assert.AreEqual(19, d.Number, "launch 2026-11-01, today 2026-11-19");
            Assert.AreEqual(19, d.PoolDay);
            var swipes = new[] { Direction.Right, Direction.Up, Direction.Left, Direction.Down, Direction.Right };
            DailyResult r = g.OnDailySolved(swipes, 3);
            Assert.AreEqual(30, r.Coins);
            Assert.AreEqual("Tilt Box #19 ➡️⬆️⬅️⬇️➡️ 5 moves ⭐⭐⭐", r.ShareText);
            Assert.AreEqual(0, g.OnDailySolved(swipes, 3).Coins, "second solve the same day pays nothing");
            Assert.IsTrue(g.Daily().SolvedToday);
            clock.AddDays(1);
            Assert.IsFalse(g.Daily().SolvedToday);
            Assert.AreEqual(30, g.OnDailySolved(swipes, 2).Coins);
            Assert.AreEqual(2, g.Daily().DaysSolved);
        }

        [Test]
        public void Daily_BadgeAtSevenDays_CounterNeverResets()
        {
            MetaGame g = NewGame(out FakeClock clock, out _);
            WinLevels(g, 10);
            DailyResult last = null;
            for (int day = 0; day < 7; day++)
            {
                last = g.OnDailySolved(new[] { Direction.Up }, 3);
                clock.AddDays(day == 3 ? 3 : 1); // a gap of missed days doesn't reset anything
            }
            Assert.AreEqual(7, last.NewBadge);
            Assert.AreEqual(7, g.Save.DaysSolved);
        }

        [Test]
        public void Daily_PoolWrapsAfter365Days()
        {
            MetaGame g = NewGame(out FakeClock clock, out _, MetaGame.LaunchDate.AddDays(365));
            Assert.AreEqual(366, g.Daily().Number);
            Assert.AreEqual(1, g.Daily().PoolDay);
        }

        [Test]
        public void ClockGoingBackMoreThanADay_PausesDailyAndCalendar()
        {
            MetaGame g = NewGame(out FakeClock clock, out _);
            WinLevels(g, 10);
            clock.AddDays(-3);
            Assert.IsTrue(g.Daily().RewardsPaused);
            Assert.AreEqual(0, g.OnDailySolved(new[] { Direction.Up }, 3).Coins);
            Assert.IsFalse(g.Calendar().CanClaim);
            clock.AddDays(2);
            Assert.IsFalse(g.Daily().RewardsPaused, "back within a day of the latest date seen");
        }

        // ---------------------------------------------------------------- calendar

        [Test]
        public void Calendar_OneGiftPerDay_MissedDaysWait_CycleGivesSkin()
        {
            MetaGame g = NewGame(out FakeClock clock, out _);
            Gift first = g.ClaimGift();
            Assert.AreEqual(1, first.Day);
            Assert.AreEqual(50, first.Coins);
            Assert.IsNull(g.ClaimGift(), "once per day");
            clock.AddDays(5); // missed four days
            Assert.AreEqual(2, g.ClaimGift().Day, "it waited at the same gift");
            for (int day = 3; day <= 7; day++)
            {
                clock.AddDays(1);
                Gift gift = g.ClaimGift();
                Assert.AreEqual(day, gift.Day);
                if (day == 7) Assert.AreEqual("skin_calendar1", gift.ItemId);
            }
            Assert.IsTrue(g.Owns("skin_calendar1"));
            clock.AddDays(1);
            Assert.AreEqual(1, g.Calendar().Next.Day, "starts again");
            Assert.AreEqual(2, g.Calendar().Cycle);
            Assert.AreEqual("skin_calendar2", MetaGame.GiftFor(7, 2).ItemId, "a new skin on the next day 7");
        }

        // ---------------------------------------------------------------- tower

        [Test]
        public void Tower_ClosedOnWeekdays_OpenOnWeekends()
        {
            MetaGame g = NewGame(out FakeClock clock, out _);
            TowerStatus weekday = g.Tower();
            Assert.IsFalse(weekday.Open);
            Assert.AreEqual(new DateTime(2026, 11, 21), weekday.NextOpen);
            Assert.IsNull(g.OnTowerLevelWon(1));
            clock.AddDays(2);
            Assert.IsTrue(g.Tower().Open);
            Assert.AreEqual("2026-W47", g.Tower().WeekId);
            clock.AddDays(1);
            Assert.IsTrue(g.Tower().Open, "Sunday");
            Assert.AreEqual("2026-W47", g.Tower().WeekId, "same weekend");
        }

        [Test]
        public void Tower_RotatesWeekly_AndProgressResetsEachWeekend()
        {
            MetaGame g = NewGame(out FakeClock clock, out _, Saturday);
            int towerA = g.Tower().Tower;
            Assert.IsNotNull(g.OnTowerLevelWon(1));
            Assert.IsNull(g.OnTowerLevelWon(3), "levels go in order");
            Assert.AreEqual(1, g.Tower().Reached);
            clock.AddDays(7);
            Assert.AreEqual(towerA % 8 + 1, g.Tower().Tower);
            Assert.AreEqual(0, g.Tower().Reached);
            clock.AddDays(7 * 7);
            Assert.AreEqual(towerA, g.Tower().Tower, "back after 8 weeks (§9: about 2 months)");
        }

        [Test]
        public void Tower_PaysPerLevel_AndTheTopGivesSkin()
        {
            MetaGame g = NewGame(out _, out _, Saturday);
            int coins = g.Coins;
            TowerReward last = null;
            for (int level = 1; level <= 10; level++) last = g.OnTowerLevelWon(level);
            Assert.IsTrue(last.Completed);
            Assert.AreEqual(coins + 10 * 15 + 200, g.Coins);
            Assert.IsTrue(g.Owns(ShelfCatalog.TowerSkin(g.Tower().Tower).Id));
        }

        [Test]
        public void IsoWeekIds()
        {
            Assert.AreEqual("2026-W47", MetaGame.IsoWeekId(new DateTime(2026, 11, 21)));
            Assert.AreEqual("2026-W01", MetaGame.IsoWeekId(new DateTime(2026, 1, 1)));
            Assert.AreEqual("2026-W53", MetaGame.IsoWeekId(new DateTime(2027, 1, 2)));
        }

        // ---------------------------------------------------------------- GDD week-4 bar

        [Test]
        public void AFullDayOfPlay_WorksEndToEnd_AndSurvivesARestart()
        {
            var clock = new FakeClock(Saturday);
            var storage = new MemoryStorage();
            var store = new SaveStore(storage);
            var game = new MetaGame(store.Load(), clock, store.Save, seed: 7);

            // Morning: gift, then 12 campaign levels with a couple of chests.
            Assert.AreEqual(50, game.ClaimGift().Coins);
            int chests = 0;
            for (int i = 0; i < 12; i++)
            {
                WinReward r = game.OnCampaignWin(i, "w1_" + (i + 1).ToString("D2"), i == 9, i % 3 == 0 ? 2 : 3);
                if (r.SmallChest != null) chests++;
            }
            game.OnLevelLost();
            Assert.AreEqual(2, chests);
            int free = Economy.FreeUndosPerLevel;
            game.TryUseUndo(ref free);
            game.TryUseUndo(ref free);
            game.TryUseHint();

            // Afternoon: daily puzzle (unlocked after level 10) and three tower levels.
            DailyResult daily = game.OnDailySolved(new[] { Direction.Left, Direction.Down }, 3);
            Assert.AreEqual(30, daily.Coins);
            for (int level = 1; level <= 3; level++) Assert.IsNotNull(game.OnTowerLevelWon(level));

            // Evening: buy and equip a box skin.
            Assert.IsTrue(game.Buy("skin_wood"));
            Assert.IsTrue(game.Equip("skin_wood"));
            string before = SaveJson.Write(game.Save);

            // "Restart": a fresh MetaGame from the stored files sees exactly the same state.
            var restarted = new MetaGame(new SaveStore(storage).Load(), clock, store.Save, seed: 99);
            Assert.AreEqual(before, SaveJson.Write(restarted.Save));
            Assert.AreEqual(12, restarted.Save.Level);
            Assert.IsTrue(restarted.Daily().SolvedToday);
            Assert.AreEqual(3, restarted.Tower().Reached);
            Assert.IsFalse(restarted.Calendar().CanClaim);
            Assert.AreEqual("skin_wood", restarted.Save.EquippedSkin);
            Assert.AreEqual(1, restarted.Save.Losses);
            Assert.AreEqual(12, restarted.Save.Wins);

            // Next morning the daily and the gift are available again.
            clock.AddDays(1);
            Assert.IsFalse(restarted.Daily().SolvedToday);
            Assert.IsTrue(restarted.Calendar().CanClaim);
            Assert.AreEqual(2, restarted.Calendar().Next.Day);
        }
    }
}
