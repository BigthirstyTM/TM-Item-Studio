# Collision on the existing 128-point coaster route

Local research fixtures, not a production exporter. Requires the measured 8 m
`BF2_ASSETS/Cube.Item.Gbx` used by the original scenery proof.

```powershell
dotnet run --project Tests/CoasterCollisionProof -- <Cube.Item.Gbx> docs/evidence/coaster-path-2026-10-31/route128.json <output-directory>
```

All variants retain the 128-point route, its elevations, linear leg timing and
fixed cube orientation. Coordinates are translated by minus the first route
point so that the cube starts at the item's origin. An 8-second start dwell is
added per cycle. At 20 m/s the cycle is 79.509 seconds; at 5 m/s, 294.005 seconds.

- `ChainCollision`: the existing scenery compiler, restoring the original
  full-size StaticShape/DynaShape on its visible child. 129 bodies.
- `Relay20`: 128 world-root bodies, one per exact route segment, at 20 m/s.
- `Relay5`: same construction at 5 m/s.
- `Relay5Bounds`: same motion/collision as Relay5, with recomputed visual bounds
  around the offset geometry. Bounds enclosure is checked after serialization.

The relay retains only one active visible cube. Each inactive cube is rotated
around an offset pivot to park it below the terrain. Its local geometry is
counter-rotated to preserve the source cube's orientation while active. The
segment frame is constructed with an upward local Y axis, so parking remains
downward on slopes. Assertions check active corner transforms, parking clearance,
body bindings, positive durations, total clocks and the four-key budget after
GBX reparse. Parking is fixture-specific, not safe in every map. Switching body
identity/rotation may launch the car; native load acceptance is not a pass.

Install generated files into `Items/TMIS_CoasterCollision`, preload and restart
the game if the native inventory does not discover them. From the game menu:

```powershell
python Tests/MotionCollisionHandoffProof/native_probe.py TMIS_CC_Relay20 CC_NewRelay --output artifacts/coaster-collision --seconds 96 --coaster-context
```

The context option places `NewCoaster.Item.Gbx` at (534.4675,10.782,400), and the
normalized moving item at (400,15,400). This preserves exactly the original
relative alignment. The car spawns at grid (12,11,12), above the cube top Y=19.
The coaster is an independent collidable surface: resting on its rails is not
proof of being carried by the cube. Compare car position with the moving cube
and inspect the recording. Use unique labels and preserve failed observations.
Use `--follow-car-camera` for a close observation camera; this changes only the
view, never item or car transforms. No plugin drives the item during playback.
