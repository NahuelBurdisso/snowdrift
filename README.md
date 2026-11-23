# Snowdrift

An endless downhill snowboarding game built in Unity 6 (URP). Carve down a procedurally
generated slope, pop off kickers, land tricks to build a combo multiplier, and thread the
trees and rocks for near-miss bonuses. Difficulty ramps with distance: the speed cap rises
and the slope gets more crowded the further you ride.

## Highlights

- **Procedural, streamed terrain** — `SlopeField` defines the slope analytically and
  `TerrainStream` builds chunks ahead of the rider with deterministic prop placement, plus a
  floating-origin shift so long runs stay numerically stable.
- **Physics-driven board** — carving grip, charged jumps, slope-aligned landings judged by
  `LandingJudge`, and a ragdoll wipeout with anatomical joint limits and a tumble impulse.
- **Run flow** — title → riding → results state machine (`RunFlow`), combo and trick scoring,
  HUD feedback (trick popups, score punch, near-miss callouts).
- **Everything generated from code** — the main scene, rider prefab and ragdoll, pose clips,
  the snowboard mesh and even the audio (wind, carve, thump, music loop, synthesized as PCM)
  are built by editor scripts, so the project can be rebuilt from scratch in batch mode.
- **Tested** — gameplay rules live in a pure C# assembly (`Snowdrift.Core`) covered by
  EditMode tests; PlayMode smoke tests run the real scene (crash/respawn, pause, restart,
  origin shift) and capture screenshots for visual checks.

## Project layout

| Path | What |
|---|---|
| `Assets/Scripts/Core` | Pure logic, no `UnityEngine` scene dependencies: carve math, combo, tricks, difficulty curve, run flow |
| `Assets/Scripts/Game` | MonoBehaviours: board controller, camera rig, crash handler, terrain stream, HUD, game feel |
| `Assets/Scripts/Editor` | Scene/prefab/asset generators, screenshot tool, build script |
| `Assets/Tests` | EditMode and PlayMode suites |

## Requirements

Unity **6000.4.0f1** with the Universal Render Pipeline, Input System and Cinemachine
(packages are pinned in `Packages/manifest.json`).

## Running

Open the project in Unity Hub and play `Assets/Scenes/Main.unity`.

From the command line (do not pass `-nographics`; URP needs a graphics device):

```bash
# EditMode tests
Unity.exe -batchmode -projectPath . -runTests -testPlatform EditMode -testResults test-results.xml

# PlayMode smoke tests
Unity.exe -batchmode -projectPath . -runTests -testPlatform PlayMode -testResults playmode-results.xml

# Rebuild the main scene from code
Unity.exe -batchmode -quit -projectPath . -executeMethod Snowdrift.Editor.ProjectSetup.Step5_BuildMainScene

# Windows standalone build -> Build/Windows/Snowdrift.exe
Unity.exe -batchmode -quit -projectPath . -executeMethod Snowdrift.Editor.BuildScript.BuildWindows
```

## Credits

Third-party models are CC0 (Kenney, Quaternius). See [CREDITS.md](CREDITS.md). Code, generated
clips, audio, shaders and materials are original to this project.
