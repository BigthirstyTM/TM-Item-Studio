# Trackmania custom FX research index ? 25 October 2026

This is the English reference for the custom Sparkler, Fogger, sprinkler, and hidden FX investigation. The evidence vocabulary is strict: **parsed** means a file reader accepted the bytes; **preloaded** means the game loaded the FID; **placed** means the editor stored an item; **visible** means a screenshot or viewport observation shows the effect; **playable** means a local play observation; **persistent** means save/reopen preserved the result. A lower level is never promoted to a higher one.

## Current product capability

TM Item Studio can preserve an existing embedded `CPlugFxSystem` (`0x0915C000`) and edit the validated particle-emitter origin. Sparkler emitters are translated together, so existing relative spacing is preserved. The browser export writes the `.Item.Gbx`; the optional `.fxsettings.json` is diagnostic only.

The custom Sparkler and Fogger fixtures load in Trackmania, survive map packaging/save/reopen, and use different static meshes. The packaged map embeds the exact item bytes and has no loose user-item dependency. Standard game assets may still be resolved from the Trackmania installation; this is different from a fully self-contained custom texture graph.

The FX graph itself remains opaque. Spawn/count, period, lifetime, size, spread, color/alpha curves, gravity, friction, particle collision, render mode, atlas, texture replacement, simulated smoke, GPU-spawn values, graph composition, and kinematic conversion are preserve-only or unavailable until a native one-variable fixture proves loading, visible output, and persistence.

## Native route status

| Route | Native evidence | Studio status |
|---|---|---|
| Existing embedded Sparkler | Two emitters; origin splice reparses; custom mesh/map package visible | Supported origin edit; graph preserved |
| Existing embedded Fogger | One emitter; origin splice; rate fixtures preload; rate-0 placement/save/reopen | Supported origin edit; GPU fields preserve-only |
| Stock `ShowTorch` | Game FID preload, placement, visible flame screenshot, save/reopen | Stock carrier reference only |
| Stock `ShowFoggerWithLight8m` | Preload, placement, visible fog/light capture and map save/reopen | Stock template reference; custom dependency-preserving clone still needs proof |
| User Torch identity clone | Native preload fails; not inventory-visible | Do not distribute |
| `PodiumSparkler*.ParticleModel.Gbx` | Native particle-model preload | No item carrier |
| `WaterSpray*` / `WaterSplash` | Native particle-model preload; vehicle archive; `idName=Unassigned` | No item carrier or sprinkler proof |
| Kinematic opaque FX | Current converter rejects missing static object model | Unavailable |
| Add FX / graph merge | No native graph merge and save proof | Disabled |

Native evidence: [`fx-native-test-status-2026-10-25.md`](evidence/fx-native-test-status-2026-10-25.md), [`native-hidden-carrier-summary-20261025.json`](../artifacts/fx-clones/hidden-carrier-tests-20261025/native-hidden-carrier-summary-20261025.json), and [`hidden-particle-preload-evidence.json`](../artifacts/fx-clones/hidden-carrier-tests-20261025/hidden-particle-preload-evidence.json).

## Hidden FX catalog and carrier tiers

### Tier 1: proven or directly usable item carriers

- `ShowFogger8M/16M.Item.Gbx` and `Sparkler8m/16m.Item.Gbx`: proven carrier families.
- `ShowFoggerWithLight8m/16m.Item.Gbx`: Fogger carrier plus a `FoggerSpot` light. The stock 8 m route now has placement, visible fog/light and save/reopen evidence; a custom clone still needs dependency-preserving proof.
- `ShowTorch.Item.Gbx` and `ShowTorchSmall.Item.Gbx`: official Stadium carriers with torch flame, smoke, light, and mesh. Stock placement/render/save is proven; identity-only clones fail because bare prefab references do not resolve as portable user dependencies.

### Tier 2: native particle assets without a proven item wrapper

- `PodiumSparkler.ParticleModel.Gbx` and `PodiumSparklerEnd.ParticleModel.Gbx`.
- `WaterSpray.ParticleModel.Gbx`, `WaterSprayThin.ParticleModel.Gbx`, `WaterSprayAndSteam.ParticleModel.Gbx`, and `WaterSplash.ParticleModel.Gbx`.
- `FlyingLeaves_BaseModel.ParticleModel.Gbx`, `InFogEmitter.ParticleModel.Gbx`, `UnderWaterBubbles_*`, `WaterWake*`, `LightTrail`, and vehicle smoke/fire/boost models.

These assets can be loaded or parsed in their original context, but an item wrapper, dependency closure, placement, visible gameplay, and save/reopen proof are still required. Vehicle and ShootMania assets may depend on owner variables, velocity, collision context, or mode-specific render setup.

### Tier 3: textures, shaders, and scene effects

Examples include `FoggerSmoke`, `Sparkler_D`, `SparklerEnd_D`, `WaterSplash`, `WhiteSmoke`, `WHITE_SMOKE_2`, `Confettis`, `ProceduralFire_Color`, `FireColorOverLife`, `LensFlares`, `LightTrail*.Material`, `TorchFlame.FuncShader`, and SceneFx bloom/color/depth assets. A texture or shader is not an FX by itself; it must be referenced by a valid FX graph and then tested natively.

### Tier 4: context-specific effects

ShootMania explosions, muzzle flashes, rail impacts, lasers, debris, vehicle exhaust, vehicle fire, boost, wake, and wingtip vortices are real game assets but are not automatically Trackmania item effects. They should be investigated only after a carrier/wrapper and context variables are identified.

## Full setting inventory

The engine metadata and GBX.NET definitions expose these candidate groups:

- **FX node:** root composition (`Parallel`, `Condition`, `SubFxSystem`, `UpdateVar`, `SoundEmitter`), joint, local/world offset, direction expressions, world velocity, frequency/scale/opacity modifiers, ambient/hue/water variables.
- **Emitter:** `IsSplashMode`, shadow-map texel size, vortex controls, radius, period, strength, lifetime, and submodel references.
- **Spawn/life:** emitter type, max particle count, start/end time, period, minimum distance, `Life`, `LifeVariation`, one-particle lifetime.
- **Size:** birth size/variation/ratio, intensity/speed/zone scaling, size-over-life, non-uniform X size, mesh scale/ramp, beam length.
- **Color/alpha:** color gradients, constant or color-over-life mode, RGB/hue modes, intensity modulation, transparency birth/variation/over-life, color-alpha coupling.
- **Physics:** weight, fluid friction, relative-to-emitter, particle collision, bounce, damper, and radius.
- **Rendering:** quad, speed quad, line, beam, mesh, triangle, light-only, water splash, camera droplets, sprite sorting, trails, roll, atlas mode/dimensions/index/animation, material/shader/mesh references.
- **GPU/fog:** textures 1?4, `IsFogEmitter`, `FogEmissionRate`, GPU spawn box/fields, simulated-smoke lifespan/radius/gravity/curves/filament settings.
- **Water:** `WaterSplashVelY`, water render modes, and `CPlugDynaWaterModel`. DynaWater is a waterfall/force model; it does not install a block water volume from an item.

The machine-readable matrix is [`fx-settings-matrix.tsv`](evidence/fx-settings-matrix.tsv). The complete 83-row carrier inventory is [`fx-hidden-catalog-full.tsv`](evidence/fx-hidden-catalog-full.tsv), with its read-only complete preload evidence in [`hidden-particle-native-preload-complete-2026-10-26.json`](evidence/hidden-particle-native-preload-complete-2026-10-26.json): 54 native preload passes, 7 null-nod failures, and 22 parser skips. The 83-model parser audit is [`custom-fx-particle-model-audit-2026-10-25.md`](custom-fx-particle-model-audit-2026-10-25.md) and [`fx-particle-model-audit-2026-10-25.json`](evidence/fx-particle-model-audit-2026-10-25.json). GBX.NET read 61 of 83 models; normal serialization was byte-identical for only one, so the nested fields are not promoted to a writer.

## Implementation roadmap

1. Keep the current opaque-preserving carrier route and origin editor stable.
2. Build one native wrapper fixture for a Tier 2 particle asset at a time, starting with `WaterSprayAndSteam` and `PodiumSparkler`.
3. Package every referenced prefab, FxSystem, particle model, material, shader, texture, and placement dependency; test cold preload, placement, visible local play, map save/reopen, and a second process.
4. Add a UI writer only after the exact field survives all those checks. Until then, expose discovery and dependency diagnostics as read-only.
5. Add graph composition and kinematics only after a native opaque-preserving prefab route exists.

## Evidence and language policy

All FX research, evidence, matrices, and release notes are maintained in English. Dutch explanations belong only in the chat. Older filenames remain for compatibility, but their contents are English references or point to this index.

## Native gallery map

The retained carrier and probe items are grouped in the native save/reopen fixture documented in [`fx-gallery-map-2026-10-27.md`](evidence/fx-gallery-map-2026-10-27.md). The map reopened with 12 anchored items. The one `FidStadiumBackslash` probe was excluded because Trackmania could not embed it during native save.

The one-by-one particle inventory and the first native carrier-swap attempt are documented in [`fx-particle-replacement-2026-10-28.md`](evidence/fx-particle-replacement-2026-10-28.md). The current writer stops at opaque `CPlugParticleEmitterSubModel` replacement; the Studio exposes these candidates as status-aware routes until a binary-preserving transplant proves native visibility and persistence.

The first custom-named WaterSplash emitter candidate is documented in [`fx-custom-watersplash-2026-10-28.md`](evidence/fx-custom-watersplash-2026-10-28.md). It passes fresh native item preload and the byte-identical route wrapper passes placement plus map save/reopen, but no static Stadium WaterSplash stream was visible. It remains a candidate until a vehicle/water carrier context is proven.

The follow-up native name-route probe is documented in [`fx-particle-name-route-2026-10-28.md`](evidence/fx-particle-name-route-2026-10-28.md). Eight generated wrappers passed native preload and map placement, but none showed a visible particle stream in the editor/playground captures, so they remain research fixtures.
