using System.Collections;
using System.Collections.Generic;
using TiltBox.Core;
using TiltBox.Meta;
using UnityEngine;

namespace TiltBox.Game
{
    /// <summary>
    /// The composition root and play flow: loads the save and level files, shows the home screen, and runs
    /// campaign levels, the daily puzzle and the Weekend Tower. Wires input -> <see cref="LevelSession"/> (rules)
    /// -> views, and reports results to <see cref="MetaGame"/> (economy). Views only expose events and Show methods.
    /// </summary>
    [RequireComponent(typeof(SwipeInput), typeof(BoardView), typeof(HudView))]
    [RequireComponent(typeof(MetaScreens))]
    public sealed class GameController : MonoBehaviour
    {
        public enum PlayMode { Home, Campaign, Daily, Tower }

        [SerializeField] private float winScreenSeconds = 1.5f;
        [Tooltip("Show the animated hand on the first level until the first swipe (GDD §11).")]
        [SerializeField] private bool handHintOnFirstLevel = true;

        // One-line tips when a world's new mechanic first appears (placeholder for the week-5 tutorial flow).
        private static readonly Dictionary<string, string> Tips = new Dictionary<string, string>
        {
            { "w2_01", "Ice! An iced block skips one swipe, then slides." },
            { "w3_01", "One-way gates: blocks pass only along the arrow." },
            { "w4_01", "Sticky pads stop a block. The next swipe frees it." },
            { "w5_01", "Portals: in one, out the other, still sliding." },
            { "w6_01", "Paint changes a block's colour as it passes." },
            { "w7_01", "Slide the key into the keyhole to open the locks." },
            { "w8_01", "Conveyors move a resting block one step after each swipe." },
            { "w9_01", "Linked blocks move together until one is binned." },
            { "w10_01", "Rooftop: every trick you've learned, together." },
        };

        private SwipeInput _input;
        private BoardView _board;
        private HudView _hud;
        private MetaScreens _screens;
        private Camera _camera;
        private List<LevelDefinition> _levels;
        private MetaGame _meta;
        private SaveStore _store;

        private PlayMode _mode = PlayMode.Home;
        private int _index;
        private int _towerFloor;
        private LevelSession _session;
        private bool _busy;
        private bool _homeRequested;
        private int _freeUndos;
        private readonly List<Direction> _swipeLog = new List<Direction>();
        private GameObject _hand;
        private Vector2Int _lastScreen;

        public LevelSession Session => _session;
        public int LevelIndex => _index;
        public bool IsBusy => _busy;
        public PlayMode Mode => _mode;
        public MetaGame Meta => _meta;

        /// <summary>Dev tools (auto-player): skip chest popups so play isn't interrupted. Rewards still apply.</summary>
        public bool QuietRewards { get; set; }

        private void Awake()
        {
            _input = GetComponent<SwipeInput>();
            _board = GetComponent<BoardView>();
            _hud = GetComponent<HudView>();
            _screens = GetComponent<MetaScreens>();
            _camera = Camera.main;
            _hud.Build();
            _input.Swiped += OnSwipe;
            _hud.UndoClicked += OnUndo;
            _hud.HintClicked += OnHint;
            _hud.RestartClicked += OnRestart;
            _hud.RetryClicked += OnRestart;
            _hud.HomeClicked += OnHome;
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

            var storage = new FileSaveStorage();
            _store = new SaveStore(storage);
            SaveData save = _store.Load();
            if (_store.LastLoadSource == "backup") _store.Save(save); // repair the damaged main file right away
            _meta = new MetaGame(save, new SystemClock(), _store.Save);
            Debug.Log("Tilt Box: save loaded from " + _store.LastLoadSource + " (" + storage.Folder + ")");

            _screens.Build(_meta);
            _screens.PlayCampaign = () => LoadLevel(Mathf.Min(_meta.Save.Level, _levels.Count - 1));
            _screens.PlayDaily = StartDaily;
            _screens.PlayTower = StartTower;
            _screens.CampaignLabel = () => _meta.Save.Level >= _levels.Count ? "All done! Replay" : "Level " + (_meta.Save.Level + 1);
            _screens.DailyAvailable = () => LoadDailyLevel(_meta.Daily().PoolDay) != null;
            _screens.TowerAvailable = () => LoadTowerLevel(_meta.Tower().Tower, 1) != null;
            ShowHome();
        }

        /// <summary>Dev tools: play on a throwaway in-memory save so the real save file is untouched.</summary>
        public void UseTemporarySave()
        {
            _meta = new MetaGame(new SaveData(), new SystemClock(), null);
            _screens.Build(_meta);
            _screens.ShowHome(false);
        }

        private void OnApplicationPause(bool paused)
        {
            if (!paused) _meta?.Touch();
        }

        private void Update()
        {
            var screen = new Vector2Int(Screen.width, Screen.height);
            if (screen != _lastScreen) FitCamera();
            DevKeys();
        }

        // ------------------------------------------------------------------ starting levels

        private void ShowHome()
        {
            StopAllCoroutines();
            _busy = false;
            _mode = PlayMode.Home;
            ClearHand();
            _hud.HidePanels();
            _hud.SetVisible(false);
            _screens.ShowHome(true);
        }

        /// <summary>Plays campaign level <paramref name="index"/> (0-based across all worlds).</summary>
        public void LoadLevel(int index)
        {
            _index = (index % _levels.Count + _levels.Count) % _levels.Count;
            LevelDefinition level = _levels[_index];
            Begin(PlayMode.Campaign, level, "Level " + (_index + 1), "World " + level.World + " · " + DifficultyPlan.WorldNames[level.World]);
            if (Tips.TryGetValue(level.Id, out string tip)) _hud.Toast(tip, 6f);
            if (handHintOnFirstLevel && _index == 0 && _meta.Save.Level == 0) ShowHintHand(Solver.Hint(level.Layout, level.Start));
        }

        private void StartDaily()
        {
            DailyStatus d = _meta.Daily();
            LevelDefinition level = LoadDailyLevel(d.PoolDay);
            if (level == null) { _screens.ShowMessage("Daily puzzle", "Today's puzzle isn't in this build yet."); return; }
            Begin(PlayMode.Daily, level, "Daily #" + d.Number, "Daily puzzle");
        }

        private void StartTower()
        {
            TowerStatus t = _meta.Tower();
            if (!t.Open || t.Reached >= DifficultyPlan.LevelsPerTower) return;
            _towerFloor = t.Reached + 1;
            LevelDefinition level = LoadTowerLevel(t.Tower, _towerFloor);
            if (level == null) { _screens.ShowMessage("Weekend Tower", "This tower isn't in this build yet."); return; }
            Begin(PlayMode.Tower, level, "Floor " + _towerFloor + " / " + DifficultyPlan.LevelsPerTower, "Weekend Tower " + t.Tower);
        }

        /// <summary>Plays any level outside the sequence (dev tools, previews). Progress isn't saved for it.</summary>
        public void PlayLevel(LevelDefinition level) => Begin(PlayMode.Home, level, level.Id, "Preview");

        private void Begin(PlayMode mode, LevelDefinition level, string title, string subtitle)
        {
            StopAllCoroutines();
            _mode = mode;
            _session = new LevelSession(level);
            _busy = false;
            _homeRequested = false;
            _freeUndos = Economy.FreeUndosPerLevel;
            _swipeLog.Clear();
            _screens.ShowHome(false);
            _hud.SetVisible(true);
            _board.Build(level);
            _hud.HidePanels();
            _hud.SetLevel(title, level.Kind == LevelKind.Hard, subtitle, level.Rules.Describe());
            ClearHand();
            RefreshHud();
            FitCamera();
        }

        private static LevelDefinition LoadDailyLevel(int day) => Load("Daily/" + DifficultyPlan.DailyId(day));

        private static LevelDefinition LoadTowerLevel(int tower, int floor) =>
            Load("Towers/Tower" + tower + "/" + DifficultyPlan.TowerId(tower, floor));

        private static LevelDefinition Load(string resource)
        {
            var file = Resources.Load<TextAsset>(resource);
            if (file == null) return null;
            try { return LevelParser.Parse(file.text, file.name); }
            catch (LevelFormatException e) { Debug.LogError(e.Message); return null; }
        }

        // ------------------------------------------------------------------ playing

        /// <summary>Same path as a player swipe; used by input and by automated play-throughs.</summary>
        public void Swipe(Direction dir) => OnSwipe(dir);

        private void OnSwipe(Direction dir)
        {
            if (_session == null || _busy || _screens.PopupOpen || _screens.HomeVisible) return;
            if (_session.Status != SessionStatus.Playing) return; // input ignored while animating (§14)
            SwipeResult result = _session.Swipe(dir);
            if (!result.Changed)
            {
                StartCoroutine(_board.Shake(result.Direction));
                return;
            }
            _swipeLog.Add(dir);
            ClearHand();
            StartCoroutine(PlaySwipe(result));
        }

        private IEnumerator PlaySwipe(SwipeResult result)
        {
            _busy = true;
            RefreshHud();
            yield return _board.AnimateSwipe(result);
            _busy = false;

            if (_session.Status == SessionStatus.Won) yield return WinFlow();
            else if (_session.Status == SessionStatus.OutOfMoves)
            {
                if (_mode != PlayMode.Home) _meta.OnLevelLost();
                _hud.ShowOutOfMoves(true);
            }
        }

        private IEnumerator WinFlow()
        {
            _busy = true;
            LevelDefinition level = _session.Level;
            int stars = _session.Stars;
            switch (_mode)
            {
                case PlayMode.Campaign:
                {
                    WinReward reward = _meta.OnCampaignWin(_index, level.Id, level.Kind == LevelKind.Hard, stars);
                    string extra = reward.Coins > 0 ? "+" + reward.Coins + " coins" : reward.NewLevel ? "" : "Replay";
                    if (reward.SmallChest != null || reward.WorldChest != null) extra += "  ·  Chest!";
                    _hud.ShowWin(stars, _session.MovesUsed, level.Par, extra);
                    RefreshHud();
                    yield return WaitOrHome(winScreenSeconds);
                    int next = _index + 1;
                    if (_homeRequested || next >= _levels.Count)
                    {
                        ShowHome();
                        if (next >= _levels.Count) _screens.ShowMessage("You did it!", "Every level is complete.\nCome back for the daily puzzle and the Weekend Tower.");
                    }
                    else
                    {
                        if (_levels[next].World != level.World)
                            _hud.Toast("World " + _levels[next].World + ": " + DifficultyPlan.WorldNames[_levels[next].World], 3f);
                        LoadLevel(next);
                    }
                    if (!QuietRewards)
                    {
                        if (reward.SmallChest != null) _screens.ShowMessage("Chest!", MetaScreens.Describe(reward.SmallChest));
                        if (reward.WorldChest != null)
                            _screens.ShowMessage("World " + level.World + " complete!", "World chest: " + MetaScreens.Describe(reward.WorldChest));
                    }
                    break;
                }
                case PlayMode.Daily:
                {
                    DailyStatus d = _meta.Daily();
                    DailyResult result = _meta.OnDailySolved(_swipeLog, stars);
                    _hud.ShowWin(stars, _session.MovesUsed, level.Par, result.Coins > 0 ? "+" + result.Coins + " coins" : "");
                    yield return WaitOrHome(1f);
                    ShowHome();
                    _screens.ShowDailyResult(d.Number, _session.MovesUsed, stars, result, null);
                    break;
                }
                case PlayMode.Tower:
                {
                    TowerReward reward = _meta.OnTowerLevelWon(_towerFloor);
                    _hud.ShowWin(stars, _session.MovesUsed, level.Par, reward != null ? "+" + reward.Coins + " coins" : "");
                    yield return WaitOrHome(winScreenSeconds);
                    if (reward != null && reward.Completed)
                    {
                        ShowHome();
                        _screens.ShowMessage("Top of the tower!", "+" + reward.Coins + " coins" + (reward.ItemId != null ? "\nNew box skin: " + ShelfCatalog.Find(reward.ItemId)?.Name : ""));
                    }
                    else if (_homeRequested || reward == null) ShowHome();
                    else StartTower();
                    break;
                }
                default:
                    _hud.ShowWin(stars, _session.MovesUsed, level.Par, "Preview");
                    yield return WaitOrHome(winScreenSeconds);
                    ShowHome();
                    break;
            }
        }

        private IEnumerator WaitOrHome(float seconds)
        {
            for (float t = 0; t < seconds && !_homeRequested; t += Time.deltaTime) yield return null;
        }

        private void OnHome()
        {
            if (_busy && _session != null && _session.Status == SessionStatus.Won) { _homeRequested = true; return; }
            if (_busy) return;
            ShowHome();
        }

        private void OnUndo()
        {
            if (_busy || _session == null || !_session.CanUndo) return;
            if (!_meta.TryUseUndo(ref _freeUndos))
            {
                _hud.Toast("No undos left. Get more in the shop.");
                return;
            }
            _session.Undo();
            if (_swipeLog.Count > 0) _swipeLog.RemoveAt(_swipeLog.Count - 1);
            _board.ShowState(_session.State);
            _hud.ShowOutOfMoves(false);
            RefreshHud();
        }

        private void OnHint()
        {
            if (_busy || _session == null || _session.Status != SessionStatus.Playing) return;
            if (_meta.Hints <= 0) { _hud.Toast("No hints left. Get more in the shop."); return; }
            RuleSet rules = _session.Level.Rules;
            Direction? move = Solver.Hint(_session.Level.Layout, _session.State, rules);
            if (move == null) { _hud.Toast("No way to win from here. Undo or restart."); return; } // free: nothing to show
            _meta.TryUseHint();
            Direction swipe = rules.SwipeFor(move.Value);
            _hud.Toast("Swipe " + swipe.ToString().ToLowerInvariant(), 3f);
            ShowHintHand(swipe);
            RefreshHud();
        }

        private void OnRestart()
        {
            if (_busy || _session == null) return;
            _session.Restart();
            _freeUndos = Economy.FreeUndosPerLevel;
            _swipeLog.Clear();
            _board.ShowState(_session.State);
            _hud.HidePanels();
            RefreshHud();
        }

        private void RefreshHud()
        {
            if (_session == null || _meta == null) return;
            _hud.SetMoves(_session.Level.HasMoveLimit ? _session.MovesLeft : -1, _session.MovesLeftForThreeStars, _session.CanUndo,
                _meta.Undos, _freeUndos, _meta.Hints);
            _hud.SetCoins(_meta.Coins);
            _hud.SetChestBar(_mode == PlayMode.Campaign, _meta.LevelsToChest, Economy.LevelsPerSmallChest);
        }

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

        // ------------------------------------------------------------------ hint hand (placeholder: a sliding dot)

        private void ShowHintHand(Direction? swipe)
        {
            ClearHand();
            if (!swipe.HasValue) return;
            _hand = new GameObject("HandHint");
            var r = _hand.AddComponent<SpriteRenderer>();
            r.sprite = PlaceholderArt.Circle;
            r.color = new Color(1f, 1f, 1f, 0.85f);
            r.sortingOrder = 50;
            StartCoroutine(AnimateHand(_hand.transform, r, swipe.Value));
        }

        private void ClearHand()
        {
            if (_hand != null) Destroy(_hand);
            _hand = null;
        }

        private IEnumerator AnimateHand(Transform hand, SpriteRenderer r, Direction dir)
        {
            GridPos o = dir.Offset();
            Vector3 from = _board.CellToWorld(new GridPos(_board.Layout.Width / 2, _board.Layout.Height / 2)) - new Vector3(o.X, -o.Y, 0f) * 0.8f;
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

        // ------------------------------------------------------------------ developer shortcuts (editor and dev builds)

        private void DevKeys()
        {
            if (!Debug.isDebugBuild || _busy || _mode != PlayMode.Campaign) return;
            if (Input.GetKeyDown(KeyCode.N)) LoadLevel(_index + 1);
            else if (Input.GetKeyDown(KeyCode.P)) LoadLevel(_index - 1);
            else if (Input.GetKeyDown(KeyCode.R)) OnRestart();
            else if (Input.GetKeyDown(KeyCode.Z) || Input.GetKeyDown(KeyCode.Backspace)) OnUndo();
            else if (Input.GetKeyDown(KeyCode.H)) OnHint();
        }
    }
}
