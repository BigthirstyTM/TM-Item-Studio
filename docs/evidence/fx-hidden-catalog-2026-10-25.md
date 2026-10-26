# FX catalog and hidden carriers ? 25 October 2026

This catalog is a file inventory of the local `OpenplanetNext/Extract/GameData`. An asset name or particle model proves that the game contains the asset; it does not prove that the asset is a standalone `.Item.Gbx`, can render in an item, or survives map save/reopen.

## Direct Stadium item carriers

| Effect | Carrier | References | Status |
|---|---|---|---|
| Fogger 8/16 m | `ShowFogger8M/16M.Item.Gbx` plus prefab | Fogger FxSys/particle model and `FoggerSmoke.Texture.gbx` | Proven; custom clones delivered |
| Sparkler 8/16 m | `Sparkler8m/16m.Item.Gbx` plus prefab | Two particle emitters and Sparkler textures | Proven; custom clones delivered |
| Podium Sparkler | `PodiumSparkler.FxSys.Gbx` | PodiumSparkler and PodiumSparklerEnd models | Particle-model preload proven; no item carrier |
| Torch flame/smoke | `ShowTorch.Item.Gbx`, `ShowTorchSmall.Item.Gbx` | Torch prefab, smoke FxSys/model, light, flame material | Stock preload/placement/save/reopen/render proven; identity-only user clone fails native preload |
| Fogger with light | `ShowFoggerWithLight8m/16m.Item.Gbx` | Fogger carrier plus `FoggerSpot.Light.gbx` | Stock 8 m preload, visible fog/light and save/reopen pass; custom clone proof pending |

## Strong particle candidates

| Candidate | Likely route | Status |
|---|---|---|
| `FlyingLeaves_BaseModel`, `InFogEmitter` | VFX or particle wrapper | Found; no item route |
| `WaterSpray`, `WaterSprayThin` | Decorative sprinkler | Native particle preload; vehicle archive and no item wrapper |
| `WaterSprayAndSteam` | Water plus steam sprinkler | Native particle preload; no item wrapper |
| `WaterSplash`, `CameraWaterDroplets`, `UnderWaterBubbles_*` | Splash/bubbles item | Native or corpus discovery; vehicle/water context unresolved |
| `WaterWake*`, `LightTrail` | Moving/dynamic carrier | Context-bound |
| Vehicle exhaust/fire/smoke/boost assets | Vehicle carrier | Context-bound |
| ShootMania explosions, impacts, lasers, debris | Mode-specific graph | Game assets; no Trackmania item proof |

## Textures and scene resources

Candidate resources include `ProceduralFire_Color`, `FireColorOverLife`, `WhiteSmoke`, `WHITE_SMOKE_2`, `Confettis`, `Sparkle`, `AnimFire_D`, `WaterSplash`, `AirBubble_*`, `LensFlares`, LightTrail materials, and `TorchFlame.FuncShader`. A texture or shader is not an FX by itself. It must be referenced by a valid FX graph and pass native tests.

## Probe priority

1. Preserve and package a complete native carrier.
2. Build a `WaterSprayAndSteam` wrapper and test it as a decorative sprinkler.
3. Investigate the Podium Sparkler graph.
4. Test one particle setting per fixture, including cold preload, placement, visible play, save/reopen, and a second process.
5. Expose Add FX or a general particle library only after graph composition has native save proof.

The current Item Studio preserves unknown FX payloads and writes only the validated emitter origin. This catalog records real sources and investigation paths; it does not claim direct editor or map compatibility for unproven candidates.

See the consolidated English reference: [`fx-research-index-2026-10-25.md`](../fx-research-index-2026-10-25.md).
