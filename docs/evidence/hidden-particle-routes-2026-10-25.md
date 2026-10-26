# Hidden particle routes and native preload results

This document records the expanded hidden-effect pass. The probe used
Trackmania's `ControlFids` preload operation against read-only game FIDs. It
did not modify a map, item, or game asset. A successful preload proves that the
engine owns and can instantiate the particle-model class; it does not prove
that the model can be attached to a custom `.Item.Gbx` or rendered outside its
original context.

The complete machine-readable output is
[`hidden-particle-native-preload-expanded-2026-10-25.json`](hidden-particle-native-preload-expanded-2026-10-25.json).

## Native preload results

All 31 selected candidates passed native preload as
`CPlugParticleEmitterModel`:

- Stadium: `PodiumSparkler`, `PodiumSparklerEnd` and `TorchSmoke`.
- Effects: `FlyingLeaves_BaseModel` and `InFogEmitter`.
- Vehicle smoke/fire: `AsphaltSmoke_Stronger`, `DirtSmoke`, `DirtSmokeHigh`,
  `IceSmoke`, `CarReactorDirtSmoke`, `CarReactorSandSmoke`,
  `CarReactorSnowSmoke`, `VehicleOnFire` and `VehicleSmokeTrail`.
- Vehicle boost/energy: `Boost_Basic`, `Boost_Turbo`, `Boost_Turbo2`,
  `Boost_TurboRoulette2`, `Boost_TurboRoulette3`, `Sparkles` and `LightTrail`.
- Water: `WaterSpray`, `WaterSprayThin`, `WaterSprayAndSteam`,
  `WaterSplash`, `WaterSplashBig`, `CameraWaterDroplets`,
  `UnderWaterBubbles_Medium`, `UnderWaterBubbles_Small`,
  `UnderWaterBubbles_Spray` and `WaterWakeWNormals`.

The Stadium models resolve named IDs and live in `Stadium.pak`. Most vehicle
and Effects models resolve as `idName=Unassigned` and live in
`Maniaplanet_ModelsSport.pak` or `Maniaplanet.pak`. Those archive/context
boundaries are a strong warning against treating them as item-ready assets.

## What can be promoted to custom items now

The safe implementation pattern is to start from an existing item carrier
whose complete prefab and FX graph already loads natively, then preserve that
graph while changing the mesh and the validated emitter offset. This is the
route already proven for the custom Sparkler and Fogger. `Torch` is a valid
stock carrier, but identity-only user clones failed because bare
`Media\\Prefab` references were not resolved; a portable Torch requires a
complete dependency package and a new native save/reopen proof.

`PodiumSparkler` is a promising second Stadium graph, but no direct item root
was found. It is therefore a particle-model candidate, not an item feature.

The water models are the best sprinkler candidates by visual intent, but they
are vehicle-context assets with `idName=Unassigned`. A future sprinkler wizard
should create a dedicated wrapper fixture, package all referenced textures and
materials, and then run native preload, placement, visible gameplay, map
save/reopen and cold restart before exposing the route.

`FlyingLeaves_BaseModel` and `InFogEmitter` also preload, but they require a
VFX/material wrapper that is not currently represented by a proven item root.
Vehicle smoke, boost, sparks, wakes, bubbles and fire should remain catalogued
as context-specific candidates until an item carrier exists.

## Product boundary

The current Studio can preserve an opaque existing `CPlugFxSystem` and edit a
validated emitter origin. It does not yet serialize arbitrary particle-model
fields, create a new FxSystem graph, or convert a vehicle-context particle
model into an item. The expanded preload pass increases the candidate catalog;
it does not change those authoring limits.
