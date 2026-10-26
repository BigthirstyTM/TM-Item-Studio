# Skyward promo plane: original banner converted to animated cloth

Date: 23 October 2026. Report language: English. Investigation ongoing.

## Current result

The complete original SKYWARD banner can now deform visibly in Trackmania, including its lettering and emblem. A genuine native `WarpFlag` material gives a bright blue background and white lettering. A matched zero-amplitude control remains still, so the observed movement is actual geometry deformation rather than changing illumination.

**An attached waving banner on the flying aircraft is not yet working.** The aircraft's native kinematic motion and the separately baked banner orbit both run, but the banner separates from the tow cable. Three inherited-timing controls also fail. Entering the editor's playable test mode does not remove the separation.

These are embedded research maps with a native `Flag16m` initializer and three custom dependency files. They do not establish a standalone, single `.Item.Gbx`, a cold no-Openplanet result for the new aircraft assets, or recipient/multiplayer portability.

## Evidence

- [Bright blue/white banner recording, stationary aircraft](../artifacts/skyward-banner-20261023/static-warpflag-center-motion-01/raw.mp4): real 1920 by 1080 central crop of the 3840 by 2160 game window; 16 seconds, 661 captured frames, average **41.313 fps**. Capture requested 60 fps; sustained 60 fps was not achieved. No interpolated frames or simulated animation.
- [Matched zero-amplitude recording](../artifacts/skyward-banner-20261023/static-warpflag-zero-motion-01/raw.mp4): 9 seconds, 431 frames, average 47.889 fps.
- [Native screenshot](../artifacts/skyward-banner-20261023/material-nativewarpflag-oneuv-01/overview.jpg) shows the whole aircraft and banner.
- [Initial flight diagnostic](../artifacts/skyward-banner-20261023/flight-circle-oneuv-motion-01/raw.mp4) and [independent video audit](../artifacts/skyward-banner-20261023/flight-circle-oneuv-motion-01/video-audit.md) demonstrate the attachment failure.
- [Three timing-control video audits](../artifacts/skyward-banner-20261023/sentinel-tests-video-audit.md).

The Openplanet/MCP stack was present for automation, camera control and read-only diagnostics. No dedicated flag companion, process-memory write, GPU injection, plugin reload or game restart was used in this experiment. This distinction is not a no-Openplanet proof.

## Original source and separation

Source: `Documents/Trackmania/Items/BF2_ASSETS/Skyward_DoubleDecker_Plane/PromoPlane_Skyward.Item.Gbx`.

SHA-256: `7EEDD88D3417A59F08724F6755EE665ADC35CAF7CE2E46AEFC554C5443EE2FB6`. The source remains unchanged. A separate original-byte copy and generated research Items were used.

The source has a direct `CPlugPrefab`, one kinematic `CPlugDynaObjectModel`, and a world-relative Y constraint. Its rotation takes 150 seconds; its separate vertical travel repeats over 160 seconds. The combined repeat is 2,400 seconds. The original geometry lies approximately 135 metres from its rotation origin.

The Solid2 mesh contains 13 visuals with matching material slots. **Visual 9 is the complete banner**: 2,576 vertices, 2,447 triangles, 7,341 indices, and two UV channels. Both overlapping front/back components are retained. The SKYWARD letters and emblem are triangulated geometry using palette UVs, rather than an external SKYWARD image. Visual 8 is the rigid tow structure. The banner's leading attachment edge is its maximum-Z edge, approximately Z = -9.020492 metres.

For stationary controls the other 12 visuals and their material mappings are preserved in a static aircraft. Only the banner is moved into the ordinary non-kinematic cloth host from the previously accepted v6 flag-remap seed. This avoids conflating cloth rendering with native flight timing.

## Animated visual construction

The file-only builder is [Tests/SkywardBannerResearch/Program.cs](../Tests/SkywardBannerResearch/Program.cs). It emits all five cloth LOD visuals, each containing the full original banner topology and 86 animation frames. Every frame preserves the original indices and both sides. Frame 85 copies frame 0 exactly. Bounds include every stored pose. Generated files are reparsed and checked before native loading.

Let `d = max(0, maxZ - Z - 0.5)` and let `u` be `d` divided by the remaining banner length. The stationary wave displaces X by:

```text
0.65 * u^2 * sin(2*pi*d/7 - 2*pi*frame/85) metres
```

The leading half-metre remains fixed. The loop lasts eight seconds. Normals receive the inverse-transpose of the deformation Jacobian. A zero-amplitude control bypasses the displacement exactly.

### UV layout was decisive

Keeping both source UV channels produced absent or severely exploded banner geometry, even though the effective cloth already had all five structured GPU visuals initialized. Removing only UV1 made the complete banner visible and animated.

The [isolated pair verification](../artifacts/skyward-banner-20261023/oneuv-pair-verification-contrast.json) proves the complete canonical Items become byte-identical after only removing UV1 and normalizing the display name. This establishes a working one-UV layout and a failing two-UV layout for these exact files. An incompatible packed vertex stride is the leading explanation; this test does not independently prove the shader's precise stride calculation.

### Native material route

The accepted placeholder material, GameSkin and TerrainModifier route remaps the cloth to a genuine game material before the structured-visual initialization stage. The source placeholder has an inline zero-pass ShaderApply. The successful bright control changes the replacement folder to `Stadium256/Media/Material/` and the replacement short name to `WarpFlag`.

The effective material is the game's `WarpFlag.Material.Gbx`, using the native VertexTween shader. Its native country texture array supplies the blue/white palette. Letter vertices sample UV0 `(0.25, 0.5)` and background vertices `(0.5, 0.1)`. The source's letter/background classification is exact for this mesh and has no mixed-class triangles. This restores clear lettering, **not the original black/red/white palette**.

The earlier `ItemFlag` material forcibly colorizes its diffuse through a fully opaque green hue mask, which explains why white diffuse samples still appeared dark green. Moving to a stable white texture patch alone did not fix that. Embedded cloned native materials were also tested; their effective cloth remained 0/5 structured and invisible. Their cross-context lookup/early shader gate remains unresolved. See the [material findings and native shader evidence](../artifacts/skyward-banner-20261023/material-candidates-findings-20261023.md).

## Flight experiment and failed controls

The flight controls retain the source native rigid body, its 12 non-banner visuals, materials and collision. Geometry and collision are translated together by X +115 metres to give an approximately 20-metre aircraft orbit. Both entity rest transforms and the source constraint pivots remain identity/zero.

To isolate timing, the original 150/160-second movement is replaced with one linear 20-second Y rotation and no vertical travel. The ordinary banner's frames bake the corresponding rigid rotation together with five local wave cycles. This is a hybrid: **native kinematics move the aircraft; vertex animation moves the banner**. The banner is not physically constrained to the aircraft.

| Control | Native rigid timing | Ordinary cloth timing | Observed result |
|---|---|---|---|
| CircleWave baseline | Period scale 1, max 1, phases 0/0 | Period 20 seconds, max 20, phases 0/0 | Both move; attachment fails |
| RigidSentinel | Original sentinel values 1/-1/-1/-1 | Baseline cloth | Attachment fails |
| ClothSentinel | Baseline rigid | Period 20/20, phases -1/-1 | Attachment fails |
| BothSentinel | Rigid sentinels | Cloth phases -1/-1 | Attachment fails |

The baseline recording shows the same orbit direction and recurrence near twenty seconds. It suggests a stable phase mismatch over that recording, rather than opposite rotation or obvious progressive drift. Approximate image-centroid fitting is not an exact phase measurement. A large attachment gap cannot be explained by the small angular sampling error alone.

An editor-playground test (`MapEditor_Test`) was also entered with the native blue/white CircleWave material variant. The free-camera screenshot and recording still show separation. This is a playground observation, not race validation or a finished playable map.

## Clock investigation

Read-only inspection of the pinned executable identifies distinct explicit time inputs. The ordinary auto FuncTree path evaluates approximately:

```text
phase = positiveFraction(clockMilliseconds * 0.001 / effectivePeriod + effectivePhase)
```

Its owner clock is reset-relative. The native kinematic evaluator receives a separately selected time record and subtracts a manager offset, with a special -1 sentinel. Matching stored period and phase numbers therefore does not establish matching effective clocks. See [the bounded native clock trace](../artifacts/skyward-banner-20261023/clock-producer-trace-v9/findings.md) and [clock-origin addendum](../artifacts/skyward-banner-20261023/clock-producer-trace-v9/clock-origin-addendum.md).

Live discovery and paired observations in `MapEditor_Test` have been recorded under `clock-live-discovery-playground-02/03` and `clock-live-samples-playground-01/02`. The physical-manager getter was resolved with pinned native code/factory/owner guards. Its measured kinematic offset is 297,280 milliseconds; the shared animated matrix follows the expected twenty-second rotation. The visual scene clock minus that offset predicts the measured yaw within sequential-read uncertainty.

Crucially, the inspected CPU ordinary FuncTree list is **empty** in all samples. Its reset-origin clock cannot therefore be attributed to the active banner. The retained native vertex shader also has an instanced path using `DrawV.TimeSc` and a packed, quantized instance period/phase, plus a non-instanced path. Which inputs the current banner actually uses remains to be measured. The [exact shader timing audit](../artifacts/skyward-banner-20261023/material-candidates-vertex-timing-v1/README.md) documents this distinction. A CPU clock comparison alone does not explain the banner's offset.

The observations are sequential bounded reads, not an atomic GPU capture. No clock or render state is modified by the sampler.

### Packed instance timing and native constant upload

The guarded [CPU instance snapshot](../artifacts/skyward-banner-20261023/hms-packed-live-03/snapshot.json) succeeds in the manager's alternate storage mode. It retains all 91 active records and their stable-ID mappings, with the map and descriptors checked again after the reads. The record at the custom aircraft anchor `(788, 128, 450)` has packed timing word `0x80000665`: period code 1637, phase code zero, palette zero. The shader decodes that period as approximately **20.017568786 seconds**, rather than the authored 20 seconds. This is a CPU record observation; exact file identity and the active GPU draw must be linked separately.

Against a perfectly uniform native twenty-second rotation, this rate difference alone would accumulate approximately 0.316 degrees per native revolution, or 0.95 degrees per minute. It cannot explain the large initial attachment gap seen in the short flight recordings.

Pinned executable inspection also identifies the native VertexTween constant uploader at preferred address `0x140A9C080`, selected by the shader filename dispatch at `0x140A9C580`. Its instanced branch copies `cFrame` directly from visual offset `0x118`, without subtracting one. It obtains `TimeSc` from the unsigned millisecond field at `*(renderContext + 0x838) + 0x1AC`, converted to floating-point seconds. The non-instanced branch instead supplies explicit frame offsets and blend weight. See [the exact uploader disassembly](../artifacts/skyward-banner-20261023/shader-upload-static-v2/0x140a9c080.asm.txt).

This exposes two concrete checks before another synchronization fixture: establish the renderer clock's semantic owner, and verify the current draw's branch and actual frame count. The current authored table duplicates its endpoint at frame 85; an instanced draw using all 86 frames would include a short stationary interval while blending that duplicate back to frame zero. That inference must not be confused with a direct capture of the banner's current uploaded constants.

## Reproduction and remaining gates

The successful bright stationary map is:

`artifacts/skyward-banner-20261023/material-candidates-native-warpflag-oneuv-v1/TMIS_Skyward_NativeWarpFlag.Map.Gbx`

Map SHA-256: `02703C4CE9A8A691D9F321D846A4A9F360C50CDBED4402E268FB2D2CBD174BB3`.

It embeds the generated Item and all three custom dependencies, and contains native Flag16m first. The map is 14,331,277 bytes; the Item is 21,893,651 bytes. These deliberately duplicate full geometry across all five LODs and 86 frames. Size, LOD simplification and platform upload limits must be addressed before treating this as a production asset.

The following remain required: synchronize native flight and local cloth deformation; verify the exact loop seam and attachment through multiple cycles; cold-load the exact new files without Openplanet; check native save dependency retention; test a separate recipient; restore or deliberately author the intended palette; reduce asset size; and test collision/placement/online constraints. A successful stationary banner does not satisfy those remaining gates.

The next useful flight work is a paired measurement of actual rigid matrices, active cloth frame/blend, and both clock origins, followed by a fixture that uses one shared time source. Repeated arbitrary phase adjustments would not establish a portable solution.

## 24 October follow-up: textured aircraft and anchor-rebase controls

The latest native evidence is recorded in the [textured flight and banner-anchor follow-up](../artifacts/skyward-banner-20261023/flight-anchor-control-report.md). The split-entity flight fixture now preserves the aircraft's source textures while the banner entity is rebased at the measured rack contact. It loads and renders, but the banner remains detached because the rigid aircraft and the ordinary VertexTween cloth consume independent transform paths. A second native constraint did not move a non-kinematic cloth; the same graph with a kinematic cloth was rejected during native map loading; and a shared `CPlugAnimLocSimple` field left the cloth fixed in the stadium frame. These controls narrow the remaining problem to the transform owner/clock relationship rather than the contact-point arithmetic.

## 24 October follow-up: inline collision and parent-child constraints

The earlier kinematic-cloth load failure was isolated to the collisionless body. A new v4 fixture adds an inline triangular `CPlugSurface` to both `StaticShape` and `DynaShape`. It loads after a fresh restart, retains the aircraft's source textures, and renders the waving banner. Four one-second samples prove that both bodies animate, but they also show the banner separating from the aircraft as their effective constraint phases diverge. The fixture is recorded under [`shared-flight-anchor-kinematic-v4-runtime`](../artifacts/skyward-banner-20261023/shared-flight-anchor-kinematic-v4-runtime/).

The next test changed the topology rather than the clock: the cloth became a native parent-child (`Ent1=0`, `Ent2=1`) with zero local native motion. Compact and interleaved entity layouts both loaded. A third A/B moved the rack vector into `Pos1` and left the child entity at the parent origin. All three controls still rendered a detached banner. This demonstrates that the typed parent-child graph and constraint anchor fields survive native loading, but this `VertexTween` visual path does not inherit the aircraft's live matrix.

Finally, clearing `IsKinematic` on the child was tested once. Trackmania exited during map loading, the bridge reset, and the game had to be restarted. The exact file and event are preserved in [`parent-child-dynamic-v1-runtime/native-load-boundary.json`](../artifacts/skyward-banner-20261023/parent-child-dynamic-v1-runtime/native-load-boundary.json); the file was not retried. Enabling `DynamizeOnSpawn` while keeping the child kinematic is loadable but leaves the same detached result.

The practical boundary is now clear: two independent native constraints are insufficient; a loadable parent-child graph is insufficient for this animated visual class; and a dynamic child is unsafe. The remaining viable paths are a single transform owner whose complete mesh uses a material-compatible animated layout, or a native render/physics composition mechanism that explicitly applies the parent's live matrix to a child `VertexTween` visual.

The nested-prefab control tested the same question with only one outer aircraft constraint. The animated cloth was placed inside a `CPlugPrefab` owned by that aircraft entity, so it had no second native constraint of its own. The item and map loaded, and the cloth waved, but it remained in the stadium frame while the aircraft orbited. The fixture is recorded under [`nested-prefab-flight-v1`](../artifacts/skyward-banner-20261023/nested-prefab-flight-v1/); its item hash is `B6B6C8B1F90E5C8796E9BF8C9CF621382092FD2F247B2D15E8063080005E2733` and its map hash is `98E0FC5110938E6838261B77A900F2D3EDE2AAA218F7B5E4414CA874B7C28A20`.

This removes the extra-constraint topology as the only explanation. The native reader accepts the prefab graph, but this animated `VertexTween` visual still does not compose the outer entity's live matrix. Further phase or anchor changes are therefore not a productive route; the remaining safe target is one transform owner with a material-compatible combined visual, or a child visual type with explicit parent-matrix composition in the engine.

## 24 October 2026: single-host LOD A/B and cold restart

The next fixture combines the twelve textured rigid aircraft visuals and the five animated cloth LOD visuals inside one `CPlugDynaObjectModel`. It uses one native transform owner, the proven one-UV `ItemFlag` cloth material, and the native flag LOD distance table `[16, 64, 128, 512]`. Rigid visuals use `LodMask=31`; the five cloth visuals retain masks `1, 2, 4, 8, 16`. This is a timing/layout control only; it does not alter the original Skyward source Item.

- Item: [`TMIS_Skyward_OneUVSingleHostFlight.Item.Gbx`](../artifacts/skyward-banner-20261023/single-host-flight-itemflag-lod-v2/files/TMIS_Skyward_OneUVSingleHostFlight.Item.Gbx)
- Item SHA-256: `C1BEAF48B9876AF4DA3C0668AD0D996129F9D62D31BA3ABE20D96B523CD7B51A`
- Map: [`TMIS_Skyward_OneUVSingleHostFlightLod_v2.Map.Gbx`](../artifacts/skyward-banner-20261023/single-host-flight-itemflag-lod-v2/TMIS_Skyward_OneUVSingleHostFlight.Map.Gbx)
- Map SHA-256: `4DAA84493818F513E120BFBD36EA9166C429BB60D24041C4A3A0A67F0937ADDB`

The map loaded in `MapEditor` after a cold Trackmania restart. `GetMapInfo` reported two anchored items: the native `Flag16m` initializer and the embedded Skyward item. The aircraft retained its source textures and changed world pose between consecutive viewport captures. The close captures also show a separate dark banner-like geometry that changes with the flight phase. This is evidence of native load, visibility and motion after restart; it is not yet proof that the dark geometry is the complete intended cloth or that its rack attachment is correct.

The runtime captures are preserved in [`runtime-evidence-20261024`](../artifacts/skyward-banner-20261023/single-host-flight-itemflag-lod-v2/runtime-evidence-20261024/). `cold-restart-overview-a.jpg` and `cold-restart-overview-b.jpg` were taken after the same restart with a fixed camera and show the aircraft at different phases. `lod-distance-12.jpg`, `lod-distance-32.jpg`, `lod-distance-64.jpg`, `lod-distance-128.jpg`, and `lod-distance-512.jpg` are a visual distance sweep. The aircraft remains visible at all five sampled distances without an obvious duplicate rigid aircraft or an abrupt disappearance. Because the camera focus distance is not an engine-side LOD readback, this sweep is an observation rather than a definitive threshold proof.

The cold restart closes the earlier process-cache gap for this exact v2 map. It does not establish standalone operation without Openplanet, recipient portability, a playable race, or continuous cloth attachment. The next decisive native probe is an engine-side visual/LOD count at controlled camera distances, paired with a close multi-frame capture of the cloth leading edge. If that probe confirms one rigid owner and five selected cloth LODs, the single-host route becomes the strongest remaining implementation path.
