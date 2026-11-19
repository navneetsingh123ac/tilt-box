# Tilt Box dev tools

Everything here runs without the Unity editor (needs the .NET SDK).

## Core tests

Runs the same NUnit tests as Unity's Test Runner (`Assets/Tests/EditMode`) against the same Core code (`Assets/Scripts/Core`):

```
cd DevTools/CoreTests
dotnet test
```

`DevTools/Core` compiles Core as netstandard2.1 + C# 9, the same as Unity 6, so if it builds here it builds in Unity.

## LevelLab

Checks level files: solver par vs. the file's par, number of optimal solutions, random-win rate (GDD §5),
dead-end first swipes, and how often the solution parks a block against a wrong-colour bin.

```
cd DevTools/LevelLab
dotnet run -- check ../../Assets/Resources/Levels/World1
```

Search for board ideas (random boards with a par in range, best-scored first, printed as JSON to paste into a level file):

```
dotnet run -- search <w> <h> <colours> <blocks> <minPar> <maxPar> [walls] [seed] [tries] [--wrongbin]
dotnet run -- search 6 6 3 4 8 8 6 41 3000
```

The real generator and picker is a Unity editor window planned for week 2.

## In the editor

- **Tilt Box > Build Game Scene** rebuilds `Assets/Scenes/Game.unity` and sets it as the only build scene.
- **Tilt Box > Apply Project Settings** sets portrait, product and company name, and a 540x960 PC window.
- Dev keys in Play mode (editor and development builds): N / P next and previous level, R restart,
  Z or Backspace undo, H hint.
- `DemoAutoPlayer` (add it to the Game object in Play mode) plays levels with the solver's solutions through the
  real game. It works as an end-to-end smoke test and for recording real-speed devlog clips.
