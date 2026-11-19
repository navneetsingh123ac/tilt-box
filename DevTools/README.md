# Tilt Box dev tools

Everything here runs without the Unity editor (needs the .NET SDK).

## Core tests

Runs the same NUnit tests as Unity's Test Runner (`Assets/Tests/EditMode`) against the same Core code (`Assets/Scripts/Core`):

```
cd DevTools/CoreTests
dotnet test
```

`DevTools/Core` compiles Core as netstandard2.1 + C# 9, the same as Unity 6, so if it builds here it builds in Unity.
The content tests check every level file against the difficulty plan (`Core/DifficultyPlan.cs`, GDD §4–§6).

## LevelLab

```
cd DevTools/LevelLab
dotnet run -- check ../../Assets/Resources/Levels/World2      # stats + "fits its slot?" for each level
dotnet run -- slot 3 14                                        # generate one slot, print the best 5
dotnet run -- fill 3 4 30                                      # generate a range of slots (see below)
```

`fill <world> [from] [to] [attempts] [--teach] [--force]` generates each slot, stores the 10 best candidates in
`LevelCandidates/<id>.json` (git-ignored, reproducible) and writes the best one that isn't already used as the
level file. It skips hand-made slots (World 1 levels 1–10 and teach levels) unless `--teach`, and keeps existing
level files unless `--force`. Generation is deterministic and uses all CPU cores.

## In the editor

- **Tilt Box > Level Picker**: browse every slot, compare the shipped level with the stored candidates, play
  any of them inside the window (arrows / WASD, R restart, Z undo), generate fresh candidates, and pick one.
  Run the EditMode tests after picking.
- **Tilt Box > Build Game Scene** rebuilds `Assets/Scenes/Game.unity` and sets it as the only build scene.
- **Tilt Box > Apply Project Settings** sets portrait, product and company name, and a 540x960 PC window.
- Dev keys in Play mode (editor and development builds): N / P next and previous level, R restart,
  Z or Backspace undo, H hint.
- `DemoAutoPlayer` (add it to the Game object in Play mode) plays levels with the solver's solutions through the
  real game. It works as an end-to-end smoke test and for recording real-speed devlog clips (set `captureFolder`).
