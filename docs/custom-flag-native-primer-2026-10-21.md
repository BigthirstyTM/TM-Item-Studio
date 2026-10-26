# Custom flags: native primer and local shader-cache tests

21 October 2026. Report language: English.

**One native flag is not a working initializer for the tested custom flags.**
It makes cloth appear, but the long, swallowtail, and round models render
native-sized rectangular cloth instead of their custom outlines. A standalone
animated custom flag is still unproven.

## Recording

[Download the native-primer test video](../artifacts/flag-native-primer-20261021/native-primer-wrong-shapes.mp4).
The recording is 1920 × 1080, 17.983 seconds, 1,069 captured frames, averaging
59.44 fps. It shows an overview and closer views. Its English caption explicitly
identifies the wrong outlines and says this is not a working solution.

For comparison, the earlier [working three-shape recording](../artifacts/flag-round-video-20261021/custom-flags-swallowtail-long-round.mp4)
shows the correct outlines with temporary runtime initialization. That earlier
recording does not prove standalone persistence.

The dedicated flag helpers `TMISFlagGpuPlacement`, `TMISFlagMaterialProbe`, and
`TMISVanillaFlagCopy` were absent during the primer tests. Openplanet, Editor++,
the control bridge, and the existing material crash-workaround baseline remained
installed. These were not plugin-free game sessions.

## Native-primer matrix

All maps are disposable test maps. Cold tests use saved embedded items with the
loose primer Items folder removed. Measurements inspect the actual placed item
models. Each model has five animated visual nodes with 86 frames each.

| Test | Visible result | Custom structured-buffer initialization |
| --- | --- | --- |
| Native flag drawn before custom placement | Cloth appears; custom outlines are lost | 0/5 per custom model |
| Native-first saved map, cold process | Cloth appears; custom outlines are lost | 0/5 per custom model |
| Custom-first saved map, separate cold process | Cloth appears; custom outlines are lost | 0/5 per custom model |
| Custom-only saved map, another cold process | Poles only; no cloth | 0/5 per custom model |
| Native flag comparison | Normal native flag | 5/5 |

The custom-only screenshot and measurement were taken before any diagnostic
preload of the native flag. The read-only probe now has `--no-native` for this
purpose. Merely loading a native comparison must not contaminate that control.

Source and extracted embedded geometry were compared independently. All five
visuals of all three models retain identical XYZ hashes and frame counts. Saving
the map did not replace their geometry with rectangles. The visible result is
consistent with stale/shared native GPU-buffer state, but no new GPU capture in
this round proves the exact buffer identity.

Selected evidence under `artifacts/flag-native-primer-20261021/`:

- `07-native-first-three-custom.webp` and `native-first-visuals.json`.
- `cold-native-first-embedded-only.webp` and `cold-native-first-visuals.json`.
- `cold-custom-first-embedded-only.webp` and `cold-custom-first-visuals.json`.
- `cold-custom-only-before-native-preload.webp` and `cold-custom-only-visuals.json`.
- `source-vs-embedded-geometry.json`.

## Stable reference variants

Seven additional address-free item variants were staged and preloaded natively.
Three use named material routes (`ItemFlag`, the Stadium material path, and the
GameData material path). Four use complete inline material/parent nodes with
shader-reference ancestor levels 1–4.

All seven load as item models, but none initializes any of its five custom
structured-buffer flags. The named routes leave the inspected runtime material
arrays empty. The ancestor routes have shader FIDs but no cached effective
shader. None was placed/rendered as a successful candidate. The receipt is
`route-native-preloads.json`.

An initial inventory attempt occurred before actual staging completed and is
retained separately as `route-pre-staging-inventory-errors.json`; it is not
counted as a candidate failure.

## Corrected HLSL interpretation and real compiled-cache package

The correct native shader-source path is
`GameData/Shaders/Tech3/Warp_TDiffSpec_VertexTween_v.hlsl`. The previously checked
`Shaders/Tech3/...` path omitted `GameData/`. Its empty extraction did not establish
that the actual native HLSL resource was unavailable.

The correct native FID is virtual and loads as `CPlugFileVHlsl`. The game ships
compiled resources in `Packs/GpuCache_D3D11_SM5.zip`. Static inspection shows
startup registration scans `.GpuCache.Gbx` entries and creates virtual HLSL FIDs
under the selected shader root. A cache node is `CPlugGpuCompileCache`, not a
shader or HLSL node, so substituting it into a shader's script reference would
not be type-correct.

A new address-free local package preserves the actual extracted native shader
and transitive non-HLSL dependencies, and supplies all nine relevant compiled
caches, including the DeferredWrite BlackNoSpec dependency. No HLSL placeholder
or fake source was created. The installation cache and game executable were not
modified.

After a cold game restart with that package staged:

- The local vertex-cache file loads as `CPlugGpuCompileCache`.
- Preloading it does not register/load the corresponding local HLSL FID.
- The local Shader.Gbx preload returns null.
- The item loads, but all five animated visuals remain uninitialized (0/5).

This rejects the tested local compiled-cache arrangement. It does not prove
that all possible packages or alternate native loading paths are impossible.
The candidate was not placed with an unresolved effective shader.

Evidence: `local-cache-package/dependency-manifest.json`,
`compiled-cache-manifest-complete.json`, `cold-preload-results.json`, the archived
pre-restart log, and recorded bridge calls. The package generator is
`Tests/FlagProbe/package_flag_shader.py`.

## Remaining requirement

The custom model must obtain its own structured vertex buffer before its first
draw. Native Solid2 initialization can do this when a valid effective shader is
available early enough; the previous session-specific positive control proves
that mechanism, but cannot be reused after restart. A distributable solution
still needs a stable native asset/reference or loading representation that
triggers this preparation from saved bytes alone, followed by cold-process,
embedded-only, all-shape animation tests.

Adding one native flag is not sufficient evidence for that requirement.
