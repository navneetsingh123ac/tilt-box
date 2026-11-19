using System.Collections;
using System.Collections.Generic;
using System.IO;
using TiltBox.Core;
using UnityEngine;

namespace TiltBox.Game
{
    /// <summary>
    /// Development tool, not part of the shipped scene: plays levels through the real game path
    /// (GameController.Swipe -> animation -> win screen -> next level) using the solver's optimal solutions.
    /// Uses: an end-to-end smoke test of every level in the engine, and capturing real-speed devlog/trailer
    /// footage (frames are rendered at a fixed rate with Time.captureFramerate, so the video plays at true speed).
    /// Add it to the Game object in Play mode.
    /// </summary>
    [RequireComponent(typeof(GameController))]
    public sealed class DemoAutoPlayer : MonoBehaviour
    {
        public int startLevel;
        public int levelCount = 10;
        [Tooltip("Pause before each swipe, like a player thinking.")]
        public float thinkSeconds = 0.45f;
        [Tooltip("When set, every frame is saved here as a JPG at captureFps.")]
        public string captureFolder = "";
        public int captureFps = 30;

        public bool Finished { get; private set; }
        public int LevelsWon { get; private set; }
        public readonly List<string> Report = new List<string>();

        private int _frame;

        private IEnumerator Start()
        {
            var game = GetComponent<GameController>();
            if (!string.IsNullOrEmpty(captureFolder))
            {
                Directory.CreateDirectory(captureFolder);
                Time.captureFramerate = captureFps;
                StartCoroutine(Capture());
            }

            yield return null;
            game.LoadLevel(startLevel);
            yield return Wait(0.8f);

            for (int n = 0; n < levelCount; n++)
            {
                int index = game.LevelIndex;
                LevelSession session = game.Session;
                SolveResult plan = Solver.Solve(session.Level.Layout, session.State);
                foreach (Direction dir in plan.Solution)
                {
                    while (game.IsBusy) yield return null;
                    yield return Wait(thinkSeconds);
                    game.Swipe(dir);
                    yield return null;
                }
                bool won = session.Status == SessionStatus.Won;
                if (won) LevelsWon++;
                Report.Add(session.Level.Id + ": " + (won ? "won in " + session.MovesUsed + " (" + session.Stars + " stars)" : "NOT WON " + session.Status));

                // Wait for the win screen and the automatic next level.
                float timeout = 6f;
                while (game.LevelIndex == index && timeout > 0f)
                {
                    timeout -= Time.deltaTime;
                    yield return null;
                }
                yield return Wait(0.5f);
            }

            Finished = true;
            Time.captureFramerate = 0;
            Debug.Log("DemoAutoPlayer: " + LevelsWon + "/" + levelCount + " won. " + string.Join(" | ", Report));
        }

        private static IEnumerator Wait(float seconds)
        {
            for (float t = 0; t < seconds; t += Time.deltaTime) yield return null;
        }

        private IEnumerator Capture()
        {
            var endOfFrame = new WaitForEndOfFrame();
            while (!Finished)
            {
                yield return endOfFrame;
                Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(Path.Combine(captureFolder, "f" + (_frame++).ToString("D5") + ".jpg"), shot.EncodeToJPG(92));
                Destroy(shot);
            }
        }
    }
}
