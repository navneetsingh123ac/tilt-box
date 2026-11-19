using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TiltBox.Game
{
    /// <summary>
    /// In-level HUD (GDD §12): level number, swipes left, swipes left for 3 stars, undo, restart, plus the
    /// win and out-of-moves panels. Built in code for week 1 so there is no prefab to keep in sync; week 5
    /// replaces it with designed prefabs. Exposes plain events; it holds no game rules.
    /// </summary>
    public sealed class HudView : MonoBehaviour
    {
        /// <summary>Screen fractions the HUD covers; the camera keeps the board out of them.</summary>
        public const float TopFraction = 0.2f;
        public const float BottomFraction = 0.13f;

        public event Action UndoClicked;
        public event Action RestartClicked;
        public event Action RetryClicked;

        private static readonly Color InkColour = new Color32(0x3B, 0x30, 0x25, 0xFF);
        private static readonly Color ButtonColour = new Color32(0xFF, 0xF8, 0xEC, 0xFF);
        private static readonly Color StarOn = new Color32(0xF2, 0xB7, 0x05, 0xFF);
        private static readonly Color StarOff = new Color32(0xD9, 0xCC, 0xB6, 0xFF);

        private Font _font;
        private Text _world, _level, _swipes, _threeStar, _winTitle, _winDetail, _toast;
        private Image[] _hudStars, _winStars;
        private Button _undo;
        private GameObject _winPanel, _outPanel, _threeStarGroup;
        private float _toastUntil;

        public void Build()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (FindAnyObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            var canvasGo = new GameObject("HUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            RectTransform root = canvasGo.GetComponent<RectTransform>();

            // Top bar
            RectTransform top = Panel(root, "Top", new Vector2(0, 1 - TopFraction), Vector2.one, Color.clear);
            _world = Label(top, "World", "WORLD 1 · TOY ROOM", 36, FontStyle.Bold, new Vector2(0, 0.82f), new Vector2(1, 0.98f));
            _world.color = new Color(InkColour.r, InkColour.g, InkColour.b, 0.55f);
            _level = Label(top, "Level", "Level 1", 84, FontStyle.Bold, new Vector2(0, 0.5f), new Vector2(1, 0.86f));
            _swipes = Label(top, "Swipes", "8 swipes left", 50, FontStyle.Normal, new Vector2(0.04f, 0.12f), new Vector2(0.5f, 0.5f));
            _threeStarGroup = new GameObject("ThreeStar", typeof(RectTransform));
            var group = (RectTransform)_threeStarGroup.transform;
            group.SetParent(top, false);
            Stretch(group, new Vector2(0.5f, 0.12f), new Vector2(0.96f, 0.5f));
            _hudStars = StarRow(group, new Vector2(0.02f, 0.1f), new Vector2(0.42f, 0.9f), 3);
            _threeStar = Label(group, "ThreeStarText", "in 3", 50, FontStyle.Normal, new Vector2(0.47f, 0), new Vector2(1, 1));
            _threeStar.alignment = TextAnchor.MiddleLeft;

            // Bottom bar
            RectTransform bottom = Panel(root, "Bottom", Vector2.zero, new Vector2(1, BottomFraction), Color.clear);
            _undo = MakeButton(bottom, "Undo", "Undo", new Vector2(0.08f, 0.2f), new Vector2(0.46f, 0.8f), () => UndoClicked?.Invoke());
            MakeButton(bottom, "Restart", "Restart", new Vector2(0.54f, 0.2f), new Vector2(0.92f, 0.8f), () => RestartClicked?.Invoke());

            // Win panel
            _winPanel = Panel(root, "WinPanel", new Vector2(0.08f, 0.36f), new Vector2(0.92f, 0.64f), new Color(1, 0.98f, 0.93f, 0.97f)).gameObject;
            _winTitle = Label((RectTransform)_winPanel.transform, "Title", "Level complete!", 80, FontStyle.Bold, new Vector2(0, 0.62f), Vector2.one);
            _winStars = StarRow((RectTransform)_winPanel.transform, new Vector2(0.2f, 0.3f), new Vector2(0.8f, 0.64f), 3);
            _winDetail = Label((RectTransform)_winPanel.transform, "Detail", "", 44, FontStyle.Normal, Vector2.zero, new Vector2(1, 0.3f));
            _winPanel.SetActive(false);

            // Out of moves panel (no ad button until the ad service exists, week 5)
            _outPanel = Panel(root, "OutOfMovesPanel", new Vector2(0.08f, 0.36f), new Vector2(0.92f, 0.64f), new Color(1, 0.98f, 0.93f, 0.97f)).gameObject;
            Label((RectTransform)_outPanel.transform, "Title", "Out of moves", 80, FontStyle.Bold, new Vector2(0, 0.6f), Vector2.one);
            Button retry = MakeButton((RectTransform)_outPanel.transform, "Retry", "Retry", new Vector2(0.25f, 0.14f), new Vector2(0.75f, 0.5f),
                () => RetryClicked?.Invoke());
            retry.GetComponent<Image>().color = PlaceholderArt.ColourOf(TiltBox.Core.BlockColor.Blue);
            retry.GetComponentInChildren<Text>().color = Color.white;
            _outPanel.SetActive(false);

            _toast = Label(root, "Toast", "", 46, FontStyle.Italic, new Vector2(0, 1 - TopFraction - 0.05f), new Vector2(1, 1 - TopFraction));
        }

        public void SetLevel(int number, bool hard, int world, string worldName)
        {
            _world.text = ("World " + world + " · " + worldName).ToUpperInvariant();
            _level.text = hard ? "Level " + number + "  <color=#E5484D>HARD</color>" : "Level " + number;
            _level.supportRichText = true;
        }

        public void SetMoves(int movesLeft, int leftForThreeStars, bool canUndo)
        {
            _swipes.text = movesLeft == 1 ? "1 swipe left" : movesLeft + " swipes left";
            _threeStarGroup.SetActive(leftForThreeStars > 0);
            _threeStar.text = "in " + leftForThreeStars;
            _undo.interactable = canUndo;
        }

        public void ShowWin(int stars, int moves, int par)
        {
            _winTitle.text = stars == 3 ? "Perfect!" : "Level complete!";
            for (int i = 0; i < _winStars.Length; i++) _winStars[i].color = i < stars ? StarOn : StarOff;
            _winDetail.text = moves + (moves == 1 ? " swipe" : " swipes") + "  ·  par " + par;
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

        // --- tiny uGUI construction helpers ---

        private static void Stretch(RectTransform rt, Vector2 min, Vector2 max)
        {
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static RectTransform Panel(RectTransform parent, string name, Vector2 min, Vector2 max, Color colour)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            Stretch(rt, min, max);
            var img = go.GetComponent<Image>();
            img.color = colour;
            img.raycastTarget = colour.a > 0f;
            if (colour.a > 0f)
            {
                img.sprite = PlaceholderArt.Panel;
                img.type = Image.Type.Sliced;
                img.pixelsPerUnitMultiplier = 0.6f; // bigger corners than the sprite's native size
            }
            return rt;
        }

        private Text Label(RectTransform parent, string name, string text, int size, FontStyle style, Vector2 min, Vector2 max)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            Stretch(rt, min, max);
            var t = go.GetComponent<Text>();
            t.font = _font;
            t.text = text;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = InkColour;
            t.alignment = TextAnchor.MiddleCenter;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            return t;
        }

        private Button MakeButton(RectTransform parent, string name, string text, Vector2 min, Vector2 max, Action onClick)
        {
            RectTransform rt = Panel(parent, name, min, max, ButtonColour);
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = rt.GetComponent<Image>();
            button.onClick.AddListener(() => onClick());
            Label(rt, "Label", text, 54, FontStyle.Bold, Vector2.zero, Vector2.one);
            return button;
        }

        private static Image[] StarRow(RectTransform parent, Vector2 min, Vector2 max, int count)
        {
            var row = new GameObject("Stars", typeof(RectTransform));
            var rt = (RectTransform)row.transform;
            rt.SetParent(parent, false);
            Stretch(rt, min, max);
            var stars = new Image[count];
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("Star" + i, typeof(RectTransform), typeof(Image));
                var srt = (RectTransform)go.transform;
                srt.SetParent(rt, false);
                Stretch(srt, new Vector2((float)i / count, 0), new Vector2((float)(i + 1) / count, 1));
                var img = go.GetComponent<Image>();
                img.sprite = PlaceholderArt.Star;
                img.preserveAspect = true;
                img.color = StarOn;
                img.raycastTarget = false;
                stars[i] = img;
            }
            return stars;
        }
    }
}
