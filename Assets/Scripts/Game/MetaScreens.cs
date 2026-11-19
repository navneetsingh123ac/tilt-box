using System;
using System.Collections.Generic;
using System.Linq;
using TiltBox.Core;
using TiltBox.Meta;
using UnityEngine;
using UnityEngine.UI;

namespace TiltBox.Game
{
    /// <summary>
    /// Home, shop/shelf, gift calendar, settings, chest/reward popups and the daily result (GDD §12). Reads and
    /// changes the meta game only through <see cref="MetaGame"/>; starting levels is left to the callbacks.
    /// Placeholder look, built with <see cref="UiKit"/>.
    /// </summary>
    public sealed class MetaScreens : MonoBehaviour
    {
        public Action PlayCampaign;
        public Action PlayDaily;
        public Action PlayTower;

        /// <summary>Returns "Level 23" for the home button.</summary>
        public Func<string> CampaignLabel = () => "Play";
        public Func<bool> DailyAvailable = () => true;
        public Func<bool> TowerAvailable = () => true;

        private MetaGame _meta;
        private RectTransform _root, _home, _shop, _popupLayer;
        private Text _homeCoins, _shopCoins, _dailyText, _towerText, _giftText, _shelfText, _teaser;
        private Button _play, _dailyButton, _towerButton, _giftButton;
        private RectTransform _shopList;
        private ShelfItemType? _shopTab = ShelfItemType.Skin;

        public bool HomeVisible => _home != null && _home.gameObject.activeSelf;

        public void Build(MetaGame meta)
        {
            _meta = meta;
            if (_root != null) Destroy(_root.gameObject); // rebuilding (e.g. a temporary save for dev tools)
            _root = UiKit.Canvas(transform, "MetaUI", 10);
            BuildHome();
            BuildShop();
            _popupLayer = UiKit.Group(_root, "Popups", Vector2.zero, Vector2.one);
        }

        // ------------------------------------------------------------------ home

        private void BuildHome()
        {
            _home = UiKit.Panel(_root, "Home", Vector2.zero, Vector2.one, new Color32(0xF3, 0xEB, 0xDD, 0xFF));
            UiKit.Label(_home, "Title", "Tilt Box", 120, FontStyle.Bold, new Vector2(0, 0.84f), new Vector2(1, 0.95f));
            UiKit.Button(_home, "Settings", "Settings", new Vector2(0.03f, 0.95f), new Vector2(0.3f, 0.99f), ShowSettings, fontSize: 36);
            _homeCoins = UiKit.CoinCounter(_home, new Vector2(0.72f, 0.95f), new Vector2(0.97f, 0.99f));

            _play = UiKit.Button(_home, "Play", "Level 1", new Vector2(0.12f, 0.68f), new Vector2(0.88f, 0.81f), () => PlayCampaign?.Invoke(),
                UiKit.Good, Color.white, 80);

            RectTransform daily = UiKit.Panel(_home, "DailyCard", new Vector2(0.06f, 0.52f), new Vector2(0.94f, 0.65f), UiKit.Card);
            _dailyText = UiKit.Label(daily, "Text", "", 42, FontStyle.Normal, new Vector2(0.04f, 0f), new Vector2(0.64f, 1f), TextAnchor.MiddleLeft);
            _dailyButton = UiKit.Button(daily, "Play", "Play", new Vector2(0.66f, 0.2f), new Vector2(0.96f, 0.8f), () => PlayDaily?.Invoke(),
                UiKit.Accent, Color.white);

            RectTransform tower = UiKit.Panel(_home, "TowerCard", new Vector2(0.06f, 0.37f), new Vector2(0.94f, 0.5f), UiKit.Card);
            _towerText = UiKit.Label(tower, "Text", "", 42, FontStyle.Normal, new Vector2(0.04f, 0f), new Vector2(0.64f, 1f), TextAnchor.MiddleLeft);
            _towerButton = UiKit.Button(tower, "Play", "Climb", new Vector2(0.66f, 0.2f), new Vector2(0.96f, 0.8f), () => PlayTower?.Invoke(),
                UiKit.Accent, Color.white);

            RectTransform gift = UiKit.Panel(_home, "GiftCard", new Vector2(0.06f, 0.22f), new Vector2(0.94f, 0.35f), UiKit.Card);
            _giftText = UiKit.Label(gift, "Text", "", 42, FontStyle.Normal, new Vector2(0.04f, 0f), new Vector2(0.64f, 1f), TextAnchor.MiddleLeft);
            _giftButton = UiKit.Button(gift, "Open", "Open", new Vector2(0.66f, 0.2f), new Vector2(0.96f, 0.8f), ShowCalendar);

            RectTransform shelf = UiKit.Panel(_home, "ShelfCard", new Vector2(0.06f, 0.09f), new Vector2(0.94f, 0.2f), UiKit.Card);
            _shelfText = UiKit.Label(shelf, "Text", "", 42, FontStyle.Normal, new Vector2(0.04f, 0f), new Vector2(0.64f, 1f), TextAnchor.MiddleLeft);
            UiKit.Button(shelf, "Shop", "Shop", new Vector2(0.66f, 0.2f), new Vector2(0.96f, 0.8f), () => ShowShop(true));

            _teaser = UiKit.Label(_home, "Teaser", "", 34, FontStyle.Italic, new Vector2(0.05f, 0.01f), new Vector2(0.95f, 0.08f));
            _teaser.color = UiKit.Muted;
        }

        public void ShowHome(bool show)
        {
            _home.gameObject.SetActive(show);
            if (show) RefreshHome();
        }

        public void RefreshHome()
        {
            _meta.Touch();
            _homeCoins.text = _meta.Coins.ToString();
            UiKit.SetText(_play, CampaignLabel());

            DailyStatus d = _meta.Daily();
            bool dailyReady = DailyAvailable();
            _dailyText.text = "<b>Daily Puzzle #" + d.Number + "</b>\n" +
                (!d.Unlocked ? "Unlocks after level " + Economy.DailyUnlockLevel
                 : !dailyReady ? "Coming soon"
                 : d.RewardsPaused ? "Rewards paused (clock changed)"
                 : d.SolvedToday ? "Solved! Days solved: " + d.DaysSolved
                 : "+" + Economy.DailyReward + " coins · solved " + d.DaysSolved + " days");
            _dailyButton.interactable = d.Unlocked && dailyReady;
            UiKit.SetText(_dailyButton, d.SolvedToday ? "Replay" : "Play");

            TowerStatus t = _meta.Tower();
            bool towerReady = TowerAvailable();
            _towerText.text = "<b>Weekend Tower " + t.Tower + "</b>\n" +
                (!t.Open ? "Opens Saturday " + t.NextOpen.ToString("d MMM")
                 : !towerReady ? "Coming soon"
                 : t.Reached >= DifficultyPlan.LevelsPerTower ? "Top reached! " + DifficultyPlan.TowerRules(t.Tower).Describe()
                 : "Floor " + t.Reached + "/" + DifficultyPlan.LevelsPerTower + " · " + DifficultyPlan.TowerRules(t.Tower).Describe());
            _towerButton.interactable = t.Open && towerReady && t.Reached < DifficultyPlan.LevelsPerTower;

            CalendarStatus c = _meta.Calendar();
            _giftText.text = "<b>Gift calendar</b>\n" + (c.CanClaim ? "Day " + c.Next.Day + " gift is ready!" : c.Paused ? "Paused (clock changed)" : "Next gift tomorrow");
            UiKit.SetText(_giftButton, c.CanClaim ? "Claim" : "View");

            int owned = ShelfCatalog.LaunchShelf.Count(i => _meta.Owns(i.Id));
            _shelfText.text = "<b>Shelf</b>\n" + owned + " / 40 collected";

            Gift tomorrowGift = c.CanClaim ? null : c.Next;
            _teaser.text = "Tomorrow: Daily #" + (d.Number + 1) + (tomorrowGift != null ? " · gift: " + tomorrowGift.Label : "");
        }

        // ------------------------------------------------------------------ shop & shelf

        private void BuildShop()
        {
            _shop = UiKit.Panel(_root, "Shop", Vector2.zero, Vector2.one, new Color32(0xF3, 0xEB, 0xDD, 0xFF));
            UiKit.Label(_shop, "Title", "Shop & Shelf", 80, FontStyle.Bold, new Vector2(0, 0.88f), new Vector2(1, 0.95f));
            UiKit.Button(_shop, "Back", "Back", new Vector2(0.03f, 0.95f), new Vector2(0.25f, 0.99f), () => ShowShop(false), fontSize: 38);
            _shopCoins = UiKit.CoinCounter(_shop, new Vector2(0.72f, 0.95f), new Vector2(0.97f, 0.99f));
            string[] tabs = { "Skins", "Backgrounds", "Figurines", "Tokens" };
            for (int i = 0; i < tabs.Length; i++)
            {
                ShelfItemType? type = i < 3 ? (ShelfItemType)i : (ShelfItemType?)null;
                UiKit.Button(_shop, "Tab" + i, tabs[i], new Vector2(0.02f + i * 0.245f, 0.81f), new Vector2(0.245f + i * 0.245f, 0.86f),
                    () => { _shopTab = type; RefreshShop(); }, fontSize: 34);
            }
            _shopList = UiKit.Group(_shop, "List", new Vector2(0.03f, 0.02f), new Vector2(0.97f, 0.79f));
            _shop.gameObject.SetActive(false);
        }

        public void ShowShop(bool show)
        {
            _shop.gameObject.SetActive(show);
            if (show) RefreshShop();
            else RefreshHome();
        }

        private void RefreshShop()
        {
            foreach (Transform child in _shopList) Destroy(child.gameObject);
            _shopCoins.text = _meta.Coins.ToString();

            if (_shopTab == null)
            {
                Row(0, "Hint token  (you have " + _meta.Hints + ")", Economy.HintPrice + " coins", _meta.Coins >= Economy.HintPrice,
                    () => { _meta.BuyHint(); RefreshShop(); });
                Row(1, "Undo token  (you have " + _meta.Undos + ")", Economy.UndoPrice + " coins", _meta.Coins >= Economy.UndoPrice,
                    () => { _meta.BuyUndo(); RefreshShop(); });
                return;
            }

            List<ShelfItem> items = ShelfCatalog.All
                .Where(i => i.Type == _shopTab.Value && (i.Source == ShelfSource.Shop || i.Source == ShelfSource.WorldChest || _meta.Owns(i.Id)))
                .ToList();
            for (int k = 0; k < items.Count; k++)
            {
                ShelfItem item = items[k];
                bool owned = _meta.Owns(item.Id);
                bool equipped = item.Id == _meta.Save.EquippedSkin || item.Id == _meta.Save.EquippedBackground;
                string action;
                bool enabled;
                Action onClick;
                if (owned && item.Type == ShelfItemType.Figurine) { action = "On shelf"; enabled = false; onClick = () => { }; }
                else if (owned) { action = equipped ? "Equipped" : "Equip"; enabled = !equipped; onClick = () => { _meta.Equip(item.Id); RefreshShop(); }; }
                else if (item.Source == ShelfSource.WorldChest) { action = "World " + item.Number + " chest"; enabled = false; onClick = () => { }; }
                else { action = item.Price + " coins"; enabled = _meta.CanBuy(item); onClick = () => { _meta.Buy(item.Id); RefreshShop(); }; }
                Row(k, item.Name, action, enabled, onClick, owned);
            }
        }

        /// <summary>One shop row; two columns of up to 12 rows each.</summary>
        private void Row(int index, string name, string action, bool enabled, Action onClick, bool owned = false)
        {
            int col = index / 12, row = index % 12;
            float x0 = col * 0.5f, y1 = 1f - row / 12f;
            RectTransform cell = UiKit.Panel(_shopList, "Row" + index, new Vector2(x0 + 0.005f, y1 - 1f / 12f + 0.004f), new Vector2(x0 + 0.495f, y1 - 0.004f),
                owned ? new Color(0.88f, 0.96f, 0.88f, 1f) : UiKit.Card);
            UiKit.Label(cell, "Name", name, 30, FontStyle.Bold, new Vector2(0.04f, 0f), new Vector2(0.56f, 1f), TextAnchor.MiddleLeft);
            Button b = UiKit.Button(cell, "Action", action, new Vector2(0.58f, 0.12f), new Vector2(0.97f, 0.88f), onClick, fontSize: 28);
            b.interactable = enabled;
        }

        // ------------------------------------------------------------------ calendar

        private void ShowCalendar()
        {
            RectTransform card = Popup("Gift calendar", out RectTransform layer);
            CalendarStatus c = _meta.Calendar();
            for (int day = 1; day <= 7; day++)
            {
                int row = day <= 4 ? 0 : 1, col = day <= 4 ? day - 1 : day - 5;
                float x0 = 0.04f + col * 0.235f, y1 = row == 0 ? 0.82f : 0.52f;
                bool done = day < c.Next.Day;
                bool current = day == c.Next.Day;
                Color fill = done ? new Color(0.85f, 0.95f, 0.85f) : current ? new Color(1f, 0.93f, 0.7f) : UiKit.ButtonFill;
                RectTransform box = UiKit.Panel(card, "Day" + day, new Vector2(x0, y1 - 0.27f), new Vector2(x0 + 0.215f, y1), fill);
                UiKit.Label(box, "Text", "Day " + day + "\n" + MetaGame.GiftFor(day, c.Cycle).Label, 28, current ? FontStyle.Bold : FontStyle.Normal,
                    new Vector2(0.04f, 0.04f), new Vector2(0.96f, 0.96f));
            }
            string status = c.CanClaim ? "Day " + c.Next.Day + " is ready." : c.Paused ? "Paused until the date catches up." : "Come back tomorrow. Missed days just wait.";
            UiKit.Label(card, "Status", status, 36, FontStyle.Italic, new Vector2(0.04f, 0.13f), new Vector2(0.96f, 0.23f));
            Button claim = UiKit.Button(card, "Claim", "Claim", new Vector2(0.08f, 0.02f), new Vector2(0.48f, 0.12f), () =>
            {
                Gift g = _meta.ClaimGift();
                Close(layer);
                RefreshHome();
                if (g != null) ShowMessage("Day " + g.Day + " gift", "You got " + g.Label + "!" + (g.ItemId != null ? "\nNew box skin on your shelf." : ""));
            }, UiKit.Good, Color.white);
            claim.interactable = c.CanClaim;
            UiKit.Button(card, "Close", "Close", new Vector2(0.52f, 0.02f), new Vector2(0.92f, 0.12f), () => Close(layer));
        }

        // ------------------------------------------------------------------ settings

        private void ShowSettings()
        {
            RectTransform card = Popup("Settings", out RectTransform layer);
            SaveData s = _meta.Save;
            var labels = new[] { "Sound", "Music", "Vibration", "Notifications", "Colour symbols always on" };
            var values = new[] { s.Sound, s.Music, s.Vibration, s.Notifications, s.SymbolsAlwaysOn };
            for (int i = 0; i < labels.Length; i++)
            {
                int k = i;
                float y1 = 0.84f - i * 0.12f;
                Button b = null;
                b = UiKit.Button(card, labels[i], labels[i] + ": " + (values[i] ? "On" : "Off"), new Vector2(0.08f, y1 - 0.1f), new Vector2(0.92f, y1), () =>
                {
                    values[k] = !values[k];
                    _meta.SetSettings(values[0], values[1], values[2], values[3], values[4]);
                    UiKit.SetText(b, labels[k] + ": " + (values[k] ? "On" : "Off"));
                }, fontSize: 40);
            }
            UiKit.Label(card, "Note", "Privacy and Restore purchase arrive with ads and the store (week 5).", 30, FontStyle.Italic,
                new Vector2(0.06f, 0.14f), new Vector2(0.94f, 0.22f));
            UiKit.Button(card, "Close", "Close", new Vector2(0.3f, 0.02f), new Vector2(0.7f, 0.12f), () => { Close(layer); RefreshHome(); });
        }

        // ------------------------------------------------------------------ popups

        /// <summary>Generic message with OK (chest contents, world complete, tower top...).</summary>
        public void ShowMessage(string title, string body, Action onClose = null)
        {
            RectTransform card = Popup(title, out RectTransform layer, small: true);
            UiKit.Label(card, "Body", body, 44, FontStyle.Normal, new Vector2(0.06f, 0.3f), new Vector2(0.94f, 0.78f));
            UiKit.Button(card, "OK", "OK", new Vector2(0.3f, 0.06f), new Vector2(0.7f, 0.24f), () =>
            {
                Close(layer);
                onClose?.Invoke();
            }, UiKit.Good, Color.white);
        }

        public static string Describe(ChestResult chest)
        {
            var parts = new List<string> { chest.Coins + " coins" };
            if (chest.Hints > 0) parts.Add(chest.Hints + (chest.Hints == 1 ? " hint" : " hints"));
            if (chest.Undos > 0) parts.Add(chest.Undos + (chest.Undos == 1 ? " undo" : " undos"));
            if (chest.ItemId != null) parts.Add(ShelfCatalog.Find(chest.ItemId)?.Name + " figurine");
            return string.Join(", ", parts);
        }

        /// <summary>Daily result (§9): moves, stars, coins and the share text (copied to the clipboard).</summary>
        public void ShowDailyResult(int number, int moves, int stars, DailyResult result, Action onHome)
        {
            RectTransform card = Popup("Daily #" + number + " solved!", out RectTransform layer, small: true);
            string body = moves + " moves · " + stars + (stars == 1 ? " star" : " stars") +
                          (result.Coins > 0 ? "\n+" + result.Coins + " coins" : "\nAlready rewarded today") +
                          (result.NewBadge.HasValue ? "\nBadge: " + result.NewBadge + " days solved!" : "");
            UiKit.Label(card, "Body", body, 44, FontStyle.Normal, new Vector2(0.06f, 0.32f), new Vector2(0.94f, 0.78f));
            Button share = null;
            share = UiKit.Button(card, "Share", "Copy share text", new Vector2(0.06f, 0.06f), new Vector2(0.6f, 0.24f), () =>
            {
                GUIUtility.systemCopyBuffer = result.ShareText;
                UiKit.SetText(share, "Copied!");
            }, UiKit.Accent, Color.white, 40);
            UiKit.Button(card, "Home", "Home", new Vector2(0.64f, 0.06f), new Vector2(0.94f, 0.24f), () =>
            {
                Close(layer);
                onHome?.Invoke();
            });
        }

        private RectTransform Popup(string title, out RectTransform layer, bool small = false)
        {
            layer = UiKit.Blocker(_popupLayer, "Popup_" + title);
            RectTransform card = UiKit.Panel(layer, "Card", small ? new Vector2(0.07f, 0.33f) : new Vector2(0.04f, 0.18f),
                small ? new Vector2(0.93f, 0.67f) : new Vector2(0.96f, 0.82f), UiKit.Card);
            UiKit.Label(card, "Title", title, 60, FontStyle.Bold, new Vector2(0, small ? 0.8f : 0.88f), new Vector2(1, 0.99f));
            return card;
        }

        /// <summary>True while any popup is showing. Counts active children: Destroy is deferred to the end of the frame.</summary>
        public bool PopupOpen
        {
            get
            {
                if (_popupLayer == null) return false;
                foreach (Transform child in _popupLayer) if (child.gameObject.activeSelf) return true;
                return false;
            }
        }

        private static void Close(RectTransform layer)
        {
            layer.gameObject.SetActive(false);
            Destroy(layer.gameObject);
        }
    }
}
