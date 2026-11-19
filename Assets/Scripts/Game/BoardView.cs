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
        [SerializeField] private float portalSeconds = 0.08f;
        [SerializeField] private float conveyorSeconds = 0.14f;

        private const int OrderPanel = 0, OrderTile = 1, OrderBin = 2, OrderLink = 9, OrderBlock = 10, OrderSymbol = 11, OrderFx = 20;
        private const float BlockScale = 0.86f;

        private Transform _root;
        private BoardLayout _layout;
        private Transform[] _blocks;
        private SpriteRenderer[] _bodies, _symbols, _ice;
        private readonly Dictionary<GridPos, SpriteRenderer> _binRings = new Dictionary<GridPos, SpriteRenderer>();
        private readonly List<SpriteRenderer> _locks = new List<SpriteRenderer>();
        private readonly List<Transform> _spinners = new List<Transform>();
        private readonly List<(Transform arrow, Vector3 home, Vector3 dir)> _conveyorArrows = new List<(Transform, Vector3, Vector3)>();
        private readonly List<(int a, int b, Transform bar)> _links = new List<(int, int, Transform)>();

        public BoardLayout Layout => _layout;

        public void Build(LevelDefinition level)
        {
            StopAllCoroutines(); // running animations reference the old board's transforms
            if (_root != null) Destroy(_root.gameObject);
            _binRings.Clear();
            _locks.Clear();
            _spinners.Clear();
            _conveyorArrows.Clear();
            _links.Clear();
            _layout = level.Layout;
            _root = new GameObject("Board").transform;
            _root.SetParent(transform, false);

            var panel = Sprite("Panel", PlaceholderArt.Panel, PlaceholderArt.BoardPanel, OrderPanel, _root);
            panel.drawMode = SpriteDrawMode.Sliced; // keeps the corner radius on non-square boards
            panel.size = FramedSize;

            for (int y = 0; y < _layout.Height; y++)
            for (int x = 0; x < _layout.Width; x++)
                BuildCell(new GridPos(x, y));

            BoardState s = level.Start;
            int n = s.BlockCount;
            _blocks = new Transform[n];
            _bodies = new SpriteRenderer[n];
            _symbols = new SpriteRenderer[n];
            _ice = new SpriteRenderer[n];
            for (int i = 0; i < n; i++)
            {
                BlockColor colour = s.ColorOf(i);
                _bodies[i] = Sprite("Block" + i, PlaceholderArt.RoundedSquare, PlaceholderArt.ColourOf(colour), OrderBlock, _root,
                    Vector3.zero, BlockScale);
                Transform body = _bodies[i].transform;
                Sprite("Shine", PlaceholderArt.Highlight, new Color(1, 1, 1, 0.22f), OrderSymbol, body, Vector3.zero, 1f);
                _symbols[i] = Sprite("Symbol", PlaceholderArt.SymbolOf(colour), new Color(1, 1, 1, 0.92f), OrderSymbol, body, Vector3.zero, 0.5f);
                _ice[i] = Sprite("Ice", PlaceholderArt.Frost, PlaceholderArt.Ice, OrderSymbol + 1, body, Vector3.zero, 1.04f);
                _blocks[i] = body;
            }
            for (int i = 0; i < n; i++)
            {
                int p = s.PartnerOf(i);
                if (p > i)
                    _links.Add((i, p, Sprite("Link", PlaceholderArt.RoundedSquare, PlaceholderArt.Ink, OrderLink, _root, Vector3.zero, 1f).transform));
            }
            ShowState(s);
        }

        private void BuildCell(GridPos p)
        {
            Cell cell = _layout[p];
            Vector3 at = CellToLocal(p);
            if (cell.Kind != CellKind.Wall && cell.Kind != CellKind.Bin && cell.Kind != CellKind.Lock)
                Sprite("Floor", PlaceholderArt.RoundedSquare, PlaceholderArt.FloorTile, OrderTile, _root, at, 0.94f);
            Color ink = new Color(PlaceholderArt.Wall.r, PlaceholderArt.Wall.g, PlaceholderArt.Wall.b, 0.8f);
            switch (cell.Kind)
            {
                case CellKind.Gate:
                    Sprite("Gate", PlaceholderArt.Chevron, ink, OrderBin, _root, at, 0.55f).transform.localRotation = Rotation(cell.Dir);
                    break;
                case CellKind.Wall:
                    Sprite("Wall", PlaceholderArt.RoundedSquare, PlaceholderArt.Wall, OrderTile, _root, at, 0.98f);
                    Sprite("WallShine", PlaceholderArt.Highlight, new Color(1, 1, 1, 0.12f), OrderBin, _root, at, 0.98f);
                    break;
                case CellKind.Bin:
                {
                    Color c = PlaceholderArt.ColourOf(cell.Color);
                    Sprite("BinHole", PlaceholderArt.RoundedSquare, Color.Lerp(c, Color.black, 0.65f), OrderTile, _root, at, 0.94f);
                    _binRings[p] = Sprite("BinRing", PlaceholderArt.Ring, c, OrderBin, _root, at, 0.94f);
                    Sprite("BinSymbol", PlaceholderArt.SymbolOf(cell.Color), new Color(c.r, c.g, c.b, 0.55f), OrderBin, _root, at, 0.42f);
                    break;
                }
                case CellKind.Sticky:
                    Sprite("Sticky", PlaceholderArt.RoundedSquare, PlaceholderArt.StickyPink, OrderTile, _root, at + new Vector3(0, 0, -0.01f), 0.8f).sortingOrder = OrderBin;
                    Sprite("StickyDots", PlaceholderArt.StickyDots, new Color(1, 1, 1, 0.6f), OrderBin + 1, _root, at, 0.6f);
                    break;
                case CellKind.Portal:
                {
                    Color c = PlaceholderArt.PortalColours[(cell.PortalId - 1) % PlaceholderArt.PortalColours.Length];
                    Sprite("PortalHole", PlaceholderArt.Circle, Color.Lerp(c, Color.black, 0.7f), OrderBin, _root, at, 0.8f);
                    _spinners.Add(Sprite("Portal", PlaceholderArt.PortalRing, c, OrderBin + 1, _root, at, 0.85f).transform);
                    break;
                }
                case CellKind.Paint:
                {
                    Color c = PlaceholderArt.ColourOf(cell.Color);
                    Sprite("Paint", PlaceholderArt.Circle, new Color(c.r, c.g, c.b, 0.75f), OrderBin, _root, at, 0.62f);
                    Sprite("PaintSymbol", PlaceholderArt.SymbolOf(cell.Color), new Color(1, 1, 1, 0.8f), OrderBin + 1, _root, at, 0.28f);
                    break;
                }
                case CellKind.Lock:
                {
                    var wall = Sprite("Lock", PlaceholderArt.RoundedSquare, PlaceholderArt.Wall, OrderTile, _root, at, 0.98f);
                    var icon = Sprite("Padlock", PlaceholderArt.Padlock, PlaceholderArt.KeyGold, OrderBin, wall.transform, Vector3.zero, 0.5f);
                    _locks.Add(wall);
                    _locks.Add(icon);
                    // Floor underneath shows through once the lock opens.
                    Sprite("Floor", PlaceholderArt.RoundedSquare, PlaceholderArt.FloorTile, OrderTile - 1, _root, at, 0.94f);
                    break;
                }
                case CellKind.Keyhole:
                    Sprite("KeyholePlate", PlaceholderArt.RoundedSquare, PlaceholderArt.KeyGold, OrderBin, _root, at, 0.8f);
                    _binRings[p] = Sprite("Keyhole", PlaceholderArt.KeyholeShape, PlaceholderArt.Ink, OrderBin + 1, _root, at, 0.55f);
                    break;
                case CellKind.Conveyor:
                {
                    Vector3 dir = Vector3.zero;
                    GridPos o = cell.Dir.Offset();
                    dir = new Vector3(o.X, -o.Y, 0f);
                    Sprite("Belt", PlaceholderArt.RoundedSquare, new Color(0.55f, 0.55f, 0.6f, 0.55f), OrderBin, _root, at, 0.86f);
                    for (int k = -1; k <= 1; k += 2)
                    {
                        Vector3 home = at + dir * (0.18f * k);
                        var arrow = Sprite("BeltArrow", PlaceholderArt.Chevron, new Color(1, 1, 1, 0.75f), OrderBin + 1, _root, home, 0.32f);
                        arrow.transform.localRotation = Rotation(cell.Dir);
                        _conveyorArrows.Add((arrow.transform, home, dir));
                    }
                    break;
                }
            }
        }

        private void Update()
        {
            // Idle motion: portals spin, conveyor arrows creep in their direction.
            float t = Time.time;
            foreach (Transform s in _spinners) if (s != null) s.localRotation = Quaternion.Euler(0, 0, -t * 120f);
            foreach (var (arrow, home, dir) in _conveyorArrows)
                if (arrow != null) arrow.localPosition = home + dir * (Mathf.Repeat(t * 0.6f, 0.36f) - 0.18f);
        }

        private void LateUpdate()
        {
            // Link bars follow their two blocks and hide once either is gone.
            foreach (var (a, b, bar) in _links)
            {
                if (bar == null) continue;
                bool show = _blocks[a].gameObject.activeSelf && _blocks[b].gameObject.activeSelf;
                bar.gameObject.SetActive(show);
                if (!show) continue;
                Vector3 pa = _blocks[a].localPosition, pb = _blocks[b].localPosition;
                bar.localPosition = (pa + pb) * 0.5f;
                bool horizontal = Mathf.Abs(pa.x - pb.x) > Mathf.Abs(pa.y - pb.y);
                float len = Mathf.Max(0.3f, (pa - pb).magnitude);
                bar.localScale = horizontal ? new Vector3(len, 0.22f, 1f) : new Vector3(0.22f, len, 1f);
            }
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
                b.localScale = Vector3.one * BlockScale;
                b.localRotation = Quaternion.identity;
                Recolour(i, state.ColorOf(i));
                _ice[i].gameObject.SetActive(state.IsIced(i) && !state.IsBinned(i));
                _ice[i].transform.localScale = Vector3.one * 1.04f;
                _ice[i].color = PlaceholderArt.Ice;
            }
            foreach (var ring in _binRings.Values) ring.transform.localScale = Vector3.one * (ring.name == "Keyhole" ? 0.55f : 0.94f);
            SetLocks(state.LocksOpen ? 0f : 1f);
            LateUpdate();
        }

        public Vector3 CellToLocal(GridPos p) =>
            new Vector3(p.X - (_layout.Width - 1) * 0.5f, (_layout.Height - 1) * 0.5f - p.Y, 0f);

        public Vector3 CellToWorld(GridPos p) => _root.TransformPoint(CellToLocal(p));

        /// <summary>Plays one swipe: ice cracks and slides together, then conveyor steps, then locks open.</summary>
        public IEnumerator AnimateSwipe(SwipeResult result)
        {
            int maxDist = 1;
            foreach (BlockMove m in result.Moves) maxDist = Mathf.Max(maxDist, m.Distance);
            float perCell = Mathf.Min(secondsPerCell, maxSlideSeconds / maxDist);

            int running = 0;
            int binIndex = 0;
            foreach (int cracked in result.Cracked)
            {
                running++;
                StartCoroutine(Track(Crack(cracked), () => running--));
            }
            foreach (BlockMove m in result.Moves)
            {
                running++;
                int combo = m.Binned ? binIndex++ : -1;
                StartCoroutine(Track(AnimateMove(m, result.Direction, perCell, combo, result.After.ColorOf(m.Block)), () => running--));
            }
            while (running > 0) yield return null;

            foreach (BlockMove m in result.ConveyorMoves)
            {
                running++;
                int combo = m.Binned ? binIndex++ : -1;
                StartCoroutine(Track(Conveyor(m, combo, result.After.ColorOf(m.Block)), () => running--));
            }
            while (running > 0) yield return null;

            if (result.LocksOpened) yield return OpenLocks();
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

        private IEnumerator AnimateMove(BlockMove m, Direction dir, float perCell, int comboIndex, BlockColor finalColour)
        {
            Transform b = _blocks[m.Block];
            for (int s = 0; s < m.Segments.Count; s++)
            {
                Segment seg = m.Segments[s];
                if (s > 0) yield return PortalJump(b, CellToLocal(m.Segments[s - 1].To), CellToLocal(seg.From));
                Vector3 from = CellToLocal(seg.From), to = CellToLocal(seg.To);
                float duration = seg.Length * perCell;
                for (float t = 0; t < duration; t += Time.deltaTime)
                {
                    float u = t / duration;
                    // Brief acceleration, then a glide: mostly linear with a soft start.
                    float eased = Mathf.Lerp(u * u * (3f - 2f * u), u, 0.55f);
                    b.localPosition = Vector3.LerpUnclamped(from, to, eased);
                    yield return null;
                }
                b.localPosition = to;
            }

            if (_bodies[m.Block].color != PlaceholderArt.ColourOf(finalColour)) Recolour(m.Block, finalColour, splash: true);

            if (m.Binned)
            {
                yield return DropIntoBin(b, m.To, comboIndex);
                yield break;
            }
            FlashIfWrongBin(m, dir);
            yield return Bounce(b, dir);
        }

        /// <summary>Shrink into the entry portal, pop out of the exit portal.</summary>
        private IEnumerator PortalJump(Transform b, Vector3 entry, Vector3 exit)
        {
            for (float t = 0; t < portalSeconds; t += Time.deltaTime)
            {
                b.localPosition = entry;
                b.localScale = Vector3.one * (BlockScale * (1f - t / portalSeconds));
                yield return null;
            }
            b.localPosition = exit;
            for (float t = 0; t < portalSeconds; t += Time.deltaTime)
            {
                b.localScale = Vector3.one * (BlockScale * (t / portalSeconds));
                yield return null;
            }
            b.localScale = Vector3.one * BlockScale;
        }

        private IEnumerator Conveyor(BlockMove m, int comboIndex, BlockColor finalColour)
        {
            Transform b = _blocks[m.Block];
            Vector3 from = CellToLocal(m.From), to = CellToLocal(m.To);
            for (float t = 0; t < conveyorSeconds; t += Time.deltaTime)
            {
                b.localPosition = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / conveyorSeconds));
                yield return null;
            }
            b.localPosition = to;
            if (_bodies[m.Block].color != PlaceholderArt.ColourOf(finalColour)) Recolour(m.Block, finalColour, splash: true);
            if (m.Binned) yield return DropIntoBin(b, m.To, comboIndex);
        }

        private void Recolour(int block, BlockColor colour, bool splash = false)
        {
            _bodies[block].color = PlaceholderArt.ColourOf(colour);
            _symbols[block].sprite = PlaceholderArt.SymbolOf(colour);
            _symbols[block].transform.localScale = Vector3.one * (colour == BlockColor.Key ? 0.62f : 0.5f);
            if (splash) StartCoroutine(Burst(_blocks[block].localPosition, PlaceholderArt.ColourOf(colour), 6, 0.8f));
        }

        private IEnumerator OpenLocks()
        {
            const float duration = 0.35f;
            for (float t = 0; t < duration; t += Time.deltaTime)
            {
                SetLocks(1f - t / duration);
                yield return null;
            }
            SetLocks(0f);
        }

        private void SetLocks(float closed)
        {
            foreach (SpriteRenderer r in _locks)
            {
                Color c = r.color;
                c.a = Mathf.Lerp(0.12f, 1f, closed);
                r.color = c;
            }
        }

        /// <summary>Ice shatters: the frost swells and fades, with a few white chips.</summary>
        private IEnumerator Crack(int block)
        {
            SpriteRenderer ice = _ice[block];
            StartCoroutine(Burst(_blocks[block].localPosition, Color.white, 5, 0.7f));
            const float duration = 0.22f;
            for (float t = 0; t < duration; t += Time.deltaTime)
            {
                float u = t / duration;
                ice.transform.localScale = Vector3.one * (1.04f + 0.3f * u);
                ice.color = new Color(PlaceholderArt.Ice.r, PlaceholderArt.Ice.g, PlaceholderArt.Ice.b, PlaceholderArt.Ice.a * (1f - u));
                yield return null;
            }
            ice.gameObject.SetActive(false);
        }

        private static Quaternion Rotation(Direction d)
        {
            switch (d)
            {
                case Direction.Right: return Quaternion.Euler(0, 0, -90f);
                case Direction.Down: return Quaternion.Euler(0, 0, 180f);
                case Direction.Left: return Quaternion.Euler(0, 0, 90f);
                default: return Quaternion.identity;
            }
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
                b.localScale = BlockScale * (horizontal ? new Vector3(1f - squash, 1f + squash, 1f) : new Vector3(1f + squash, 1f - squash, 1f));
                b.localPosition = home + push * s;
                yield return null;
            }
            b.localScale = Vector3.one * BlockScale;
            b.localPosition = home;
        }

        private IEnumerator DropIntoBin(Transform b, GridPos bin, int comboIndex)
        {
            Color colour = b.GetComponent<SpriteRenderer>().color;
            StartCoroutine(Burst(b.localPosition, colour, 6 + comboIndex * 2, 1f + comboIndex * 0.25f));
            if (_binRings.TryGetValue(bin, out SpriteRenderer ring)) StartCoroutine(Punch(ring.transform, ring.transform.localScale.x, 0.18f));
            for (float t = 0; t < binSeconds; t += Time.deltaTime)
            {
                float u = t / binSeconds;
                b.localScale = Vector3.one * (BlockScale * (1f - u * u));
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
            if ((c.Kind == CellKind.Bin || c.Kind == CellKind.Keyhole) && _binRings.TryGetValue(next, out SpriteRenderer ring))
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
