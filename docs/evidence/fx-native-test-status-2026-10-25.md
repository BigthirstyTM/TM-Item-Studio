# Native FX test status — 25 October 2026

This report separates what the game actually accepted from what is only a
parser or corpus observation. The native probes used Trackmania 2026.2.2.1751
with the local Openplanet bridge. A socket response alone is not counted as a
pass.

## Passed

- The standalone custom Sparkler and Fogger load, appear in a cold packaged map,
  and preserve their mesh and embedded FX payload. The packaged map contains
  the exact item bytes and no external user-item references.
- The existing Sparkler has two particle emitters. Moving the emission point
  writes `CPlugFxSystemNode_ParticleEmitter.LocalOffsetExpr` and retains the
  ten-metre relative spacing. The Fogger has one emitter. The raw splice has
  exact restore and reparse proofs; Studio exports the item without a required
  sidecar.
- `FogEmissionRate` fixtures 0, 100 and 1000 all pass native `Fids::Preload`.
  The rate-0 fixture was placed, saved and reopened in a map. Visual plume
  output and a successful native item Save As for every rate remain open.
- The stock hidden carriers `ShowTorch` and `ShowTorchSmall` load as native
  `CGameItemModel` objects. Runtime graph inspection shows one particle emitter
  using `FoggerSmoke.Texture.gbx`. The stock `ShowTorch` also placed in a
  disposable map, survived `SaveMapAs`, and rendered its flame in the captured
  viewport screenshot. This is evidence for the game carrier, not for a custom
  user clone.
- `PodiumSparkler.ParticleModel.Gbx`, `PodiumSparklerEnd.ParticleModel.Gbx`,
  `WaterSpray.ParticleModel.Gbx`, `WaterSprayThin.ParticleModel.Gbx`,
  `WaterSprayAndSteam.ParticleModel.Gbx` and `WaterSplash.ParticleModel.Gbx`
  all pass native particle-model preload. Podium has no item carrier in the
  extracted catalog; the water models are in the vehicle archive and report
  `idName=Unassigned`, so none is promoted to an item or sprinkler.

## Negative or limited native results

- Identity-only user clones of `ShowTorch` and `ShowTorchSmall` fail native
  preload when their bare `Media\\Prefab` references are left unresolved. They
  are not portable custom carriers and do not appear in the item inventory
  search. A future Torch clone must package or resolve the complete dependency
  graph and then repeat placement and save/reopen tests.
- The current kinematic converter rejects the opaque FX item with
  `The item does not have a static object model to convert.` No moving FX item
  is supported yet.
- Existing multiple emitters are preserved, but no native graph merge or
  `Add FX` writer exists. Combining a Fogger and Sparkler, adding a new graph
  node, or assigning different carriers has not passed native save/reopen.
- Native bitmap/texture experiments can preload, and some can be placed and
  saved, but the custom particle output was not visibly rendered in the tested
  gameplay controls. They remain read-only/preserve-only.

## Per-setting ledger

| Setting group | What was tested | Result for Studio |
|---|---|---|
| Item identity, name, author, collection and placement metadata | GBX.NET round-trip, native custom Sparkler/Fogger load and map packaging | Editable/preserved |
| Emission point XYZ | One-variable raw `LocalOffsetExpr` splice, reparse, two-emitter spacing, native Fogger map save/reopen | Editable |
| Emitter count and relative offsets | Sparkler two-emitter inspection and translated export | Existing graph preserved; no add/remove writer |
| Spawn period, max count and spawn conditions | Corpus/type audit only; no safe full-graph serializer | Read-only |
| Lifetime, variation, birth/over-life size and spread | Corpus/type audit only; no native one-variable carrier mutation | Read-only |
| Color, hue, alpha and fade curves | Corpus/type audit plus bitmap controls; no persistent particle-color carrier proof | Read-only |
| Weight, gravity, friction and particle collision | Candidate fields identified; no native carrier mutation/save proof | Read-only |
| Render mode, atlas, beam/trails and mesh slots | Native stock FID references load; custom bitmap variants lacked visible FX persistence | Read-only |
| Fog/smoke GPU rate | Embedded rates 0/100/1000 preload; rate-0 place/save/reopen; visual plume not unambiguous | Preserve-only pending visual proof |
| Simulated smoke and GPU spawn fields | Names/shape discovered from definitions; no safe writer or native A/B fixture | Read-only |
| Add FX / mixed graph | No native graph merge or save/reopen route | Unavailable |
| Kinematic FX | Current converter explicitly rejects opaque FX for lack of static model | Unavailable |
| Sprinkler water models | Water particle assets preload; no item wrapper or placement | Candidate only |

## Read-only corpus candidates

The local extraction contains 83 particle-model assets. GBX.NET reads 61 and
rejects 22 because of unsupported or truncated layouts; its normal serializer
is byte-identical for only one parsed asset. This is discovery evidence, not a
particle writer. Spawn/count, period, lifetime, size, spread, color and alpha
curves, gravity/weight/friction, particle collision, render mode, atlas,
texture slots, simulated smoke, GPU spawn fields and water render modes stay
read-only until each one has a one-variable native fixture.

`WaterSpray`, `WaterSprayThin` and `WaterSprayAndSteam` are real game assets,
but no standalone item carrier or native sprinkler playback has been proven.
`CPlugDynaWaterModel` is a separate waterfall/force model and does not create a
block water volume from a custom item.

The machine-readable field matrix is
[`fx-settings-matrix.tsv`](fx-settings-matrix.tsv); the full hidden-effect
inventory is [`fx-hidden-catalog-2026-10-25.md`](fx-hidden-catalog-2026-10-25.md).
