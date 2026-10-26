# Hidden FX catalog and candidate carriers ? 25 October 2026

This catalog inventories the local Trackmania extraction. An asset name proves that the game contains the asset; it does not prove that the asset can be used as a standalone `.Item.Gbx`, rendered, or preserved after map save/reopen.

## Direct item carriers

| Effect | Carrier | Dependencies | Evidence status |
|---|---|---|---|
| Fogger 8/16 m | `Stadium/Items/ShowFogger8M/16M.Item.Gbx` plus prefab | Fogger FxSystem/particle model, `FoggerSmoke.Texture.gbx` | Custom carriers proven |
| Sparkler 8/16 m | `Stadium/Items/Sparkler8m/16m.Item.Gbx` plus prefab | Two particle emitters and Sparkler textures | Custom carriers proven |
| Podium Sparkler | `PodiumSparkler.FxSys.Gbx` | PodiumSparkler and PodiumSparklerEnd particle models | Particle-model preload passes; no item carrier found |
| Torch flame/smoke | `ShowTorch.Item.Gbx`, `ShowTorchSmall.Item.Gbx` | Torch prefab, TorchSmoke FxSys/particle model, TorchLight, flame material | Stock item preload/placement/visible flame/save/reopen pass; user identity clones fail preload |
| Fogger with light | `ShowFoggerWithLight8m/16m.Item.Gbx` | Fogger carrier plus `FoggerSpot.Light.gbx` | Stock 8 m preload, visible fog/light and save/reopen pass; custom clone proof pending |

## Particle-model candidates

| Candidate | Likely route | Status |
|---|---|---|
| `FlyingLeaves_BaseModel`, `InFogEmitter` | VFX or particle wrapper | Assets found; no item route |
| `WaterSpray`, `WaterSprayThin` | Decorative sprinkler | Native particle preload; vehicle archive and no item wrapper |
| `WaterSprayAndSteam` | Water plus steam sprinkler | Native particle preload; no item wrapper |
| `WaterSplash`, `CameraWaterDroplets`, `UnderWaterBubbles_*` | Splash/bubbles item | Native or corpus discovery; vehicle/water context unresolved |
| `WaterWake*`, `LightTrail` | Moving/dynamic carrier | Context-bound |
| `ExhaustFlame`, `VehicleOnFire`, vehicle smoke | Vehicle carrier | Context-bound |
| `Sparkles`, `Boost_*`, `WingTipVortices` | Energy/boost carrier | Context-bound |
| ShootMania explosions, impacts, lasers, debris | Mode-specific graph | Game assets; no Trackmania item proof |

## Textures and materials

Useful candidate resources include `ProceduralFire_Color`, `FireColorOverLife`, `WhiteSmoke`, `WHITE_SMOKE_2`, `Confettis`, `Sparkle`, `AnimFire_D`, `WaterSplash`, `AirBubble_*`, `LensFlares`, LightTrail materials, and `TorchFlame.FuncShader`. These are dependencies, not standalone FX. They must be referenced by a valid graph and pass native tests.

## Native probe ladder

A candidate is promoted only after: native preload, item placement, visible local play, map save/reopen, and a cold process check. The exact preload rows are in [`hidden-particle-preload-evidence.json`](../../artifacts/fx-clones/hidden-carrier-tests-20261025/hidden-particle-preload-evidence.json). The official Torch screenshot and map evidence are in [`native-hidden-carrier-summary-20261025.json`](../../artifacts/fx-clones/hidden-carrier-tests-20261025/native-hidden-carrier-summary-20261025.json).

The current studio preserves unknown FX bytes and writes only the validated emitter origin. New graph nodes, new dependencies, and nested particle values remain native-probe-only.

See the consolidated English index: [`fx-research-index-2026-10-25.md`](../fx-research-index-2026-10-25.md).

## Complete particle-model inventory

The machine-readable inventory [`fx-hidden-catalog-full.tsv`](evidence/fx-hidden-catalog-full.tsv)
covers all **83** `CPlugParticleEmitterModel` paths found by the local extraction
audit. It records parser status, native-preload status, archive/context, item
wrapper status, and the resulting Studio route for every row. A complete
read-only preload pass is recorded in
[`hidden-particle-native-preload-complete-2026-10-26.json`](evidence/hidden-particle-native-preload-complete-2026-10-26.json).
Use [`effects-asset-index.tsv`](evidence/effects-asset-index.tsv) alongside it
for the non-particle carrier closure: item roots, prefabs, FX-system files,
textures, and material dependencies.
The counts are:

| Inventory result | Count |
|---|---:|
| Parsed by GBX.NET | 61 |
| Not parsed by GBX.NET | 22 |
| Native-preloaded in the complete read-only preload pass | 54 |
| Native preload failures | 7 |
| Unparsed and therefore skipped by the preload pass | 22 |
| Proven Stadium Fogger/Sparkler item-carrier particle models | 6 |
| Native stock Torch reference model | 1 |
| Context-only or wrapper-needed candidates | 48 |
| ShootMania context-specific candidates | 28 |

`native-preload` means the game accepted the original read-only FID. It does
not mean the asset can be attached to an item. `item-carrier-proven` is reserved
for the existing Fogger/Sparkler carrier graph, which already has item,
prefab, FX-system, placement, and save/reopen evidence. Vehicle, Effects, and
ShootMania rows remain discovery candidates until a complete wrapper passes
the same native ladder.
