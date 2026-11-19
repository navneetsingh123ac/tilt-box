using System.Collections;
using System.Collections.Generic;
using TiltBox.Core;
using UnityEngine;

namespace TiltBox.Game
{
    /// <summary>
    /// Draws a level with sprites and animates swipe results (GDD §13). Pure view: it never decides rules,
    /// it only plays back the <see cref="SwipeResult"/> that Core produced. One world unit = one cell.
    /// </summary>
    public sealed class BoardView : MonoBehaviour
    {
        [Header("Slide timing (GDD §13)")]
        [SerializeField] private float secondsPerCell = 0.06f;
        [SerializeField] private float maxSlideSeconds = 0.35f;
        [SerializeField] private float bounceSeconds = 0.09f;
        [SerializeField] private float binSeconds = 0.16f;

        private const int OrderPanel = 0, OrderTile = 1, OrderBin = 2, OrderBlock = 10, OrderSymbol = 11, OrderFx = 20;

        private Transform _root;
        private BoardLayout _layout;
        private Transform[] _blocks;
        private readonly Dictionary<GridPos, SpriteRenderer> _binRings = new Dictionary<GridPos, SpriteRenderer>();

        public BoardLayout Layout => _layout;

        public void Build(LevelDefinition level)
        {
            StopAllCoroutines(); // running animations reference the old board's transforms
            if (_root != null) Destroy(_root.gameObject);
            _binRings.Clear();
            _layout = level.Layout;
            _root = new GameObject("Board").transform;
            _root.SetParent(transform, false);

            var panel = Sprite("Panel", PlaceholderArt.Panel, PlaceholderArt.BoardPanel, OrderPanel, _root);
            panel.drawMode = SpriteDrawMode.Sliced; // keeps the corner radius on non-square boards
            panel.size = FramedSize;

            for (int y = 0; y < _layout.Height; y++)
            for (int x = 0; x < _layout.Width; x++)
            {
                var p = new GridPos(x, y);
                Cell cell = _layout[p];
                Vector3 at = CellToLocal(p);
                switch (cell.Kind)
                {
                    case CellKind.Floor:
                        Sprite("Floor", PlaceholderArt.RoundedSquare, PlaceholderArt.FloorTile, OrderTile, _root, at, 0.94f);
                        break;
                    case CellKind.Wall:
                        Sprite("Wall", PlaceholderArt.RoundedSquare, PlaceholderArt.Wall, OrderTile, _root, at, 0.98f);
                        Sprite("WallShine", PlaceholderArt.Highlight, new Color(1, 1, 1, 0.12f), OrderBin, _root, at, 0.98f);
                        break;
                    case CellKind.Bin:
                        Color c = PlaceholderArt.ColourOf(cell.Color);
                        Sprite("BinHole", PlaceholderArt.RoundedSquare, Color.Lerp(c, Color.black, 0.65f), OrderTile, _root, at, 0.94f);
                        _binRings[p] = Sprite("BinRing", PlaceholderArt.Ring, c, OrderBin, _root, at, 0.94f);
                        Sprite("BinSymbol", PlaceholderArt.SymbolOf(cell.Color), new Color(c.r, c.g, c.b, 0.55f), OrderBin, _root, at, 0.42f);
                        break;
                }
            }

            BoardState s = level.Start;
            _blocks = new Transform[s.BlockCount];
            for (int i = 0; i < s.BlockCount; i++)
            {
                BlockColor colour = s.ColorOf(i);
                var body = Sprite("Block" + i + "_" + colour, PlaceholderArt.RoundedSquare, PlaceholderArt.ColourOf(colour), OrderBlock, _root,
                    Vector3.zero, 0.86f);
                Sprite("Shine", PlaceholderArt.Highlight, new Color(1, 1, 1, 0.22f), OrderSymbol, body.transform, Vector3.zero, 1f);
                Sprite("Symbol", PlaceholderArt.SymbolOf(colour), new Color(1, 1, 1, 0.92f), OrderSymbol, body.transform, Vector3.zero, 0.5f);
                _blocks[i] = body.transform;
            }
            ShowState(s);
        }

        /// <summary>Snaps every block to the given state (used for load, undo and restart).</summary>
        public void ShowState(BoardState state)
        {
            StopAllCoroutines();
            _root.localPosition = Vector3.zero;
            for (int i = 0; i < _blocks.Length; i++)
            {
                Transform b = _blocks[i];
                b.gameObject.SetActive(!state.IsBinned(i));
                b.localPosition = CellToLocal(state.PositionOf(i));
                b.localScale = Vector3.one * 0.86f;
                b.localRotation = Quaternion.identity;
            }
            foreach (var ring in _binRings.Values) ring.transform.localScale = Vector3.one * 0.94f;
        }

        public Vector3 CellToLocal(GridPos p) =>
            new Vector3(p.X - (_layout.Width - 1) * 0.5f, (_layout.Height - 1) * 0.5f - p.Y, 0f);

        public Vector3 CellToWorld(GridPos p) => _root.TransformPoint(CellToLocal(p));

        /// <summary>Plays all slides of one swipe at the same time. Yields until every block has settled.</summary>
        public IEnumerator AnimateSwipe(SwipeResult result)
        {
            int maxDist = 1;
            foreach (BlockMove m in result.Moves) maxDist = Mathf.Max(maxDist, m.Distance);
            float perCell = Mathf.Min(secondsPerCell, maxSlideSeconds / maxDist);

            int running = 0;
            int binIndex = 0;
            foreach (BlockMove m in result.Moves)
            {
                running++;
                int combo = m.Binned ? binIndex++ : -1;
                StartCoroutine(Track(AnimateMove(m, result.Direction, perCell, combo), () => running--));
            }
            while (running > 0) yield return null;
        }

        /// <summary>Little shake when a swipe moves nothing (GDD §2).</summary>
        public IEnumerator Shake(Direction dir)
        {
            GridPos o = dir.Offset();
            var axis = new Vector3(o.X, -o.Y, 0f);
            const float duration = 0.18f;
            for (float t = 0; t < duration; t += Time.deltaTime)
            {
                float k = 1f - t / duration;
                _root.localPosition = axis * (Mathf.Sin(t * 60f) * 0.07f * k);
                yield return null;
            }
            _root.localPosition = Vector3.zero;
        }

        private IEnumerator Track(IEnumerator routine, System.Action done)
        {
            yield return routine;
            done();
        }

        private IEnumerator AnimateMove(BlockMove m, Direction dir, float perCell, int comboIndex)
        {
            Transform b = _blocks[m.Block];
            Vector3 from = CellToLocal(m.From);
            Vector3 to = CellToLocal(m.To);
            float duration = m.Distance * perCell;

            for (float t = 0; t < duration; t += Time.deltaTime)
            {
                float u = t / duration;
                // Brief acceleration, then a glide: mostly linear with a soft start.
                float eased = Mathf.Lerp(u * u * (3f - 2f * u), u, 0.55f);
                b.localPosition = Vector3.LerpUnclamped(from, to, eased);
                yield return null;
            }
            b.localPosition = to;

            if (m.Binned)
            {
                yield return DropIntoBin(b, m.To, comboIndex);
                yield break;
            }

            FlashIfWrongBin(m, dir);
            yield return Bounce(b, dir);
        }

        private IEnumerator Bounce(Transform b, Direction dir)
        {
            GridPos o = dir.Offset();
            bool horizontal = o.X != 0;
            Vector3 home = b.localPosition;
            var push = new Vector3(o.X, -o.Y, 0f) * 0.05f;
            for (float t = 0; t < bounceSeconds; t += Time.deltaTime)
            {
                float s = Mathf.Sin(t / bounceSeconds * Mathf.PI);
                float squash = 0.12f * s;
                b.localScale = 0.86f * (horizontal ? new Vector3(1f - squash, 1f + squash, 1f) : new Vector3(1f + squash, 1f - squash, 1f));
                b.localPosition = home + push * s;
                yield return null;
            }
            b.localScale = Vector3.one * 0.86f;
            b.localPosition = home;
        }

        private IEnumerator DropIntoBin(Transform b, GridPos bin, int comboIndex)
        {
            Color colour = b.GetComponent<SpriteRenderer>().color;
            StartCoroutine(Burst(b.localPosition, colour, 6 + comboIndex * 2, 1f + comboIndex * 0.25f));
            if (_binRings.TryGetValue(bin, out SpriteRenderer ring)) StartCoroutine(Punch(ring.transform, 0.94f, 0.18f));
            for (float t = 0; t < binSeconds; t += Time.deltaTime)
            {
                float u = t / binSeconds;
                b.localScale = Vector3.one * (0.86f * (1f - u * u));
                b.localRotation = Quaternion.Euler(0, 0, 90f * u);
                yield return null;
            }
            b.gameObject.SetActive(false);
        }

        private void FlashIfWrongBin(BlockMove m, Direction dir)
        {
            GridPos next = m.To.Step(dir);
            if (!_layout.InBounds(next)) return;
            Cell c = _layout[next];
            if (c.Kind == CellKind.Bin && _binRings.TryGetValue(next, out SpriteRenderer ring))
                StartCoroutine(Flash(ring));
        }

        private IEnumerator Flash(SpriteRenderer r)
        {
            Color baseColour = r.color;
            for (float t = 0; t < 0.35f; t += Time.deltaTime)
            {
                r.color = Color.Lerp(Color.white, baseColour, t / 0.35f);
                yield return null;
            }
            r.color = baseColour;
        }

        private IEnumerator Punch(Transform t, float baseScale, float amount)
        {
            for (float e = 0; e < 0.2f; e += Time.deltaTime)
            {
                t.localScale = Vector3.one * (baseScale * (1f + amount * Mathf.Sin(e / 0.2f * Mathf.PI)));
                yield return null;
            }
            t.localScale = Vector3.one * baseScale;
        }

        private IEnumerator Burst(Vector3 at, Color colour, int count, float power)
        {
            var parts = new Transform[count];
            var vel = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                parts[i] = Sprite("Spark", PlaceholderArt.Circle, colour, OrderFx, _root, at, 0.18f).transform;
                float a = (i + Random.value * 0.5f) / count * Mathf.PI * 2f;
                vel[i] = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * (2.2f + Random.value) * power;
            }
            const float life = 0.35f;
            for (float t = 0; t < life; t += Time.deltaTime)
            {
                for (int i = 0; i < count; i++)
                {
                    parts[i].localPosition += vel[i] * Time.deltaTime;
                    parts[i].localScale = Vector3.one * (0.18f * (1f - t / life));
                }
                yield return null;
            }
            foreach (Transform p in parts) Destroy(p.gameObject);
        }

        private static SpriteRenderer Sprite(string name, Sprite sprite, Color colour, int order, Transform parent,
            Vector3 localPos = default, float scale = 1f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = Vector3.one * scale;
            var r = go.AddComponent<SpriteRenderer>();
            r.sprite = sprite;
            r.color = colour;
            r.sortingOrder = order;
            return r;
        }

        /// <summary>Size of the board including its frame, in world units.</summary>
        public Vector2 FramedSize => new Vector2(_layout.Width + 0.45f, _layout.Height + 0.45f);
    }
}
