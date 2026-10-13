# AL Pregnancy

**Version 0.2.40** — a BepInEx IL2CPP pregnancy plugin for **AmanatsuLocation**.

Pregnancy progresses with game days and drives a growing belly, clothing deformation, pose response, and optional body changes based on childbirth history. Use **F1** for global settings and **F8** for character state and manual shape preview.

[Downloads](https://github.com/rock-1995/AL_Pregnancy.dll/releases) · [Changelog](CHANGELOG.md) · [Validation](VERIFICATION.txt)

## Features

- Three conception modes, per-character cycles, gestation, recovery, father tracking, and persistent birth counts.
- Four-stage belly growth with adjustable fullness, width, reach, smoothing, downward sag, and original-navel shaping.
- Pose-aware attachment, lower attachment/upper release controls, and curved height boundaries.
- Breast protection based on **actual breast physics-chain skin weights**, with smooth below-breast transitions. Sternum/chest weights do not stand in for breast physics.
- Thigh-side, thigh-bone, and localized lowest-belly/pubic guards shared by shape and pose blending.
- Clothing deformation, supported supplemental clothing slots, and skirt-chain support that releases toward the native moving hem.
- Synchronization with existing low-detail body/fluid collision meshes and Obi mesh indices.
- Optional body changes using the difference between two character cards, applied to each character's own original body.
- F8 character/debug controls, manual preview, unrestricted finite numeric shape input, and diagnostic export.

## Install or upgrade

Requires AmanatsuLocation with **BepInEx IL2CPP**. Install the release DLL at `BepInEx/plugins/AL_Pregnancy/AL_Pregnancy.dll`, or extract the release archive's `BepInEx` folder into the game directory. It replaces the previous plugin; do not load it alongside `AL_Pregnancy.Preview.dll`. A compatible configuration manager supplies the **F1** interface; **F8** is provided by this plugin.

**Upgrading the DLL does not replace a saved belly preset.** Values in `BepInEx/config/ALPregnancy_belly.json` take priority over the new built-in defaults. To use the complete 0.2.40 shape, install the supplied `presets/ALPregnancy_belly.json` at that config path and restart; to retain your custom shape, leave it in place. Gameplay saves remain compatible.

**Card-based body changes require `base.png` and `reference.png` beside the DLL.** Without these valid character cards, belly growth still works but template-based body changes do not. See the template section below. The release includes the default template pair beside the DLL, plus the guide, verification notes, and preset. Game dependency assemblies are not included.

The stable plugin ID remains `local.al.pregnancy.preview`, so the global configuration filename remains `local.al.pregnancy.preview.cfg`. These legacy names do not indicate the installed version.

## Pregnancy gameplay and F1

Conception requires a supported normal-game encounter with a live female NPC, the player as the relevant target, and vaginal internal ejaculation. Free scenes without a live save actor, anal events, and ambiguous multi-participant encounters are skipped. The plugin does not guess a father from ambiguous participants.

| Setting | Default | Behavior |
| --- | --- | --- |
| Plugin / pregnancy gameplay | Enabled | Disabling gameplay pauses pregnancy progression. |
| Conception mode | SimpleCycle | SimpleCycle, ComplexCycle, or FixedChance. |
| Gestation | 280 days | Changes preserve normalized pregnancy progress. |
| Daily variation | 0% | Independent per character and game day. |
| Recovery | 180 days | Post-birth cooldown; 0 disables it. |
| Growth milestones | 0 / 20 / 45 / 70 / 100% | First four configurable; linear growth within each stage. |

**SimpleCycle:** default cycle length is 28 days. Days 8–19 use normal chance (5%); days 12–16 use danger chance (50%) and take priority. Other days use safe chance (0%). Cycle intervals and probabilities are configurable.

**FixedChance:** a fixed probability per qualifying event, 30% by default.

**ComplexCycle:** qualifying events introduce residual semen; conception is checked during daily cycle processing. Defaults: 30% base chance, 75% residual retention per day, ovulation on day 14. Egg viability is 1 on ovulation day, 0.5 the next day, and 0 otherwise. Residual amounts below 0.1 are cleared. Chance depends on base probability, residual amount, and egg viability.

Daily gestation advance is `(1 / gestationDays) × (1 + random[-1, 1] × variationFraction)`. Birth completion updates birth count and starts recovery. Setting full term in the debug panel is processed by the next game-day advance.

## F8: character state and manual shape

### Character/debug panel

Select a character to inspect pregnancy progress, day, recovery, cycle, conception chance, father, and birth count. Debug actions can force conception, clear pregnancy/recovery, set day or progress, jump to full term, and reset cooldown. These actions change gameplay state. **Save through the game's normal save menu to persist them.**

### Manual shape panel

1. Open a female character in the creator or scene. Only visible high-detail female characters are listed.
2. Select the character and enable preview.
3. Move **Growth stage** between 0 and 1; adjust shape, attachment, protection, clothing, and navel controls.
4. For an exact value, type in its numeric field and click **Apply**. Slider ranges are suggestions; finite values outside them are accepted and are not clamped merely by displaying the slider.
5. Use **Reset / restore meshes** to stop preview and restore the original meshes.

Manual preview does not change pregnancy, birth count, or saved gameplay state. **Default shape** resets to this version's built-in parameters; **Full-term shape** also sets growth to 1. Navel presets and **Rebuild shape** help visual checking. Optional card-based body progression is suspended while manual preview is active.

### Curvature and protection

| Control / JSON key | Default | Effect |
| --- | --- | --- |
| Height boundary arc / `heightBoundaryArc` | 0.8 | Above the full-term belly center, bands arch upward at the center; below it, they dip downward. 0 gives straight bands. Higher positive values increase side-to-center curvature. |
| Breast guard × physics weight / `breastGuard` | 1 | Multiplies summed actual breast-physics-chain weights; restore amount is clamped to 0–1. |
| Below-breast guard width / `lowerBreastGuardWidth` | 0.2078125 | Smooth protection width below the breast boundary, relative to torso span. 0 disables this transition. |
| Thigh side guard speed / `thighGuardSpeed` | 1.225 | Lower-side protection curve; 0 disables it. |
| Thigh bone guard / `innerThighGuardStrength` | 0 | Additional thigh-weight protection, off in the accepted preset. |
| Thigh transition width / `thighGuardWidth` | 0.2484375 | Smooths the thigh guard transition. |
| Lowest belly / pubic guard / `bottomGuardStrength` | 0.00625 | Localized protection at the very bottom of the abdomen, including pubic-weight influence. |
| Lowest belly guard width / `bottomGuardWidth` | 0.171875 | Lowest-belly transition width relative to torso span. |
| Belly sag / `bellySag` | 2.5 | Independent downward belly shift. |
| Surface slide / density / `sagStrength` | 1.0625 | Legacy surface-motion/density control, separate from downward sag. |

**One shared arc control drives both upper and lower bands.** Lower start, transition width, and curve bias control where/how the lowest abdomen attaches to the virtual belly pose; they do not independently set the geometric arc. The arc uses a fixed full-term center for the current shape settings, so its direction does not change as pregnancy advances or the character bends.

Breast ownership comes from Magica breast spring roots and descendant skin bones. At strength 1, weight 0.3 gives a base restore amount of 0.3, and weight 1 gives full protection. Fully protected vertices retain original geometry and native physics motion. Smooth transitions and seam handling share protection across body parts and nearby clothing.

Breast protection is enabled by default; optional upper-bone filtering and upper-field fade are disabled. All **81** serialized shape defaults match the latest accepted export and shipped preset. Older JSON fields retained only for compatibility may be ignored by the current growth kernel; use the active F8 controls for tuning.

## Saving settings and gameplay state

| Data | Location | Persistence |
| --- | --- | --- |
| F1 global settings | `BepInEx/config/local.al.pregnancy.preview.cfg` | BepInEx configuration. |
| Belly preset | `BepInEx/config/ALPregnancy_belly.json` | Loaded at startup; explicit values override defaults. |
| F8 shape edits | Running session | Live only; 0.2.40 has no automatic preset save button. |
| Pregnancy state | `<native-save-path>.alpregnancy.json` | Written alongside the selected native save when the game saves. |
| Diagnostic export | `BepInEx/plugins/AL_Pregnancy/diagnostics/latest-mesh.json` | **Write diagnostic log** exports settings and mesh data. |

To retain a tuned F8 shape, export diagnostics, copy the exported `settings` object into the preset's `vtx` object, preserve its wrapper (`version: 5`, `startDay: 40`), and save it at `BepInEx/config/ALPregnancy_belly.json` before restarting. The shipped preset shows the full format. `startDay` is retained for compatibility; it is not the current F1 growth schedule.

Back up/move a native save and its pregnancy sidecar together. The sidecar is checked against the native save; mismatched or damaged data is not silently applied to a different save. Check the BepInEx log if state cannot load. Debug changes alone do not write a save.

## Optional body changes: template cards

The release includes a default pair of female AmanatsuLocation character cards. Keep them beside the DLL, or replace both with your own **valid female character-card PNGs**, not ordinary screenshots:

```text
BepInEx/plugins/AL_Pregnancy/
  AL_Pregnancy.dll
  base.png
  reference.png
```

Create a base card, edit a copy's desired body properties in the creator, and save it as the reference. Keep unrelated properties identical. The plugin applies `reference − base` to each character's own original body; it does not replace the face, outfit, or identity. Missing/unreadable templates disable this feature while belly deformation continues. The supplied pair is the author-selected template from the installed plugin; replacing it changes the body-difference target.

Supported values: 42 body-shape channels; breast softness/weight; areola size; upper-arm, belly, waist, hip, and thigh softness; nipple color.

The default full-change count is **5 births**. For completed births `B`, current pregnancy progress `P` (0 when not pregnant), and count `N`, progress is `min(1, (B + P) / N)`. The first pregnancy therefore contributes up to 20% with default settings; completed births retain their contribution afterward. `N <= 0` means immediate full change.

F1 creator-value limits default to **0 / 100**, accept unrestricted integers, and are internally ordered if reversed. Limits apply to changed channels. Nipple-color progress is capped at **0.85**: `min(bodyProgress, 0.85)`, not `bodyProgress × 0.85`; 0 disables color change. Template files and original saved character data are not rewritten by this visual application.

## Clothing, skirts, and collision

Supported garments follow the body field, including supplemental `add_etc`, `add_arm`, and `add_leg` slots. Some CPU-unreadable supplemental meshes are reconstructed from original GPU buffers once per mesh lease. Failed readback and unsupported unreadable blend-shape meshes stay unchanged and log `[Clothing] Cannot read`. Acquiring a mesh may briefly synchronize the GPU during outfit changes.

Skirts use native chain hierarchy, support/release zones, and per-influence bind compensation. Lower hems retain native bone response; no separate skirt setup is needed in F8. Free accessories are not automatically treated as garments. Third-party outfits can still need individual compatibility work.

Low-detail collision meshes use their own skin weights with shared deformation/pose logic; existing Obi cache/index synchronization remains in place. This updates existing collision surfaces, not a guarantee of intersection-free animation or fluid behavior in every outfit and pose.

## Troubleshooting and validation limits

- **F8 missing:** check that BepInEx loaded 0.2.40, remove duplicate DLLs, and check the configured panel key.
- **F1 missing:** use a compatible configuration manager; F1 is not supplied by this DLL alone.
- **No preview character:** show a high-detail female character in the creator or scene.
- **Old defaults:** existing belly JSON takes priority. Default shape resets the session; installing the preset updates future launches.
- **No template changes:** check card names/location/format, turn off manual preview, and inspect gameplay pregnancy/birth state and the log.
- **Skipped/clipping garment:** inspect the log and compare without that outfit; readback does not support every mesh type.
- **Unexpected pose deformation:** compare neutral/problem poses with identical settings and export diagnostics for local investigation. Review exports before public sharing because they contain character geometry.

0.2.40 changes defaults only relative to 0.2.39. Production build and public offline regressions are reported in `VERIFICATION.txt`. Earlier curved-boundary replay at arc 0.35 still found extra intersections in an extreme thigh −60° pose. That replay does not validate all poses at the new 0.8 default. Continuous in-game animation and broad outfit/performance coverage remain outside these offline checks.

## Build and test

Requires Windows, **.NET SDK 10**, and local BepInEx/game interop assemblies. The plugin targets .NET 6; tests target .NET 10. Game, Unity, Obi, BepInEx, and IL2CPP assemblies are not included. The first build may restore framework reference packs.

```powershell
./Build.ps1 -GameDir 'D:\Games\AmanatsuLocation'
./Test.ps1 -GameDir 'D:\Games\AmanatsuLocation'
```

The build prefers `BepInEx/interop-AmanatsuLocation/interop`, otherwise `BepInEx/interop`; override with `-InteropDir` on Build.ps1. Alternatively use `dotnet build src/AL_Pregnancy.csproj -c Release -p:GameDir="D:\Games\AmanatsuLocation"`, or set `AL_GAME_DIR`. Output: `src/bin/Release/net6.0/AL_Pregnancy.dll`.

Test.ps1 runs ten offline suites: breast-physics ownership/axis driver, unrestricted inputs, main shape/gameplay/defaults, collision cache, readiness, mesh lifetime, shared-source lifetime, clothing readback, skirts, and body changes. Main tests need local BepInEx.Core; the others use test doubles. Full game-geometry captures are not distributed or required. Optional asset replay needs a separately supplied matching fixture.

## Repository layout

- `src/`: plugin runtime.
- `presets/`: complete accepted shape defaults.
- `templates/README.md`: optional body-card setup.
- `tests/`, `*-tests/`, `test-support/`: offline regressions and test doubles.
- `VERIFICATION.txt`: validation scope and limitations.

Prebuilt binaries belong in Releases. The selected template pair is in `templates/`; full diagnostic captures, saves, and local configuration are excluded. No source license has been selected on the author's behalf.
