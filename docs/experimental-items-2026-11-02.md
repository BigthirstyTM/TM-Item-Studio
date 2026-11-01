# Experimental items and linked instances — 2026-11-02

This is a progress report, not a claim that all requested capabilities are implemented. Evidence: [native experiment summary](evidence/experimental-items-2026-11-02.json). Raw local captures and generated candidates are in `artifacts/experimental-items/`.

## Delivered editor changes

- **+ Instance** retains the source at placement 1 and adds placement 2. Further clicks add another linked placement.
- The Instances panel names the root file, explains shared geometry/materials versus independent file clones, and selects an individual placement.
- Each viewport instance has a unique selection path and an original source-mesh path. Selection survives the .NET scene rebuild; edits still address the source mesh.
- GPU geometry buffers are shared. Hidden originals stay hidden when material filters run. Collision geometry is placed with each copy.
- Projects save/restore instance transforms, with the same limits enforced on save and load.
- Combined-item export now remaps constraint entity indices for each copy. Previously, copies could keep referring to the first copy's bodies. World anchors follow placement; body-local anchors remain unchanged.

Limits: this panel previews static placements, not independently timed kinematic playback. Use the motion-path array for timing. A macroblock references an external root item; a combined item embeds shared model nodes. Combined prefab export still reports unsupported scale rather than encoding scale in an entry that has no scale field. Runtime physics of all combined-instance configurations is not established by serialization tests.

Validation: app build; 17 MacroblockExport cases; Playwright linked-instances (shared buffers, unique selection, selection bridge rebuild, project roundtrip); viewer lifecycle; path-authoring project (Bezier, look-at, clearance, applied-route export).

## Moving checkpoint: controlled native results

Source: `Items/NoRespawnGates/NoRespawnCP_Ring_NoRespawn.Item.Gbx`. Its prefab contains a **version-1** `NPlugTrigger_SWaypoint`. The earlier version-0 trials had not established a working native baseline. These new candidates retain the source trigger shape and use `NoRespawn=false`.

The original trigger spans X ±13.24051 m and Z 0.16021214..0.58303356 m. Tests spawn the car at approximately (335.999, 8.014, 335.998). The displaced test anchors the item at (320,8,336); the inside control anchors it at (336,8,336). Main motion is 32 m along X over 4 seconds, followed by 4 seconds back.

- Static outside: player checkpoint count **0**.
- Static inside: player checkpoint count **1**. This establishes actual trigger registration, not just native loading. Initial-overlap times are negative because this happens during the start countdown.
- Moving mesh, stationary trigger: count **0** outside. Initial B-series had a collidable carrier that pushed the car away, so they are not sufficient motion evidence.
- C-series removes that confound by shrinking and parking the carrier collision 100 m below the mesh. With an additional world-to-trigger constraint, the car stays stationary at the measured test position and count remains **0** across multiple cycles.
- The same C-series candidate still registers **1** at its original trigger position. The added constraint does not establish a moving trigger.
- Parent-body-to-trigger constraint: Trackmania exited while opening the B-series map. A fresh crash log and socket reset were observed; this does not identify the exact engine/plugin fault. The known-unsafe candidate is excluded from default generation and must not ship as a supported template.

No working moving checkpoint or success video is claimed. Next meaningful routes: inspect native trigger attachment capabilities rather than treating a trigger as a dynamic body; determine whether a native animated surface can update its trigger broadphase; separately investigate script-driven checkpoints. A script-assisted solution must be labelled as requiring a map mode/plugin, not sold as a standalone item. Static checkpoint arrays are not equivalent to a moving trigger and would need inactive-region negative controls.

## ItemTypes, pickups and audio

The extracted GameData catalog has 1,364 item files: 1,358 parsed Ornament, 4 Vehicle, 1 Character, and one parse failure (`RedIsland/Items/Vegetation/BushSmallA.Item.Gbx`, EndOfStreamException). These numbers describe this extracted set, not every file in every installed pack. The character is `ShootMania/Items/Characters/CharacterPilot.Item.gbx`. There is no original PickUp, Turret or Cannon item in this catalog to use as a positive gameplay control.

New common-item candidates retain the ring mesh and trigger shape in a version-6 `CGameCommonItemEntityModel`:

- Ornament control;
- PickUp, with WaypointType=None;
- permanent sound reference to `Stadium/Media/Audio/WhooshDecoItems.Sound.Gbx`;
- spawn sound reference to `ShootMania/Media/Audio/Sound/UI/UISound_Bonus.Sound.Gbx`.

All four passed native FID preload. PickUp also appeared in `GetItems` at its expected placement and entered an editor test run. **Collecting, disappearance, reward and respawn are not proven.** The race checkpoint counter is not a pickup detector. An attempted native screenshot timed out and is not visual evidence. Audio candidates have not been proven audible; the exact sound reference resolver and playback event still need verification. No audio/video success is claimed.

Useful legacy assets do exist (ShootMania weapon/UI sounds, ambient OGGs, character models), but presence in extracted data does not establish a usable TM2020 gameplay controller. Renaming ItemType never creates that controller.

## Particle preview and additional FX: still pending

Fogger's embedded graph parses with NICE, including lifetime 5 s, size 8 m, life variation 0.8, size variation 0.5, viscosity 0.5 and `IsFogEmitter`/`FogEmissionRate`. The zero-rate source really contains rate 0; a preview must respect it.

The extracted standalone `Sparkler8m.ParticleModel.Gbx` fails both parsers at `CPlugParticleSplashModel` 0x090B5000. GBX.NET reports LengthLimitException while reading PlugCurve. The embedded custom Sparkler contains recognizable GPU chunks and external bitmap references, but a complete validated emission/texture dependency decode is still required. Do not patch opaque source bytes based only on a coincidental chunk signature.

**No emitted-particle renderer has been added in this change.** The existing viewport still shows the emitter gizmo. A generic random sprite effect would not meet the request to preview the actual imported effect. Native volumetric fog also needs an explicitly documented rendering approximation if implemented in Three.js.

The earlier opaque PodiumSparkler-to-Fogger transplant is a known crash fixture. Name-only replacements and vehicle-water effects without their runtime context have already failed to establish visible particles; repeating them is not new evidence.

## Further features worth investigating

1. Instance hierarchy with source/placement badges, per-instance visibility and a clear Make unique command; separate shared mesh, collision and animation state.
2. Dependency inspector: show unresolved texture, sound, particle and skeleton references before exporting; package only the required dependency closure.
3. Particle graph inspector with decoded lifetime/rate/velocity/texture atlas fields, native-versus-preview compatibility labels, then a deterministic sprite/volume preview.
4. Native ambient emitters with loop/attenuation controls and automatic near/far audio comparison; event sounds only after proving the native event hooks.
5. Pickup laboratory with paired Ornament/PickUp controls, a real grab-action graph, a supported game-mode test, and collection/respawn observations.
6. Moving-trigger laboratory with simultaneous positive/negative position tests, not just a moving visible checkpoint ring.
7. Legacy asset catalog grouped by parsed, native-loaded, visible and playable status. Include title/context dependencies rather than hiding them behind an ItemType dropdown.
8. Reusable effect assemblies: animated mesh plus light plus correctly resolved particle/sound nodes, with shared dependencies across instances.
9. Native playback regression maps for trigger crossing, collision, duplicate animation phase and asset persistence after reopening a fully embedded map.
10. Surface and material event experiments (skid/impact/boost/reset) kept distinct from ordinary sound playback and decorative particles.

Outstanding requested work: actual emitted-particle rendering; demonstrably collectible items; audible native item playback; successful new particle-effect items and their videos; a standalone moving waypoint. The current experiments narrow these questions but do not resolve them.
