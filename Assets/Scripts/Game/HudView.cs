using System;
using UnityEngine;
using UnityEngine.UI;

namespace TiltBox.Game
{
    /// <summary>
    /// In-level HUD (GDD §12): level number, swipes left, swipes left for 3 stars, chest bar, coins, home, and
    /// undo / hint / restart with their token counts, plus the win and out-of-moves panels. Built in code with
    /// <see cref="UiKit"/>; week 5 swaps in designed prefabs. Exposes plain events; it holds no game rules.
    /// </summary>
    public sealed class HudView : MonoBehaviour
    {
        /// <summary>Screen fractions the HUD covers; the camera keeps the board out of them.</summary>
        public const float TopFraction = 0.24f;
        public const float BottomFraction = 0.13f;

        public event Action UndoClicked;
        public event Action HintClicked;
        public event Action RestartClicked;
        public event Action RetryClicked;
        public event Action HomeClicked;

        private RectTransform _root;
        private Text _world, _level, _rule, _swipes, _threeStar, _coins, _chestText, _winTitle, _winDetail, _winExtra, _toast;
        private Image[] _winStars;
        private Button _undo, _hint;
        private RectTransform _chestFill;
        private GameObject _winPanel, _outPanel, _threeStarGroup, _chestGroup;
        private float _toastUntil;

        public void Build()
        {
            _root = UiKit.Canvas(transform, "HUD", 0);

            // Top bar
            RectTransform top = UiKit.Group(_root, "Top", new Vector2(0, 1 - TopFraction), Vector2.one);
            UiKit.Button(top, "Home", "Home", new Vector2(0.03f, 0.78f), new Vector2(0.22f, 0.96f), () => HomeClicked?.Invoke(), fontSize: 38);
            _coins = UiKit.CoinCounter(top, new Vector2(0.74f, 0.78f), new Vector2(0.97f, 0.96f));
            _world = UiKit.Label(top, "World", "WORLD 1 · TOY ROOM", 34, FontStyle.Bold, new Vector2(0.22f, 0.8f), new Vector2(0.74f, 0.96f));
            _world.color = UiKit.Muted;
            _level = UiKit.Label(top, "Level", "Level 1", 80, FontStyle.Bold, new Vector2(0, 0.53f), new Vector2(1, 0.8f));
            _rule = UiKit.Label(top, "Rule", "", 34, FontStyle.Italic, new Vector2(0, 0.44f), new Vector2(1, 0.55f));
            _rule.color = new Color32(0xE5, 0x48, 0x4D, 0xFF);
            _swipes = UiKit.Label(top, "Swipes", "8 swipes left", 46, FontStyle.Normal, new Vector2(0.04f, 0.24f), new Vector2(0.5f, 0.46f));
            _threeStarGroup = UiKit.Group(top, "ThreeStar", new Vector2(0.5f, 0.24f), new Vector2(0.96f, 0.46f)).gameObject;
            UiKit.StarRow((RectTransform)_threeStarGroup.transform, new Vector2(0.02f, 0.1f), new Vector2(0.42f, 0.9f), 3);
            _threeStar = UiKit.Label((RectTransform)_threeStarGroup.transform, "ThreeStarText", "in 3", 46, FontStyle.Normal,
                new Vector2(0.47f, 0), new Vector2(1, 1), TextAnchor.MiddleLeft);

            // Chest bar under the top row (§7: always visible)
            _chestGroup = UiKit.Group(top, "ChestBar", new Vector2(0.1f, 0.03f), new Vector2(0.9f, 0.2f)).gameObject;
            _chestFill = UiKit.ProgressBar((RectTransform)_chestGroup.transform, "Bar", new Vector2(0f, 0.15f), new Vector2(0.45f, 0.75f));
            _chestText = UiKit.Label((RectTransform)_chestGroup.transform, "ChestText", "", 34, FontStyle.Normal,
                new Vector2(0.48f, 0f), new Vector2(1f, 0.9f), TextAnchor.MiddleLeft);

            // Bottom bar
            RectTransform bottom = UiKit.Group(_root, "Bottom", Vector2.zero, new Vector2(1, BottomFraction));
            _undo = UiKit.Button(bottom, "Undo", "Undo", new Vector2(0.04f, 0.2f), new Vector2(0.34f, 0.8f), () => UndoClicked?.Invoke(), fontSize: 44);
            _hint = UiKit.Button(bottom, "Hint", "Hint", new Vector2(0.36f, 0.2f), new Vector2(0.64f, 0.8f), () => HintClicked?.Invoke(), fontSize: 44);
            UiKit.Button(bottom, "Restart", "Restart", new Vector2(0.66f, 0.2f), new Vector2(0.96f, 0.8f), () => RestartClicked?.Invoke(), fontSize: 44);

            // Win panel (§7: 1.5 s, stars, coins, chest bar; next level loads unless Home is tapped)
            _winPanel = UiKit.Panel(_root, "WinPanel", new Vector2(0.08f, 0.32f), new Vector2(0.92f, 0.68f), UiKit.Card).gameObject;
            var win = (RectTransform)_winPanel.transform;
            _winTitle = UiKit.Label(win, "Title", "Level complete!", 76, FontStyle.Bold, new Vector2(0, 0.74f), Vector2.one);
            _winStars = UiKit.StarRow(win, new Vector2(0.2f, 0.48f), new Vector2(0.8f, 0.76f), 3);
            _winDetail = UiKit.Label(win, "Detail", "", 42, FontStyle.Normal, new Vector2(0, 0.36f), new Vector2(1, 0.48f));
            _winExtra = UiKit.Label(win, "Extra", "", 42, FontStyle.Bold, new Vector2(0.05f, 0.2f), new Vector2(0.95f, 0.36f));
            UiKit.Button(win, "WinHome", "Home", new Vector2(0.3f, 0.04f), new Vector2(0.7f, 0.18f), () => HomeClicked?.Invoke(), fontSize: 42);
            _winPanel.SetActive(false);

            // Out of moves (§7: "+5 moves (watch ad)" arrives with the ad service in week 5; never an ad after a loss)
            _outPanel = UiKit.Panel(_root, "OutOfMovesPanel", new Vector2(0.08f, 0.36f), new Vector2(0.92f, 0.64f), UiKit.Card).gameObject;
            var outRt = (RectTransform)_outPanel.transform;
            UiKit.Label(outRt, "Title", "Out of moves", 76, FontStyle.Bold, new Vector2(0, 0.62f), Vector2.one);
            UiKit.Button(outRt, "Retry", "Retry", new Vector2(0.1f, 0.14f), new Vector2(0.55f, 0.5f), () => RetryClicked?.Invoke(),
                UiKit.Accent, Color.white);
            UiKit.Button(outRt, "OutHome", "Home", new Vector2(0.6f, 0.14f), new Vector2(0.9f, 0.5f), () => HomeClicked?.Invoke());
            _outPanel.SetActive(false);

            _toast = UiKit.Label(_root, "Toast", "", 42, FontStyle.Italic, new Vector2(0.05f, 1 - TopFraction - 0.06f), new Vector2(0.95f, 1 - TopFraction));
        }

        public void SetVisible(bool visible) => _root.gameObject.SetActive(visible);

        public void SetLevel(string title, bool hard, string subtitle, string rule)
        {
            _world.text = subtitle.ToUpperInvariant();
            _level.text = hard ? title + "  <color=#E5484D>HARD</color>" : title;
            _rule.text = rule ?? "";
        }

        /// <param name="movesLeft">Swipes left, or a negative number for levels with no limit (daily puzzles).</param>
        public void SetMoves(int movesLeft, int leftForThreeStars, bool canUndo, int undoTokens, int freeUndos, int hints)
        {
            _swipes.text = movesLeft < 0 ? "No move limit" : movesLeft == 1 ? "1 swipe left" : movesLeft + " swipes left";
            _threeStarGroup.SetActive(leftForThreeStars > 0);
            _threeStar.text = "in " + leftForThreeStars;
            _undo.interactable = canUndo;
            UiKit.SetText(_undo, freeUndos > 0 ? "Undo (free)" : "Undo (" + undoTokens + ")");
            UiKit.SetText(_hint, "Hint (" + hints + ")");
        }

        public void SetCoins(int coins) => _coins.text = coins.ToString();

        /// <summary>Chest bar: shown in the campaign only. <paramref name="levelsToChest"/> of 5.</summary>
        public void SetChestBar(bool visible, int levelsToChest, int perChest)
        {
            _chestGroup.SetActive(visible);
            if (!visible) return;
            UiKit.SetProgress(_chestFill, 1f - (float)levelsToChest / perChest);
            _chestText.text = levelsToChest == 1 ? "1 more level to a chest" : levelsToChest + " more levels to a chest";
        }

        public void ShowWin(int stars, int moves, int par, string extra)
        {
            _winTitle.text = stars == 3 ? "Perfect!" : "Level complete!";
            UiKit.SetStars(_winStars, stars);
            _winDetail.text = moves + (moves == 1 ? " swipe" : " swipes") + "  ·  par " + par;
            _winExtra.text = extra ?? "";
            _winPanel.SetActive(true);
        }

        public void ShowOutOfMoves(bool show) => _outPanel.SetActive(show);

        public void HidePanels()
        {
            _winPanel.SetActive(false);
            _outPanel.SetActive(false);
        }

        public void Toast(string message, float seconds = 2f)
        {
            _toast.text = message;
            _toastUntil = Time.time + seconds;
        }

        private void Update()
        {
            if (_toast != null && _toast.text.Length > 0 && Time.time > _toastUntil) _toast.text = "";
        }
    }
}
