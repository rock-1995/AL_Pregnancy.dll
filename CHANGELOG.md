# Changelog

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
- Translate F1 descriptions and repository documentation into English.
- Preserve the legacy plugin ID and all configuration keys for compatibility.
- No changes to gameplay, physics, geometry, defaults, or save formats.

## 0.2.25

- Adopt the accepted F8 defaults and restore the original F1 defaults.
- Add configurable growth milestones with linear interpolation.
- Replace per-pregnancy random duration with independent daily growth variation.
- Retain the existing Obi global mesh-index synchronization fix.
