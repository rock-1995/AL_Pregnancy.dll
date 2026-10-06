# Changelog

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
