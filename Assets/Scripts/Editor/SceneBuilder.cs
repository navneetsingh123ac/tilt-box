using TiltBox.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TiltBox.EditorTools
{
    /// <summary>
    /// Rebuilds the game scene and the project settings it depends on from code, so the setup is reviewable
    /// and repeatable instead of living only in hand-edited scene files. Menu: Tilt Box / Build Game Scene.
    /// </summary>
    public static class SceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/Game.unity";

        [MenuItem("Tilt Box/Build Game Scene")]
        public static void BuildGameScene()
        {
            // No "save changes?" dialog: this also runs from automation, where a modal would hang the editor.
            // It replaces the open scene, so run it from an empty or saved scene.
            ApplyProjectSettings();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 6f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = PlaceholderArt.Background;
            cam.transform.position = new Vector3(0f, 0f, -10f);

            var game = new GameObject("Game");
            game.AddComponent<GameController>(); // RequireComponent adds SwipeInput, BoardView, HudView

            if (!AssetDatabase.IsValidFolder("Assets/Scenes")) AssetDatabase.CreateFolder("Assets", "Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Debug.Log("Tilt Box: built " + ScenePath + " and set it as the only build scene.");
        }

        [MenuItem("Tilt Box/Apply Project Settings")]
        public static void ApplyProjectSettings()
        {
            PlayerSettings.companyName = "Sketchify Studio";
            PlayerSettings.productName = "Tilt Box";
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            // PC (Google Play Games / Windows fun-test builds): a portrait window.
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 540;
            PlayerSettings.defaultScreenHeight = 960;
            PlayerSettings.resizableWindow = true;
            Debug.Log("Tilt Box: project settings applied (portrait, 540x960 window on PC).");
        }
    }
}
