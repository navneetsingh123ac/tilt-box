using System.Collections;
using System.Collections.Generic;
using TiltBox.Core;
using UnityEngine;

namespace TiltBox.Game
{
    /// <summary>
    /// Runs the level sequence across all worlds: wires input -> <see cref="LevelSession"/> (rules) -> views.
    /// The only class that knows about all the others; each view only exposes events and Show methods.
    /// </summary>
    [RequireComponent(typeof(SwipeInput), typeof(BoardView), typeof(HudView))]
    public sealed class GameController : MonoBehaviour
    {
        [SerializeField] private float winScreenSeconds = 1.5f;
        [Tooltip("Show the animated hand on the first level until the first swipe (GDD §11).")]
        [SerializeField] private bool handHintOnFirstLevel = true;

        private const string ProgressKey = "tiltbox.level";
        private const string OldWorld1ProgressKey = "tiltbox.w1.level"; // week-1 builds saved per world

        // One-line tips when a world's new mechanic first appears (placeholder for the week-5 tutorial flow).
        private static readonly Dictionary<string, string> Tips = new Dictionary<string, string>
        {
            { "w2_01", "Ice! An iced block skips one swipe, then slides." },
            { "w3_01", "One-way gates: blocks pass only along the arrow." },
        };
        private const string StarsKey = "tiltbox.stars.{0}";

        private SwipeInput _input;
        private BoardView _board;
        private HudView _hud;
        private Camera _camera;
        private List<LevelDefinition> _levels;
        private int _index;
        private LevelSession _session;
        private bool _busy;
        private GameObject _hand;
        private Vector2Int _lastScreen;

        public LevelSession Session => _session;
        public int LevelIndex => _index;
        public bool IsBusy => _busy;

        private void Awake()
        {
            _input = GetComponent<SwipeInput>();
            _board = GetComponent<BoardView>();
            _hud = GetComponent<HudView>();
            _camera = Camera.main;
            _hud.Build();
            _input.Swiped += OnSwipe;
            _hud.UndoClicked += OnUndo;
            _hud.RestartClicked += OnRestart;
            _hud.RetryClicked += OnRestart;
        }

        private void Start()
        {
            _levels = LevelCatalog.LoadAll();
            if (_levels.Count == 0)
            {
                Debug.LogError("No levels found in Resources/Levels");
                enabled = false;
                return;
            }
            int saved = PlayerPrefs.GetInt(ProgressKey, PlayerPrefs.GetInt(OldWorld1ProgressKey, 0));
            LoadLevel(Mathf.Clamp(saved, 0, _levels.Count - 1));
        }

        private void Update()
        {
            var screen = new Vector2Int(Screen.width, Screen.height);
            if (screen != _lastScreen) FitCamera();
            DevKeys();
        }

        public void LoadLevel(int index)
        {
            StopAllCoroutines();
            _index = (index % _levels.Count + _levels.Count) % _levels.Count;
            _session = new LevelSession(_levels[_index]);
            _busy = false;
            _board.Build(_session.Level);
            _hud.HidePanels();
            LevelDefinition level = _session.Level;
            _hud.SetLevel(_index + 1, level.Kind == LevelKind.Hard, level.World, DifficultyPlan.WorldNames[level.World]);
            if (Tips.TryGetValue(level.Id, out string tip)) _hud.Toast(tip, 6f);
            RefreshHud();
            FitCamera();
            SetHand(handHintOnFirstLevel && _index == 0);
        }

        /// <summary>Same path as a player swipe; used by input and by automated play-throughs.</summary>
        public void Swipe(Direction dir) => OnSwipe(dir);

        private void OnSwipe(Direction dir)
        {
            if (_busy || _session.Status != SessionStatus.Playing) return; // input ignored while animating (§14)
            SwipeResult result = _session.Swipe(dir);
            if (!result.Changed)
            {
                StartCoroutine(_board.Shake(dir));
                return;
            }
            SetHand(false);
            StartCoroutine(PlaySwipe(result));
        }

        private IEnumerator PlaySwipe(SwipeResult result)
        {
            _busy = true;
            RefreshHud();
            yield return _board.AnimateSwipe(result);
            _busy = false;

            switch (_session.Status)
            {
                case SessionStatus.Won:
                    yield return WinFlow();
                    break;
                case SessionStatus.OutOfMoves:
                    _hud.ShowOutOfMoves(true);
                    break;
            }
        }

        private IEnumerator WinFlow()
        {
            _busy = true;
            LevelDefinition level = _session.Level;
            int stars = _session.Stars;
            string key = string.Format(StarsKey, level.Id);
            if (stars > PlayerPrefs.GetInt(key, 0)) PlayerPrefs.SetInt(key, stars);
            int next = _index + 1;
            PlayerPrefs.SetInt(ProgressKey, next % _levels.Count);
            PlayerPrefs.Save();

            _hud.ShowWin(stars, _session.MovesUsed, level.Par);
            yield return new WaitForSeconds(winScreenSeconds);
            if (next >= _levels.Count) _hud.Toast("You finished every level! Back to level 1.", 3f);
            else if (_levels[next].World != level.World)
                _hud.Toast("World " + _levels[next].World + ": " + DifficultyPlan.WorldNames[_levels[next].World], 3f);
            LoadLevel(next);
        }

        private void OnUndo()
        {
            if (_busy || !_session.Undo()) return;
            _board.ShowState(_session.State);
            _hud.ShowOutOfMoves(false);
            RefreshHud();
        }

        private void OnRestart()
        {
            if (_busy) return;
            _session.Restart();
            _board.ShowState(_session.State);
            _hud.HidePanels();
            RefreshHud();
        }

        private void RefreshHud() =>
            _hud.SetMoves(_session.MovesLeft, _session.MovesLeftForThreeStars, _session.CanUndo);

        /// <summary>Fits the board into the screen area between the HUD bars, for any aspect ratio.</summary>
        private void FitCamera()
        {
            if (_camera == null || _board.Layout == null) return;
            _lastScreen = new Vector2Int(Screen.width, Screen.height);
            float aspect = (float)Screen.width / Mathf.Max(1, Screen.height);
            float free = 1f - HudView.TopFraction - HudView.BottomFraction;
            Vector2 size = _board.FramedSize + new Vector2(0.4f, 0.4f);
            float ortho = Mathf.Max(size.y / free * 0.5f, size.x / aspect * 0.5f);
            _camera.orthographic = true;
            _camera.orthographicSize = ortho;
            // Shift the camera so the board sits in the middle of the free band, not the middle of the screen.
            float bandCentre = HudView.BottomFraction + free * 0.5f; // viewport y
            _camera.transform.position = new Vector3(0f, (0.5f - bandCentre) * 2f * ortho, -10f);
        }

        // --- first-level hand hint (placeholder: a pulsing dot that slides the solver's first move) ---

        private void SetHand(bool show)
        {
            if (_hand != null) Destroy(_hand);
            _hand = null;
            if (!show) return;
            Direction? hint = Solver.Hint(_session.Level.Layout, _session.State);
            if (!hint.HasValue) return;
            _hand = new GameObject("HandHint");
            var r = _hand.AddComponent<SpriteRenderer>();
            r.sprite = PlaceholderArt.Circle;
            r.color = new Color(1f, 1f, 1f, 0.85f);
            r.sortingOrder = 50;
            StartCoroutine(AnimateHand(_hand.transform, r, hint.Value));
        }

        private IEnumerator AnimateHand(Transform hand, SpriteRenderer r, Direction dir)
        {
            GridPos o = dir.Offset();
            Vector3 from = _board.CellToWorld(_session.State.PositionOf(0));
            Vector3 to = from + new Vector3(o.X, -o.Y, 0f) * 1.6f;
            while (hand != null)
            {
                for (float t = 0; t < 1.1f && hand != null; t += Time.deltaTime)
                {
                    float u = Mathf.Clamp01(t / 0.8f);
                    hand.position = Vector3.Lerp(from, to, u * u * (3f - 2f * u));
                    hand.localScale = Vector3.one * 0.45f;
                    r.color = new Color(1f, 1f, 1f, 0.85f * (1f - Mathf.Max(0f, t - 0.8f) / 0.3f));
                    yield return null;
                }
                yield return new WaitForSeconds(0.25f);
            }
        }

        // --- developer shortcuts for play-testing (editor and development builds only) ---

        private void DevKeys()
        {
            if (!Debug.isDebugBuild || _busy) return;
            if (Input.GetKeyDown(KeyCode.N)) LoadLevel(_index + 1);
            else if (Input.GetKeyDown(KeyCode.P)) LoadLevel(_index - 1);
            else if (Input.GetKeyDown(KeyCode.R)) OnRestart();
            else if (Input.GetKeyDown(KeyCode.Z) || Input.GetKeyDown(KeyCode.Backspace)) OnUndo();
            else if (Input.GetKeyDown(KeyCode.H))
            {
                Direction? hint = Solver.Hint(_session.Level.Layout, _session.State);
                _hud.Toast(hint.HasValue ? "Hint: swipe " + hint.Value : "No way to win from here - undo or restart");
            }
        }
    }
}
