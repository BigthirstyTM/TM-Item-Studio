# Embedded custom flag proof — 22 October 2026

## Current result

**A custom swallowtail flag now loads from the map's embedded files and visibly waves, without a flag companion or renderer-memory writes.** This is a first embedded normal-load proof. A cold restart without Openplanet and native editor resaving are still pending at this checkpoint; portability to an unmodified client is not yet claimed.

The map contains the custom item and its source material, GameSkin and MaterialModifier. The engine uses its existing material remapping path to replace the ordinary source material with the native packed `ItemFlag` material, then rebuilds the effective mesh and its animation buffers. No native flag item is placed in this map.

## Reproducible evidence

- [12-second actual game recording](../artifacts/flag-selfcontained-20261022/embedded-v6-upright/embedded-swallowtail-actual-60hz.mp4): 1920×1080, 717 captured frames, mean 59.75 fps. Desktop Duplication at requested 60 Hz; no interpolation or speed change.
- [Three actual moments](../artifacts/flag-selfcontained-20261022/embedded-v6-upright/embedded-motion-comparison.jpg).
- [Motion measurement and video hash](../artifacts/flag-selfcontained-20261022/embedded-v6-upright/motion-validation.json): 599 distinct cloth crops across a ten-second fixed-camera sample. Median adjacent difference 2.216 gray levels, versus 0.012 in the background control.
- [Effective graph](../artifacts/flag-selfcontained-20261022/embedded-v6-upright/effective.json): normal Prefab → Dyna → cloned Solid2; all five cloth visuals have the structured-buffer flag, retaining 86 animation frames each.
- [Native material identity](../artifacts/flag-selfcontained-20261022/embedded-v6-upright/native-material.json): effective early material equals the exact native packed `ItemFlag.Material.Gbx` node.
- [Verified runtime verdict](../artifacts/flag-selfcontained-20261022/embedded-v6-upright/verdict.json).
- [Map and embedded file manifest](../artifacts/flag-selfcontained-20261022/material-remap-embedded-v6/TMIS_MaterialRemap_Flag_v6_Upright.Map.Gbx.manifest.json).

Map SHA-256: `1FC34C734D0B1CDDFA99DD755E507C95437637E20041C4D1FCD9E9387AA2AE3A`.

Video SHA-256: `3E376CB8D6FA3662C0CEAB874753818E38AC7213A892607C6507922F5402016C`.

## Controlled change that enabled embedded loading

V3's shaderless source material worked as an ordinary user item but crashed the embedded loader's early blend-state query. V4 exposed a native FID-versus-inline serialization mismatch. V5 corrected that mismatch but retained shader pipelines with missing script metadata, causing a different null dereference.

V6 stores one genuine inline `CPlugShaderApply` with an explicitly false first-shader selector, null FID tails, zero render passes and null optional legacy script fields. It has no external shader dependencies. The [native static audit](../artifacts/flag-selfcontained-20261022/no-pass-audit/findings.md) supported one bounded preload. That preload passed before an anchored item was attempted: non-null source ShaderApply, exact source material FID matching the GameSkin, valid native replacement folder, source mesh 0/5 structured visuals. The subsequent ordinary map load produced 5/5 effective structured visuals and visible motion.

The zero-pass source is a pre-remapping placeholder, not a drawable replacement for the native flag shader. The effective instance uses the native shader after remapping.

## Limits and remaining gates

### Subsequent cold-load results

Two fresh-process checks have now failed to remap: the three-shape map and the byte-identical single upright map. Both loaded without crashing, but effective meshes remained on the original source material, with 0/5 structured visuals per flag. The same original single map then passed when loaded in the already running session after another map. Thus the linked animation is valid embedded **warm-session** evidence; cold persistence is not solved.

The three-shape serialization audit ruled out lost modifier data: after normalizing the two item Name strings and their header sizes, its swallowtail is byte-identical to the accepted single candidate. Native cache inspection shows a real material-remap stage, correct skin/folder parameters, a matching source FID, and a cached null output falling back to the original model. Later valid metadata does not establish its state at the first build. Replacement loading is synchronous once its FID is found; simply preloading the shader is not established as the cause.

Native editor resaving did create a real three-item map without a warning. Its ZIP contains three newly serialized items and omits the separate material/skin/modifier files. That is a file-production result only; a dependency audit and cold reopen are required before calling it a successful portable save. The earlier SaveMapAs attempt while switched to the playground produced no file and is not counted as a save.

A new bounded control places a genuine native Flag16m anchor before the custom item. This retests the user's primer idea specifically with the new remapping mechanism; older primer failures concerned different item graphs.

General Openplanet, Editor++ and the diagnostic bridge were loaded during this checkpoint. The dedicated TMIS helpers were absent. The original user fixtures had been removed before the embedded tests; v6 was never installed in the user Items or game Items folder. However, a fresh process without Openplanet must still prove that no general plugin or already loaded state is needed.

The first one-anchor map had an incorrect pitch/yaw order; its runtime graph passed but it was superseded by the upright map above. Two GDI window recordings showed a stale editor surface and were rejected as animation evidence. The linked recording uses the current monitor's Desktop Duplication surface and was visually checked.

Next gates are identical-file cold loading, no-Openplanet loading, native editor resaving with inspection of the saved ZIP, and long/round shape checks. Simultaneous kinematic rotation and cloth waving remain a separate unsolved requirement.

Environment: Trackmania build `2026-02-02_17_51`, executable SHA-256 `3FC7D8CDA542BEDA131C44306B123F4004D07D7E22F512B46B762AFC29F6EDDA`.
