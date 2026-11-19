using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TiltBox.Core;
using TiltBox.Game;
using UnityEditor;
using UnityEngine;

namespace TiltBox.EditorTools
{
    /// <summary>
    /// GDD §5 picker: browse every level slot, compare the shipped level with the generator's best candidates,
    /// play any of them right here (arrow keys / WASD, R restart, Z undo), generate fresh candidates, and pick one.
    /// Candidates live in LevelCandidates/ at the project root (one file per slot, written by this window or by
    /// DevTools/LevelLab fill).
    /// </summary>
    public sealed class LevelPickerWindow : EditorWindow
    {
        private const float Cell = 34f;

        private int _world = 1;
        private int _slot = 11;
        private List<StoredCandidate> _candidates = new List<StoredCandidate>();
        private LevelDefinition _current;
        private string _currentInfo = "";
        private int _shown = -1; // -1 = current level, otherwise candidate index
        private LevelSession _play;
        private Vector2 _scroll;
        private Task<List<Candidate>> _job;
        private CancellationTokenSource _cancel;
        private int _jobProgress;
        private int _attempts = 6000;
        private string _status = "";

        private static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;
        private string LevelPath(int world, int n) =>
            Path.Combine(Application.dataPath, "Resources", "Levels", "World" + world, DifficultyPlan.IdFor(world, n) + ".json");
        private string CandidatePath(int world, int n) =>
            Path.Combine(ProjectRoot, "LevelCandidates", DifficultyPlan.IdFor(world, n) + ".json");

        [MenuItem("Tilt Box/Level Picker")]
        public static void Open() => GetWindow<LevelPickerWindow>("Level Picker").minSize = new Vector2(760, 520);

        private void OnEnable() => LoadSlot();

        private void OnDisable() => _cancel?.Cancel();

        private void LoadSlot()
        {
            _candidates = new List<StoredCandidate>();
            _current = null;
            _currentInfo = "";
            _shown = -1;
            string lp = LevelPath(_world, _slot);
            try
            {
                if (File.Exists(lp))
                {
                    _current = LevelParser.Parse(File.ReadAllText(lp), Path.GetFileName(lp));
                    _currentInfo = Describe(_current);
                }
                string cp = CandidatePath(_world, _slot);
                if (File.Exists(cp)) _candidates = CandidateFile.Read(File.ReadAllText(cp), Path.GetFileName(cp));
            }
            catch (LevelFormatException e)
            {
                _status = e.Message;
            }
            StartPlay();
        }

        private static string Describe(LevelDefinition l)
        {
            SolveResult r = Solver.Solve(l.Layout, l.Start);
            return "par " + r.Par + "  optimal " + r.OptimalSolutionCount + "  randomWin " +
                   (LevelMetrics.RandomWinRate(l.Layout, l.Start, l.Limit) * 100).ToString("0") + "%  deadEnds " +
                   LevelMetrics.DeadEndsFromStart(l.Layout, l.Start, l.Limit);
        }

        private LevelDefinition Shown => _shown < 0 ? _current : _candidates[_shown].Level;

        private void StartPlay() => _play = Shown != null ? new LevelSession(Shown) : null;

        private void Update()
        {
            if (_job == null) return;
            Repaint();
            if (!_job.IsCompleted) return;
            if (_job.Status == TaskStatus.RanToCompletion)
            {
                List<Candidate> found = _job.Result;
                Directory.CreateDirectory(Path.GetDirectoryName(CandidatePath(_world, _slot)));
                File.WriteAllText(CandidatePath(_world, _slot), CandidateFile.Write(DifficultyPlan.IdFor(_world, _slot), found));
                _status = found.Count + " candidates kept.";
            }
            else _status = "Generation failed: " + _job.Exception?.GetBaseException().Message;
            _job = null;
            LoadSlot();
        }

        private void OnGUI()
        {
            HandleKeys();
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawSlotList();
                using (new EditorGUILayout.VerticalScope())
                {
                    DrawSlotHeader();
                    DrawBoardAndCandidates();
                }
            }
        }

        private void DrawSlotList()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(170)))
            {
                int world = EditorGUILayout.IntSlider("World", _world, 1, DifficultyPlan.SupportedWorlds);
                if (world != _world) { _world = world; LoadSlot(); }
                _scroll = EditorGUILayout.BeginScrollView(_scroll);
                for (int n = 1; n <= DifficultyPlan.LevelsPerWorld; n++)
                {
                    LevelKind kind = DifficultyPlan.KindFor(_world, n);
                    string label = DifficultyPlan.IdFor(_world, n) + "  " + kind + (DifficultyPlan.IsHandMade(_world, n) ? "  (hand)" : "");
                    var style = n == _slot ? EditorStyles.boldLabel : EditorStyles.label;
                    if (GUILayout.Button(label, style)) { _slot = n; LoadSlot(); }
                }
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawSlotHeader()
        {
            SlotSpec spec = DifficultyPlan.SpecFor(_world, _slot);
            EditorGUILayout.LabelField(DifficultyPlan.IdFor(_world, _slot) + " — " + DifficultyPlan.WorldNames[_world], EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Slot: " + spec);
            EditorGUILayout.LabelField("Shipped: " + (_current == null ? "none" : _currentInfo));
            using (new EditorGUILayout.HorizontalScope())
            {
                _attempts = EditorGUILayout.IntField("Attempts", _attempts, GUILayout.Width(220));
                if (_job == null)
                {
                    if (GUILayout.Button("Generate candidates", GUILayout.Width(160))) StartJob(spec);
                }
                else
                {
                    GUILayout.Label("Generating… " + _jobProgress + "/" + _attempts, GUILayout.Width(200));
                    if (GUILayout.Button("Cancel", GUILayout.Width(70))) _cancel.Cancel();
                }
            }
            if (!string.IsNullOrEmpty(_status)) EditorGUILayout.HelpBox(_status, MessageType.Info);
        }

        private void StartJob(SlotSpec spec)
        {
            _cancel = new CancellationTokenSource();
            _jobProgress = 0;
            int seed = (int)(System.DateTime.Now.Ticks & 0x7fffffff);
            CancellationToken token = _cancel.Token;
            int attempts = _attempts;
            _job = Task.Run(() => LevelGenerator.Generate(spec, seed, attempts, 10, token, n => _jobProgress = n));
            _status = "Seed " + seed;
        }

        private void DrawBoardAndCandidates()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                // Choice list
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(330)))
                {
                    if (_current != null && GUILayout.Toggle(_shown == -1, "Shipped level", "Button")) Select(-1);
                    for (int i = 0; i < _candidates.Count; i++)
                        if (GUILayout.Toggle(_shown == i, "#" + (i + 1) + "  " + _candidates[i].Summary, "Button")) Select(i);
                    if (_candidates.Count == 0) EditorGUILayout.HelpBox("No candidates for this slot yet.", MessageType.None);

                    GUILayout.Space(8);
                    using (new EditorGUI.DisabledScope(_shown < 0))
                    {
                        if (GUILayout.Button("Use this candidate for " + DifficultyPlan.IdFor(_world, _slot), GUILayout.Height(28)))
                            UseCandidate(_candidates[_shown]);
                    }
                }

                // Board + play
                using (new EditorGUILayout.VerticalScope())
                {
                    if (_play == null) { GUILayout.Label("Nothing to show."); return; }
                    GUILayout.Label("Play: arrows / WASD, R restart, Z undo.  Swipes " + _play.MovesUsed + " / " + _play.Limit +
                                    "  (par " + _play.Level.Par + ")  " + _play.Status +
                                    (_play.Status == SessionStatus.Won ? "  " + _play.Stars + " stars" : ""));
                    Rect area = GUILayoutUtility.GetRect(_play.Level.Layout.Width * Cell + 8, _play.Level.Layout.Height * Cell + 8,
                        GUILayout.ExpandWidth(false));
                    DrawBoard(area, _play.Level.Layout, _play.State);
                }
            }
        }

        private void Select(int index)
        {
            if (_shown == index) return;
            _shown = index;
            StartPlay();
            GUI.FocusControl(null);
        }

        private void UseCandidate(StoredCandidate c)
        {
            string path = LevelPath(_world, _slot);
            if (File.Exists(path) && DifficultyPlan.IsHandMade(_world, _slot) &&
                !EditorUtility.DisplayDialog("Replace a hand-made level?", DifficultyPlan.IdFor(_world, _slot) +
                    " is a hand-made slot. Replace it with this generated candidate?", "Replace", "Cancel"))
                return;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, LevelWriter.ToJson(c.Level, "Picked in the Level Picker; " + c.Summary.Replace("  ", ", ")));
            AssetDatabase.Refresh();
            _status = "Wrote " + DifficultyPlan.IdFor(_world, _slot) + ". Run the EditMode tests to re-check all levels.";
            LoadSlot();
        }

        private void HandleKeys()
        {
            Event e = Event.current;
            if (e.type != EventType.KeyDown || _play == null || EditorGUIUtility.editingTextField) return;
            Direction? dir = null;
            switch (e.keyCode)
            {
                case KeyCode.UpArrow: case KeyCode.W: dir = Direction.Up; break;
                case KeyCode.DownArrow: case KeyCode.S: dir = Direction.Down; break;
                case KeyCode.LeftArrow: case KeyCode.A: dir = Direction.Left; break;
                case KeyCode.RightArrow: case KeyCode.D: dir = Direction.Right; break;
                case KeyCode.R: _play.Restart(); e.Use(); Repaint(); return;
                case KeyCode.Z: _play.Undo(); e.Use(); Repaint(); return;
            }
            if (dir == null) return;
            _play.Swipe(dir.Value);
            e.Use();
            Repaint();
        }

        private static void DrawBoard(Rect area, BoardLayout layout, BoardState state)
        {
            EditorGUI.DrawRect(area, PlaceholderArt.BoardPanel);
            var centred = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 15 };
            for (int y = 0; y < layout.Height; y++)
            for (int x = 0; x < layout.Width; x++)
            {
                var r = new Rect(area.x + 4 + x * Cell + 2, area.y + 4 + y * Cell + 2, Cell - 4, Cell - 4);
                TiltBox.Core.Cell cell = layout[x, y];
                switch (cell.Kind)
                {
                    case CellKind.Wall: EditorGUI.DrawRect(r, PlaceholderArt.Wall); break;
                    case CellKind.Bin:
                        EditorGUI.DrawRect(r, PlaceholderArt.ColourOf(cell.Color));
                        EditorGUI.DrawRect(new Rect(r.x + 4, r.y + 4, r.width - 8, r.height - 8), Color.Lerp(PlaceholderArt.ColourOf(cell.Color), Color.black, 0.65f));
                        break;
                    default:
                        EditorGUI.DrawRect(r, PlaceholderArt.FloorTile);
                        if (cell.Kind == CellKind.Gate)
                        {
                            centred.normal.textColor = PlaceholderArt.Wall;
                            GUI.Label(r, cell.ToChar().ToString(), centred);
                        }
                        break;
                }
                int b = state.BlockAt(new GridPos(x, y));
                if (b < 0) continue;
                var inner = new Rect(r.x + 3, r.y + 3, r.width - 6, r.height - 6);
                EditorGUI.DrawRect(inner, PlaceholderArt.ColourOf(state.ColorOf(b)));
                if (state.IsIced(b))
                {
                    EditorGUI.DrawRect(inner, new Color(PlaceholderArt.Ice.r, PlaceholderArt.Ice.g, PlaceholderArt.Ice.b, 0.55f));
                }
                centred.normal.textColor = state.IsIced(b) ? PlaceholderArt.Ink : Color.white;
                GUI.Label(inner, state.IsIced(b) ? "ice" : state.ColorOf(b).ToLetter().ToString().ToUpperInvariant(), centred);
            }
        }
    }
}
