# AL Pregnancy

Version **0.2.27**. A BepInEx IL2CPP pregnancy plugin for **AmanatsuLocation**.

Provides pregnancy progression, belly and clothing deformation, F1 global settings, an F8 character panel with manual shape preview, and synchronization with the game's existing fluid collision meshes.

## Changes in 0.2.27

AL's additional clothing slots (`add_etc`, `add_arm`, `add_leg` and their compact name variants) now enter the existing clothing deformation and pose-response pipeline. This fixes the heart waist chain `o_add_etc00_waist00` from the reported coordinate card and covers similarly named belts and additional garments.

Some of these native assets disable CPU Read/Write. When acquiring their private meshes, the plugin can reconstruct a readable copy from the original mesh's GPU vertex/index buffers. The copy preserves all vertex streams, skin bind poses/weights, submeshes, UVs and bounds. Only supplemental clothing uses this fallback. The shared original is restored through the existing lease and fluid-lifetime handling. A failed readback or an unsupported unreadable blend-shape mesh stays on its original mesh and emits a `[Clothing] Cannot read` warning.

Belly shape, growth, saved settings, defaults and the free-accessory exclusions are unchanged. No additional F1/F8 controls are needed.

GPU copying occurs once per mesh lease, not every frame, but may briefly synchronize the GPU when changing outfits. The reporter confirmed the waist-chain fix works in game with the supplied coordinate. Broader pose coverage and outfit-switching performance have not been measured; see `VERIFICATION.txt` for the scope of validation.

## Features and defaults

- F1 defaults: SimpleCycle, 280 gestation days, and 180 recovery days. Existing saved configuration values take precedence.
- Growth milestones: 0%, 20%, 45%, 70%, and 100%. The first four are configurable in F1; growth is linear within each stage.
- Independent daily variation per character: `(1 / base gestation days) * (1 + random[-1, 1] * variation fraction)`.
- F8 shape defaults retain the previously accepted settings.
- Preserves the existing Obi mesh-index synchronization fix for fluid collisions.

## Build

Requires Windows, .NET SDK 10, and a working AmanatsuLocation installation with BepInEx IL2CPP. The plugin targets .NET 6; regression projects target .NET 10. The first build may need to restore framework reference packs.

Game, Unity, Obi, BepInEx, and IL2CPP dependency assemblies are not included. References are loaded from your game's `BepInEx/core` and interop directories.

```powershell
./Build.ps1 -GameDir 'D:\Games\AmanatsuLocation'
```

The build prefers `BepInEx/interop-AmanatsuLocation/interop` when present, otherwise `BepInEx/interop`. Override it when necessary:

```powershell
./Build.ps1 -GameDir 'D:\Games\AmanatsuLocation' -InteropDir 'D:\Games\AmanatsuLocation\BepInEx\interop'
```

Alternatively, run `dotnet build src/AL_Pregnancy.csproj -c Release -p:GameDir="D:\Games\AmanatsuLocation"`, or set the `AL_GAME_DIR` environment variable before running dotnet.

Build output: `src/bin/Release/net6.0/AL_Pregnancy.dll`.

## Install or upgrade

1. Close the game and back up the existing plugin.
2. Remove the old `AL_Pregnancy.Preview.dll` from the plugins tree when upgrading. Do not keep both DLLs installed.
3. Place `AL_Pregnancy.dll` in `BepInEx/plugins/AL_Pregnancy/`. The old `AL_PregnancyPreview` directory can be renamed to `AL_Pregnancy` to keep its diagnostics.
4. Leave existing configuration files and saves in place. No configuration reset or save migration is required.

Only install the plugin DLL, not its dependency assemblies. F1 opens global configuration when the game's configuration manager is available. F8 opens the character and manual shape panel. New diagnostic captures are written to `BepInEx/plugins/AL_Pregnancy/diagnostics/latest-mesh.json`.

The stable internal plugin ID remains `local.al.pregnancy.preview` for compatibility. Accordingly, the existing configuration filename remains `local.al.pregnancy.preview.cfg`. These are legacy identifiers, not release-status labels. The word "preview" in manual shape controls and the `navelPreviewFull` setting describes an actual feature and remains unchanged.

`presets/ALPregnancy_belly.json` contains the accepted F8 defaults as a reference. Normal upgrades do not require replacing existing configuration.

## Tests

```powershell
./Test.ps1 -GameDir 'D:\Games\AmanatsuLocation'
```

Runs the main regression suite, collision cache tests, native completion tests, fluid mesh lifetime tests, shared-source lifetime tests, and clothing buffer-copy tests. The main suite requires the local `BepInEx.Core.dll`; the other suites use source code and test doubles. Offline tests do not replace in-game visual, native physics, or performance checks.

`tests/user-trace-*.json` contains lightweight mesh-selection and bone-name regression data. Full character geometry captures, game saves, and local configuration are excluded. Optional collision replay requires a separately supplied matching 0.2.19 capture; the default test command does not depend on it.

The buffer-copy implementation uses Unity's [original mesh vertex buffers](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Mesh.GetVertexBuffer.html), not posed renderer output.

## Repository layout

- `src/`: plugin runtime source.
- `presets/`: accepted F8 defaults.
- `tests/`: shape, progression, storage, selection, and configuration regressions.
- `clothing-runtime-tests/`: mesh copy success/failure tests using API doubles.
- `collision-runtime-tests/`: collision cache and Obi index tests, plus optional capture replay.
- `readiness-tests/`, `lifetime-tests/`, `source-lifetime-tests/`: readiness and resource ownership regressions.

Upload the contents of this directory to the repository root. Build products are excluded by `.gitignore`. Prebuilt plugin binaries belong in a separate release archive.

No source license has been selected on the author's behalf. The repository owner can add a LICENSE file when choosing the distribution terms.
