# Skyward banner file research

This isolated console project creates candidate Items and complete embedded editor maps. It does not stage assets, modify the original Item, start/stop a game, write process memory or control the desktop. Native loading, visibility, deformation and restart/embedding results must be recorded separately by the game test operator.

The pinned source is `Documents/Trackmania/Items/BF2_ASSETS/Skyward_DoubleDecker_Plane/PromoPlane_Skyward.Item.Gbx`, SHA-256 `7EEDD88D3417A59F08724F6755EE665ADC35CAF7CE2E46AEFC554C5443EE2FB6`. The complete source visual 9, including both apparent sides and geometric SKYWARD lettering, contains 2,576 vertices and 2,447 triangles. Every triangle is retained. The other twelve visuals retain their original material slots; all thirteen source material nodes remain byte-identical.

## Generate controls

Run from the repository root and always choose an unused evidence directory:

```powershell
dotnet run --project Tests/SkywardBannerResearch -- --generate <new-directory>
dotnet run --project Tests/SkywardBannerResearch -- --generate-contrast <new-directory>
dotnet run --project Tests/SkywardBannerResearch -- --generate-oneuv <new-directory>
dotnet run --project Tests/SkywardBannerResearch -- --generate-contrast-oneuv <new-directory>
dotnet run --project Tests/SkywardBannerResearch -- --generate-flight-contrast-oneuv <new-directory>
```

Static modes separate the original frozen plane reference, zero additional deformation and a travelling wave. Source UV0 and UV1 are preserved by default. `contrast` deliberately maps UV0 to native white/green palette samples and does not preserve the original blue/white appearance. `oneuv` deliberately removes source UV1, leaving exactly one UV channel to match the established native flag buffer layout. All other generated graph changes remain identical between corresponding two-UV and one-UV controls, apart from display names.

The flight controls contain four maps: stationary zero-wave, stationary wave, circular zero-wave and circular wave. A documented X translation of 115 metres applies to every retained rigid visual position, original collision vertex and banner rest position. This changes the approximate plane radius from 135 to 20 metres. The source's independent 150-second rotation and 160-second vertical travel are replaced by a single 20-second circular Y motion and zero vertical travel. The native rigid body uses an authored 20,000 ms linear segment with instance PeriodSc/PeriodScMax of 1; the ordinary nonkinematic cloth requests PeriodSc/PeriodScMax of 20 seconds. Both request phase zero. Native agreement of these two consumers is the purpose of the controls and cannot be assumed from their serialized values.

Banner frames bake the rigid rotation and five wave cycles into one 86-frame loop. The leading half-metre remains fixed relative to the rigid transform. Positions and normals in frame 85 are copied from frame 0. Five LODs each retain the complete source topology. Angular interpolation follows chords and introduces approximately 2.4 cm midpoint sag at the outermost approximately 34-metre banner radius; it is distinct from clock or phase drift. No kinematic constraint or LocAnim is attached to the cloth.

All maps contain a genuine native Stadium Flag16m anchor and the complete custom Item with its three pinned v6 material-remap dependencies. The maps are editor fixtures and are not validated race maps. A single independent Item, native-free initialization, original source motion parity, multiplayer and second-client portability are not established by this generator.

## Verify the isolated UV-layout difference

```powershell
dotnet run --project Tests/SkywardBannerResearch -- --verify-layout-pair <two-uv-item> <one-uv-item>
```

This reparses both complete Items, removes only UV1 from the two-UV candidate and normalizes only their display names. Their complete canonical Item serializations and reference tables must then be byte-identical. Inputs are checked again to prove they were not modified. This is stronger than comparing selected geometry fields. Native evidence is still required to attribute visible rendering changes to the layout difference.

The generator verifies every frame position/normal/UV, all indices/frame tables, conservative bounds, exact loop endpoints, complete visual serialization, rigid material serializations, normalized rigid positions/collision, embedded payload bytes, native anchor and original source hashes. A successful console run is a file/serialization verdict only.

## Pinned executable clock inspection

`audit_clocks_pe.py <unused-directory>` preserves the native constraint evaluator, subvisual sequencer and quaternion excerpts. `trace_tween_clock_pe.py <unused-directory>` preserves the ordinary auto FuncTree registration/tick, its exact millisecond multiplier, native kinematic clock accessors/consumers and bounded reference candidates. Both inspect only the pinned executable disk file; they do not open a process or operate the game. The ordinary FuncTree clock formula and the native kinematic offset are separate inputs whose effective runtime agreement remains a measurement obligation. See `artifacts/skyward-banner-20261023/clock-producer-trace-v9/findings.md` for the measured static boundary and focused read-only next test.

## Guarded native clock observations

`sample_paired_clocks.py` is a separate read-only operator tool. It does not open or reload a map, stage a plugin, move the camera, write process memory, inject code, or open a process handle. The game operator must first load an owned `TMIS ` editor fixture in exact `MapEditor` or `MapEditor_Test` mode. The initial mode is saved and must remain unchanged throughout discovery and every observation; general race/playground contexts are not accepted. It uses only the existing localhost bridge's `GetMode`, `GetMapInfo`, `DevGetPointers`, `DevSceneComponents`, `GetTypeInfo` and `DevSafeRead` tools. It discovers fresh semantic roots each run; it never accepts a persisted raw pointer as a starting root.

```powershell
python Tests/SkywardBannerResearch/sample_paired_clocks.py <unused-directory> --discover --expect-map "<exact fixture display name>"
python Tests/SkywardBannerResearch/sample_paired_clocks.py <another-unused-directory> --expect-map "<exact fixture display name>" --samples 3 --interval 2
```

Run discovery first. It checks the pinned disk executable SHA-256, selected matching live instruction bytes, native vtable slots, exact `CHmsZone` component name/class ID/vtable, and the reflected app/scene/physics graph. `CHmsZone+0x18` supplies the native ordinary clock manager. The latter's `+8` pointer supplies the object whose `+0x1AC` millisecond field is actually consumed by the pinned native ordinary FuncTree tick. Its semantic type is not assumed: equalities against the discovered app, visual scene, zone and physical scene are recorded. A separate verified path, `CHmsZone+0xD8`, supplies the state whose `+0x1A4/+0x1A8/+0x1AC` fields hold reset origin, incoming tick milliseconds and elapsed milliseconds. The native zone tick stores `incoming-origin` as elapsed; it resets origin if incoming decreases or the difference exceeds `0x1000000`. The sampler records the contiguous origin/incoming/elapsed fields and checks exact pointer equality with the ordinary consumer's owner before associating the origin with that consumer. `IScenePhys.Dyna` (or another successfully reflected exact field) supplies the raw physical manager; its first word is an owner pointer, not a `CMwNod` vtable. No guessed physics offset is followed.

Normal mode observes the bounded active ordinary registration list, exact native subvisual sequencer settings and packed tree frame/blend, native kinematic visual matrices/shared phases, the physical manager's `+0x7A38` millisecond offset, and the visual scene time field used by E++'s Kinematics tab. This last field is **not yet asserted to equal the argument passed to the physical updater**. Sequential RPC reads are not atomic: each request has wall-clock/monotonic timestamps, each observation brackets the clock reads, and the total spans are retained for analysis. Map and key root identities are checked before and after each observation. Count, pointer, byte-length, read fault, build, identity and deadline failures stop the run without a mutation fallback.

Live reflection can expose `IScenePhy.Dyna` as a method (`offset=0xFFFF`), rather than a direct field. The sampler never dereferences `0xFFFF`. Its alternate method route matches exact pinned getter/accessor/factory instruction bytes, reads the registered Dyna slot index from module RVA `0x2053B84`, and applies the native common accessor's `scene+0x10+slot*8` formula. Before accepting the pointer, it requires the live component registry's factory to match native Dyna factory RVA `0x7BAAD0`, and the raw manager's owner to match the physical collision component obtained through the independently verified collision getter. These raw offsets are accepted only with those native-code/registry/owner guards, not because they happened to yield readable data.

The first three live `MapEditor_Test` samples in `artifacts/skyward-banner-20261023/clock-live-samples-playground-01` found **no ordinary FuncTree registrations**. Thus this CPU list is not evidence of the visible banner's effective frame clock. The zone reset origin was measurable (`3145 ms`) and all six origin equations held, while the native kinematic shared signal rotated consistently with the authored 20-second circle. Its animated matrix starts at shared signal `+0xC`; the constraint record's matrix is the unchanged base location. The instanced VertexTween GPU branch's packed timing remains a separate tracing target. Do not associate the zone clock with the banner merely because both exist in the same scene.

Evidence is append-only: `requests.jsonl` preserves exact requests/replies including failures, `roots.json` preserves semantic/reflection provenance, and `sample-*.json` preserves observations. A successful script result means the guarded reads completed. It does not prove visual attachment, synchronization, playback, embedding or compatibility without player plugins; those claims need the corresponding fixture and game/video evidence. Syntax and pinned disk-section reads have been verified offline; live guard/layout results must be recorded separately.

## Native packed instance timing

`trace_packed_clock_pe.py <unused-directory>` searches the pinned executable disk file for the exact phase/period constants and bit masks. Matches are candidates until interpreted. `dump_native_functions.py <unused-directory> <preferred-function-VA> ...` preserves all exception-table fragments sharing each requested function's ultimate unwind root; `--range START:END` permits a bounded explicit leaf excerpt. These tools never call the bridge or open a process.

The native CPU encoder at preferred VA `0x140260EA0` and native ordinary instance allocator at `0x140262E20` now establish the packed timing word's producer and CPU storage route. See `artifacts/skyward-banner-20261023/packed-clock-native-v3/findings.md`. For native manager mode0, its `+0xC0` buffer contains32-byte quaternion/translation/timing records, with the packed word at record+28; `+0xE0` supplies each record's model slot in the `+0x60` table. These offsets are code-derived, not borrowed from the forest manager.

```powershell
python Tests/SkywardBannerResearch/sample_hms_instance_timing.py <unused-directory> --expect-map "<exact fixture display name>"
```

This separate read-only observation reuses the paired sampler's identity/build guards, then verifies exact native encoder/caller/allocator bytes and requires the semantic `NHmsMgrInstDyna_SMgr` component to equal the allocator's manager at `CHmsZone+0x248`. It accepts the two code-derived native storage modes0 and1, bounds model/instance buffers, checks descriptor/mapping stability and retains raw bytes and decoded period/phase codes. Mode1 follows the native read accessor `0x140264DC0` and roster builder `0x140263380`: `+0x1D8` maps stable instance IDs to main `+0xC0` records or staged `+0x1E8` records according to bit31. Model indices come from `+0xE0` or `+0x208`; dead mappings are skipped and the roster's model metadata bit23 is removed. Other modes are refused. A successful CPU snapshot does not itself establish the current shader draw branch, resource binding, uploaded `cFrame` or `DrawV.TimeSc`.

`python Tests/SkywardBannerResearch/test_hms_instance_guards.py` runs four offline safeguards covering main/staged/dead resolution, out-of-range staged indices, changed mappings and unknown storage modes. It never opens a socket.
