# Custom flag material remapping — 22 October 2026

## Current result

**Later update:** corrected v6 now passes embedded normal loading and visible waving. See the [embedded proof report](custom-flag-embedded-breakthrough-2026-10-22.md). The following sections retain the earlier loose-file checkpoint and rejected v3–v5 history.

**A custom swallowtail flag now waves through ordinary user-item placement without a flag companion or runtime renderer writes. Embedded-only cold loading is not yet working.** This is a new intermediate result; it does not establish a portable finished item.

The item uses the engine's existing `MaterialModifier` mechanism. Its mesh initially references a separate early `CPlugMaterial` placeholder. A GameSkin identifies the exact placeholder FID (`UseFile=true`), and a modifier resolves replacement `ItemFlag` under the native global folder `Stadium\Media\Material\`. During ordinary item construction, the engine replaces the material and initializes the effective mesh.

## Observed evidence

Trackmania build: `2026-02-02_17_51`, executable SHA-256 `3FC7D8CDA542BEDA131C44306B123F4004D07D7E22F512B46B762AFC29F6EDDA`.

| Test | Observed result |
|---|---|
| Cold startup, no dedicated TMIS flag or placement helper | Passed; plugin baseline retained in `cold1/post-start-baseline.json`. |
| Ordinary inventory registration | Both v3 items appeared after the full inventory scan; 8,425 total inventory items. An immediate query during refresh incorrectly suggested zero items. |
| Ordinary placement using the inventory's exact path | Passed; map count changed from 0 to 1. |
| Source mesh before placement | 0/5 structured flags; the material source FID matched the GameSkin source. |
| Effective instance graph | Normal Prefab → Dyna → cloned Solid2, 5/5 visuals with `0x08000000` enabled, each retaining 86 animation frames. |
| Effective early material | Pointer exactly equals the native packed `ItemFlag.Material.Gbx` node. |
| Visible animation in editor playground | Passed; genuine 12-second 1920×1080 recording, 714 frames, mean 59.5 fps from a 60 Hz capture. No motion interpolation. |
| Native editor save/embedding | Failed with the game's explicit item serialization/embedding warning. No saved-map success inferred from the RPC response. |
| Manually packaged map, all five GBX files embedded, unique item folder, loose v3 files absent, fresh process | Failed: game crashed while loading. Same fixture will not be retried unchanged. |

Evidence directory: [`artifacts/flag-selfcontained-20261022`](../artifacts/flag-selfcontained-20261022).

- [Actual animation recording](../artifacts/flag-selfcontained-20261022/cold1/loose-user-flag-60fps.mp4)
- [Three real frames from the recording](../artifacts/flag-selfcontained-20261022/cold1/loose-motion-comparison.jpg)
- [Effective model and visual flags](../artifacts/flag-selfcontained-20261022/cold1/effective.json)
- [Native material identity](../artifacts/flag-selfcontained-20261022/cold1/native-material.json)
- [Isolated embedded-map manifest](../artifacts/flag-selfcontained-20261022/material-remap-embedded/TMIS_MaterialRemap_Embedded_20261022.Map.Gbx.manifest.json)
- [Fresh crash log](../artifacts/flag-selfcontained-20261022/cold2/crash/fresh-LogCrash_00000000003E7E50.txt)

## Embedded crash and next controlled change

The fresh crash at 10:01:12 reads address `0x238` with a null RCX in native function `0x1403E7E50`. Its caller obtains a shader from a material via `0x14040F750` and passes the result without a null guard. The current placeholder deliberately has zero device-material/shader slots, so it provides a concrete explanation for this failure path. The exact crashing material was not sampled before termination; attribution to the placeholder remains a strongly supported hypothesis.

Further controlled changes exposed two additional native-format boundaries:

- **V4:** a genuine ordinary ZOnly material with an inline ShaderApply failed during native loading, even in an empty map. The crash at `0x140912443` normalized a null FID. Static follow-up found that the native material's `DeviceMat.U03=true` had been retained while the generic GBX.NET writer emitted an inline node. The native reader treats that selector as a FID reference, and always treats the two tail shader slots as FID references. A GBX.NET reparse did not catch this native incompatibility. External HLSL path failure alone was not proven.
- **V5:** `U03=false`, one inline primary shader, null tail slots, and no external script references moved loading past the material reference reader. It then crashed inside shader pipeline metadata copying at `0x1404908F9`, reading a null parameter array. Emptying script references while retaining the genuine pipeline graph is therefore insufficient. This fixture was also rejected without a repeat.

These failures occurred before candidate placement; neither is evidence about the final remapped flag's rendering. A possible no-pass source shader is being audited before another native load. An empty embedded map permits native preload and shader inspection before any candidate instance is built or drawn. A successful preload must still be followed by effective-graph, animation, cold-start and embedded-only checks.

The independent [native-save audit](../artifacts/flag-selfcontained-20261022/material-remap-embedded/native-save-audit/findings.md) identifies the editor's embedding output under `:temp:\MemoryTemp\CurrentMap_EmbeddedFiles\ContentToSave\`. Retained user-FID dependencies encounter a native cross-drive reference gate. The exact first rejected FID was not sampled, so this is a mechanism-supported explanation rather than a measured identity. `RemapFolder` is serialized separately as a global-relative string and was ruled out as that external-FID rejection. A manually packaged map and native editor resave support must be verified separately.

## Limits

This experiment did not change Editor.op, the injection proxy, native packs, shader caches or original user items. It did not demonstrate rotation together with cloth waving, multiple-shape embedded operation, operation without Openplanet, or another player's machine. The installed-game-folder route documented in earlier reports remains distinct from this ordinary user-folder result. A single ordinary `.Item.Gbx` with native editor embedding is not yet available.
