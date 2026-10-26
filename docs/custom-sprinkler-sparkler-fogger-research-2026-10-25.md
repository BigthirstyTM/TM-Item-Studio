# Custom sprinklers, sparklers, and foggers ? research reference

## Executive result

Custom Fogger and Sparkler items are feasible when an existing native FX carrier is preserved. Trackmania treats them as ordinary `CGameItemModel` ornaments; the visible effect is supplied by a prefab and `CPlugFxSystem` graph. The current Item Studio safely edits the validated emitter origin and preserves the opaque graph. It does not yet write arbitrary particle fields or compose new graphs.

There is no official `Sprinkler*.Item.Gbx` in the Stadium extraction. A sprinkler therefore has two possible meanings:

1. a decorative water-particle item using `WaterSpray`, `WaterSprayThin`, or `WaterSprayAndSteam`; or
2. a waterfall-like object using `CPlugDynaWaterModel`.

The water particle models preload natively, but they are vehicle-archive assets with `idName=Unassigned` and no proven item wrapper. `CPlugDynaWaterModel` is a waterfall/force model, not a block water volume.

## Carrier structure

```text
CGameItemModel (Item.Gbx)
?? CPlugPrefab / dyna object
   ?? CPlugFxSystem (0x0915C000)
      ?? CPlugFxSystemNode_ParticleEmitter
         ?? CPlugParticleEmitterModel
            ?? CPlugParticleEmitterSubModel
               ?? GPU spawn/model
               ?? spawn/life/render/physics records
               ?? optional smoke/splash/impact records
```

The public parser does not expose a safe complete writer for this graph. Some standalone `.ParticleModel.Gbx` files have typed definitions, but that does not make the full item graph writable.

## Candidate settings

The engine metadata exposes node composition and expressions, emitter and spawn controls, lifetime and size curves, color/alpha, weight/friction/collision, render modes and atlases, mesh/material/shader dependencies, fog GPU fields, simulated smoke, and water render modes. The complete machine-readable list is [`fx-settings-matrix.tsv`](evidence/fx-settings-matrix.tsv).

The practical settings groups are:

- **Emitter origin:** local XYZ; currently editable and natively validated.
- **Spawn/life:** count, period, conditions, lifetime and variation; preserve-only.
- **Size/render:** birth size, curves, beams, trails, mesh, atlas and render mode; preserve-only.
- **Color/alpha:** gradients, hue, transparency and fade curves; preserve-only.
- **Physics:** weight, gravity, friction and particle collision; preserve-only.
- **Fog/smoke:** texture slots, fog flag, emission rate and simulated smoke curves; preserve-only except for preload evidence of rate fixtures.
- **Water:** splash velocity/render modes and DynaWater fields; candidate-only until an item wrapper and native playback exist.

## Native evidence

- Custom Sparkler and Fogger carriers load, package, and survive native map save/reopen. Sparkler has two existing emitters; Fogger has one.
- `FogEmissionRate` fixtures 0, 100, and 1000 preload. Rate 0 has placement/save/reopen evidence, but visual plume proof is not unambiguous.
- Stock Torch carriers preload and render; identity-only user Torch clones fail because bare prefab references do not resolve as portable user dependencies.
- Podium Sparkler and water particle models preload, but no item carrier or placement proof exists.
- Opaque FX cannot currently be converted to a moving kinematic body.

See [`fx-native-test-status-2026-10-25.md`](evidence/fx-native-test-status-2026-10-25.md) and the consolidated [`fx-research-index-2026-10-25.md`](fx-research-index-2026-10-25.md).

## Engineering roadmap

1. Preserve and package the full native carrier dependency closure.
2. Build one wrapper fixture for `WaterSprayAndSteam` and one for `PodiumSparkler`.
3. Test preload, placement, visible local play, save/reopen, and a cold process for each wrapper.
4. Add a writer only for fields that survive that ladder.
5. Add graph composition and kinematics only after an opaque-preserving prefab route exists.
