# Changelog

## 0.3.5

- Give the native character-editor pregnancy controls the game's blue panel background and standard two-line slider layout. Keep both rows at the native height with consistent spacing to prevent overlapping labels.
- Preserve the native numeric-input, reset and Undo bindings. This is a layout change; appearance values, pregnancy progression and save behavior are unchanged.
- Build verified; the revised layout still needs an in-game visual recheck.

## 0.3.4

- Add the optional low-detail belly input bridge used by AL Collision Plus 0.2.0. It refreshes pregnancy inputs for native breast collision when liquid helpers are inactive, without enabling liquid GameObjects or changing the native fluid mesh submission/Obi path.
- Retain the 0.3.3 body, clothing, gameplay and save behavior. No new pregnancy setting or preset migration is required for this bridge.

## 0.3.3

- Create private readable copies for every recognized unreadable garment, including native pantyhose waist/abdomen panels, instead of limiting this path to supplemental slots. Reuse valid copies across clothing visibility changes.

## 0.3.2

- Restore pose-following when native clothing updates replace a garment's `sharedMesh`; rebind only changed garments while retaining valid body and clothing state.

## 0.3.0–0.3.1

- Add the native character-editor **妊娠** page with separate **お腹** and **体型** appearance sliders. Confirmed in-game edits persist through the normal save; cancelled edits and standalone creator previews do not write character cards.
- Store appearance values in companion-save format v4. Migrate v1–v3 in memory and write v4 on the next normal save. Version 0.2.44 cannot read v4; keep paired native-save/sidecar backups before upgrading.
- Publish appearance changes from pregnancy progression, birth history and relevant settings, while reducing repeated stable-frame body-data copies and native refresh work.
- Coalesce native clothing notifications at the completed Human update and preserve unchanged mesh leases, virtual bindings and collision state. Retain the original deformation, protection and native skinning behavior.

## 0.2.45–0.2.47 (local updates)

- Cache exact garment-to-body correspondence while its source geometry and transform remain unchanged, reusing the established nearest-vertex result.
- Reduce repeated collision-reader scratch allocations and retain completed-update collision scheduling.

## 0.2.44

- Apply template body-slider changes to enabled clothing shape corrections, using each garment's original correction values and the existing progression and slider limits.
- Supply the same corrected values to native animation reads. Preserve garment selection and restore saved body/clothing data after each update, including exceptions and reloads.

## 0.2.43

- Set Forward fullness (`growthFullness`) to **1.10**, previously 1.116265, in both built-in defaults and the supplied preset. The other 80 shape defaults are unchanged.
- Retain the 0.2.42 animation contact correction without runtime algorithm changes.
- Keep the README focused on current features, controls and saved settings; move release-specific discussion to the changelog and validation file.

## 0.2.42

- Fix hand/chest separation in grabbing and supporting animations when pregnancy or postpartum body changes alter chest size. The reported case was confirmed fixed in-game.
- Supply the committed visual body during native MotionIK/HActor contact reads and eight motion controllers' animator parameter reads. The latter use an inlined getter that bypassed the initial 0.2.41 correction.
- Refresh native animation parameters after live body changes, including F8 pregnancy-progress edits made after scene entry. Refresh the original values when body changes are removed; defer refresh until the scene is ready.
- Restore original character-data references after every scoped read, including exceptions and nested/multiple-actor calls. Retain native cover handling.
- Add animation regression coverage: 923 body/animation checks pass. Keep deformation, breast/thigh protection, curvature, template cards and all 81 defaults unchanged from 0.2.40.
- Synchronize F8 and diagnostic version labels. No new setting or preset migration is required.

## 0.2.41 (local candidate)

- Scoped native IK and actor shape reads to the visual body. This did not resolve the reported pose because the separate animator parameter path was still reading the original card; 0.2.42 completes the correction.

## 0.2.40

- Make all 81 accepted shape settings the built-in defaults and ship the matching preset.
- Relative to 0.2.39, update below-breast guard width to 0.2078125, height boundary arc to 0.8, upper transition start to 0.22, and breast guard strength to 1. Other defaults and runtime algorithms are unchanged.
- Expand the English guide with gameplay modes, F1/F8 operation, curvature/protection controls, preset persistence, template-based body changes, and validation limits.
- Publish ten offline regression suites without requiring private game-mesh captures. Keep the breast-asset replay optional through a fixture-path argument.

## 0.2.39

- Replace straight height bands with a shared curved material-height coordinate. Upper bands arch upward at the center; lower bands dip downward.
- Add Height boundary arc (0 = straight), shared by shape fields, pose transitions, and protection boundaries.
- Keep a fixed full-term center across growth stages and animation poses. Preserve physical-space handling for contact, sag, and gradients.

## 0.2.38

- Scale breast protection continuously by actual breast-physics-chain skin weights and Breast guard strength.
- Preserve native geometry/motion at full protection and blend partial protection smoothly, including seam and shading handling.

## 0.2.37

- Add smooth below-breast, thigh-side/thigh-weight, and lowest-belly/pubic guards inspired by the COM3D2 protection approach.
- Share protection between static shape and pose blending, body parts, nearby clothing, and low-detail collision deformation.

## 0.2.36

- Resolve breast ownership from Magica breast spring roots and their descendants instead of chest/sternum names.
- Exclude waist jiggle from virtual-axis carrier motion while retaining the body's native physics response.

## 0.2.28–0.2.35 development series

- Add native skirt-chain support/release, per-influence bind compensation, and native hem motion (0.2.28).
- Add card-difference body changes with persistent birth-count contribution, F1 limits, and nipple-color progress cap; correct IL2CPP nullable setter handling (0.2.29–0.2.30).
- Add independent downward Belly sag alongside the legacy surface slide control, numeric Apply inputs, and updated pose/attachment presets (0.2.31–0.2.32).
- Preserve committed native body customization across readiness changes; apply belly/clothing geometry before native body customization and bind the final pose afterward (0.2.33–0.2.34).
- Allow finite numeric shape inputs outside recommended slider ranges (0.2.35 candidate and subsequent builds). The intermediate candidate was not accepted as a standalone release after its offline intersection evaluation.

## 0.2.27

- Include AL supplemental clothes in the existing deformation pipeline, including the reported heart waist chain.
- Add a readable private GPU-buffer copy for unreadable supplemental clothes, retaining original mesh restoration and ownership.
- Reject incomplete copies and unreadable blend shapes without replacing the visible source.
- Add selection, buffer integrity/failure, source-lifetime regressions and an optional local asset replay.
- The reporter confirmed the supplied waist-chain coordinate works in game.
- Preserve belly shape, growth logic, settings, presets, collision behavior and free-accessory exclusions.

## 0.2.26

- Rename the plugin assembly and installation directory to `AL_Pregnancy`.
- Rename the panel controller to `PregnancyController` and update the window title and diagnostic path.
- Update the F1 setting descriptions and usage documentation.
- Preserve the legacy plugin ID and all configuration keys for compatibility.
- No changes to gameplay, physics, geometry, defaults, or save formats.

## 0.2.25

- Adopt the accepted F8 defaults and restore the original F1 defaults.
- Add configurable growth milestones with linear interpolation.
- Replace per-pregnancy random duration with independent daily growth variation.
- Retain the existing Obi global mesh-index synchronization fix.

