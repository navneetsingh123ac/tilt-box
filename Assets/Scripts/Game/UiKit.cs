using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TiltBox.Game
{
    /// <summary>
    /// Tiny helpers for building placeholder uGUI in code (weeks 1-4). Week 5 replaces the screens with designed
    /// prefabs; keeping every element on these helpers makes that swap one place to look.
    /// </summary>
    public static class UiKit
    {
        public static readonly Color Ink = new Color32(0x3B, 0x30, 0x25, 0xFF);
        public static readonly Color Muted = new Color32(0x3B, 0x30, 0x25, 0x8C);
        public static readonly Color Card = new Color(1f, 0.98f, 0.93f, 0.97f);
        public static readonly Color ButtonFill = new Color32(0xFF, 0xF8, 0xEC, 0xFF);
        public static readonly Color Accent = new Color32(0x3E, 0x7B, 0xFA, 0xFF);
        public static readonly Color Good = new Color32(0x2F, 0xA6, 0x6A, 0xFF);
        public static readonly Color Gold = new Color32(0xF2, 0xB7, 0x05, 0xFF);
        public static readonly Color StarOff = new Color32(0xD9, 0xCC, 0xB6, 0xFF);
        public static readonly Color Dim = new Color(0.16f, 0.12f, 0.08f, 0.55f);

        private static Font _font;
        public static Font Font => _font != null ? _font : (_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

        /// <summary>A screen-space canvas scaled for the 1080x1920 portrait reference (GDD §14).</summary>
        public static RectTransform Canvas(Transform parent, string name, int sortingOrder)
        {
            if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(parent, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            return (RectTransform)go.transform;
        }

        public static void Stretch(RectTransform rt, Vector2 min, Vector2 max)
        {
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        public static RectTransform Group(RectTransform parent, string name, Vector2 min, Vector2 max)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            Stretch(rt, min, max);
            return rt;
        }

        /// <summary>A rounded panel; transparent colours make a plain layout group that ignores clicks.</summary>
        public static RectTransform Panel(RectTransform parent, string name, Vector2 min, Vector2 max, Color colour)
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
                img.pixelsPerUnitMultiplier = 0.6f;
            }
            return rt;
        }

        /// <summary>A full-screen dimmer that blocks clicks to what's behind a popup.</summary>
        public static RectTransform Blocker(RectTransform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            Stretch(rt, Vector2.zero, Vector2.one);
            go.GetComponent<Image>().color = Dim;
            return rt;
        }

        public static Text Label(RectTransform parent, string name, string text, int size, FontStyle style, Vector2 min, Vector2 max,
            TextAnchor align = TextAnchor.MiddleCenter)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            Stretch(rt, min, max);
            var t = go.GetComponent<Text>();
            t.font = Font;
            t.text = text;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = Ink;
            t.alignment = align;
            t.raycastTarget = false;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        public static Button Button(RectTransform parent, string name, string text, Vector2 min, Vector2 max, Action onClick,
            Color? fill = null, Color? textColour = null, int fontSize = 50)
        {
            RectTransform rt = Panel(parent, name, min, max, fill ?? ButtonFill);
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = rt.GetComponent<Image>();
            button.onClick.AddListener(() => onClick());
            Text label = Label(rt, "Label", text, fontSize, FontStyle.Bold, new Vector2(0.04f, 0f), new Vector2(0.96f, 1f));
            if (textColour.HasValue) label.color = textColour.Value;
            return button;
        }

        public static void SetText(Button b, string text) => b.GetComponentInChildren<Text>().text = text;

        public static Image[] StarRow(RectTransform parent, Vector2 min, Vector2 max, int count)
        {
            RectTransform row = Group(parent, "Stars", min, max);
            var stars = new Image[count];
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("Star" + i, typeof(RectTransform), typeof(Image));
                var srt = (RectTransform)go.transform;
                srt.SetParent(row, false);
                Stretch(srt, new Vector2((float)i / count, 0), new Vector2((float)(i + 1) / count, 1));
                var img = go.GetComponent<Image>();
                img.sprite = PlaceholderArt.Star;
                img.preserveAspect = true;
                img.color = Gold;
                img.raycastTarget = false;
                stars[i] = img;
            }
            return stars;
        }

        public static void SetStars(Image[] stars, int count)
        {
            for (int i = 0; i < stars.Length; i++) stars[i].color = i < count ? Gold : StarOff;
        }

        /// <summary>A gold coin icon plus a number label.</summary>
        public static Text CoinCounter(RectTransform parent, Vector2 min, Vector2 max, int size = 46)
        {
            RectTransform group = Group(parent, "Coins", min, max);
            var icon = new GameObject("CoinIcon", typeof(RectTransform), typeof(Image));
            var irt = (RectTransform)icon.transform;
            irt.SetParent(group, false);
            Stretch(irt, new Vector2(0f, 0.15f), new Vector2(0.28f, 0.85f));
            var img = icon.GetComponent<Image>();
            img.sprite = PlaceholderArt.Circle;
            img.color = Gold;
            img.preserveAspect = true;
            img.raycastTarget = false;
            return Label(group, "CoinText", "0", size, FontStyle.Bold, new Vector2(0.3f, 0f), Vector2.one, TextAnchor.MiddleLeft);
        }

        /// <summary>A horizontal progress bar; returns the fill to resize.</summary>
        public static RectTransform ProgressBar(RectTransform parent, string name, Vector2 min, Vector2 max)
        {
            RectTransform back = Panel(parent, name, min, max, StarOff);
            back.GetComponent<Image>().raycastTarget = false;
            RectTransform fill = Panel(back, "Fill", Vector2.zero, new Vector2(0.5f, 1f), Gold);
            fill.GetComponent<Image>().raycastTarget = false;
            return fill;
        }

        public static void SetProgress(RectTransform fill, float t) => fill.anchorMax = new Vector2(Mathf.Clamp01(t), 1f);
    }
}
