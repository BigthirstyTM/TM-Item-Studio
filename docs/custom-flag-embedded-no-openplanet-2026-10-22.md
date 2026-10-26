# Embedded custom flags without Openplanet — 22 October 2026

## Demonstrated result

**Three embedded custom flag shapes visibly waved in fresh game processes without Openplanet or matching loose custom assets.** This passed both with the genuine native `Flag16m` serialized first and, in a separate cold process, serialized last. An otherwise identical no-native map loaded only the poles. These tests establish a map-contained rendering and animation proof on this client and a native-anchor dependence for the exact configuration tested, using the game's own material remapping.

**Native-written Items now also render correctly after a targeted file repair.** Restoring omitted dependencies alone had left all three custom Items invisible, even with a native flag present. Changing only their archive reference tables to ancestor-zero sibling paths restored all three poles and waving cloths in fresh no-Openplanet PID 16860. The exact native-written headers/raw bodies and accepted dependency bytes are preserved. This establishes a repairable export path; ordinary unmodified editor resaving still needs dependency collection and reference normalization.

The corrected native-written Items were also tested without a native anchor in a separate fresh no-Openplanet process, **PID 24704**. All three custom poles load, but all three cloths remain absent. The normalized pair shares the exact same embedded ZIP and custom anchor records. Reference portability is therefore repaired, while native-anchor-conditioned cloth visibility remains for these exact files.

- [Download the video](../artifacts/flag-selfcontained-20261022/embedded-v6-primer-no-openplanet/final/embedded-custom-flags-no-openplanet-1080p.mp4): overview, then separate close-ups of swallowtail, long and round cloth. Actual game capture, requested 60 Hz, measured capture rate in the adjacent validation file. No generated cloth motion or frame interpolation.
- [Download the original four-anchor test map](../artifacts/flag-selfcontained-20261022/material-remap-shapes-v6-near-native-primer/TMIS_Embedded_Flag_Shapes_v6_Near_NativePrimerFirst.Map.Gbx).
- [Map and six-file embedding manifest](../artifacts/flag-selfcontained-20261022/material-remap-shapes-v6-near-native-primer/TMIS_Embedded_Flag_Shapes_v6_Near_NativePrimerFirst.Map.Gbx.manifest.json).
- [Video validation and motion measurements](../artifacts/flag-selfcontained-20261022/embedded-v6-primer-no-openplanet/final/validation.json).
- [Contact sheet of real capture moments](../artifacts/flag-selfcontained-20261022/embedded-v6-primer-no-openplanet/final/motion-contact-sheet.jpg).
- [Native-last cold-process video](../artifacts/flag-selfcontained-20261022/embedded-v6-native-last-no-openplanet/final/native-last-embedded-custom-flags-no-openplanet-1080p.mp4): all three shapes waving with the native anchor at index 3; [English findings](../artifacts/flag-selfcontained-20261022/embedded-v6-native-last-no-openplanet/findings.md), [frame and motion validation](../artifacts/flag-selfcontained-20261022/embedded-v6-native-last-no-openplanet/final/validation.json), and [native-last map](../artifacts/flag-selfcontained-20261022/material-remap-shapes-v6-native-last/TMIS_Embedded_Flag_Shapes_v6_NativeLast.Map.Gbx).
- [Native-save reference-repair video](../artifacts/flag-selfcontained-20261022/native-resave-sibling-primer-cold-no-openplanet/final/native-save-reference-repaired-flags-no-openplanet-1080p.mp4): native-written Items with normalized sibling references, all three poles and cloths visible and waving; [English findings](../artifacts/flag-selfcontained-20261022/native-resave-sibling-primer-cold-no-openplanet/findings.md), [frame/motion validation](../artifacts/flag-selfcontained-20261022/native-resave-sibling-primer-cold-no-openplanet/final/validation.json), [contact sheet](../artifacts/flag-selfcontained-20261022/native-resave-sibling-primer-cold-no-openplanet/final/native-save-reference-repair-motion-contact-sheet.jpg), and [repaired test map](../artifacts/flag-selfcontained-20261022/native-resave-sibling-refs/TMIS_v6_Three_NativeResave_SiblingRefs_NativePrimerFirst.Map.Gbx).
- [Three-control kinematic comparison video](../artifacts/flag-selfcontained-20261022/kinematic-v6-cold-comparison/final/kinematic-v6-three-cold-controls-1080p.mp4): 75 seconds of actual source frames in enlarged fixed close-up. Baseline cloth visibly deforms; stationary kinematic cloth has a frozen appearance; rotating kinematic cloth rotates, with independent waving unestablished. [English motion findings](../artifacts/flag-selfcontained-20261022/kinematic-v6-cold-comparison/findings.md).

The final video contains all **1,600** source frames, lasts **28.9834 seconds**, and is 1920×1080 with variable frame timing. The four original captures average approximately 54–57 fps. Fixed cloth crops change substantially more than stationary grass controls: mean adjacent-frame motion ratios are 17.81× for the swallowtail close-up, 48.11× for long and 98.13× for round. The three cloth regions in the overview also pass independently.

The original capture timestamps contain irregularities. The output retains all frames; 13 output presentation timestamps differ from ffprobe's per-file reconstruction, with a maximum local difference of 66.7 ms. Four muxer monotonicity corrections are one 1/15360-second tick each. There is no frame generation, fps conversion or speed filter; exact timing details are retained in validation.json.

The separate native-last video contains all **380** original frames over **6.983333 seconds**, measured at **54.415 captured fps**. Its labelled 1080p copy retains every source frame and the original duration, without interpolation or fixed-rate conversion. Fixed cloth crops exceed the stationary background's mean adjacent-frame variation by **32.20×** for round, **34.95×** for long and **18.96×** for swallowtail; the fixed-pole control varies less than the background. Two output timestamps differ from the decoder's best-effort reconstruction by one 1/15360-second tick each. [Contact sheet](../artifacts/flag-selfcontained-20261022/embedded-v6-native-last-no-openplanet/final/native-last-motion-contact-sheet.jpg).

The native-save reference-repair video retains all **386** original frames, measured at **55.274 captured frames per span-second** over **6.983333 seconds**. Its labelled 1080p copy lasts **6.983398 seconds**, one 1/15360-second tick longer. Three output timestamps differ from the decoder's best-effort source reconstruction by at most one tick; the source contains 60 duplicate raw PTS. Cloth-to-background mean adjacent-frame difference ratios are **63.78×** for round, **38.20×** for long and **36.96×** for swallowtail. The fixed-pole control varies less than the background, and visible cloth outlines/logos deform. No source frames were generated or dropped, and no interpolation or fixed-rate conversion was performed.

The original demonstration maps are editor fixtures. A separate gravity track has now completed genuine native validation in 8,060 ms, and its native save/validation ghost survive the bounded repair; native shadow-baked/full-saved and re-repaired outputs have now been accepted by cold ordinary Local Play. The elevated-coordinate candidate also shows visible custom cloth deformation during the fixed race countdown before the car moves, with the Long250 lower edge partly occluded by the railing. The result has not yet been repeated on a second computer or in multiplayer. A fully self-contained single Item, preservation through unmodified native editor resaves, and simultaneous kinematic flight plus cloth animation remain separate requirements.

## Isolation and observations

The game was fully stopped at the menu. The original Openplanet `dinput8.dll` proxy was hash-checked and temporarily moved outside the game directory before a new game process was launched. The test used the normal native menu: Create â†’ Track Editor â†’ Edit a Track â†’ My Local Tracks. No diagnostic bridge or helper could operate inside that process.

Fresh process: **PID 20672**, started **2026-10-22 11:12:37 local time**. This map was the first map opened in that process. The [process receipt](../artifacts/flag-selfcontained-20261022/embedded-v6-primer-no-openplanet/process-before.json), [module list before loading](../artifacts/flag-selfcontained-20261022/embedded-v6-primer-no-openplanet/modules-before.json), [module list after the overview](../artifacts/flag-selfcontained-20261022/embedded-v6-primer-no-openplanet/modules-after.json), and [module list after all close-ups](../artifacts/flag-selfcontained-20261022/embedded-v6-primer-no-openplanet/modules-final.json) establish:

- No loaded module has an Openplanet path.
- The only `dinput8.dll` is `C:\Windows\System32\dinput8.dll`.
- `Openplanet.log` retained its exact size and modification timestamp throughout the experiment.
- The six matching asset filenames were absent from the user Items and loose game Items roots; [isolation receipt](../artifacts/flag-selfcontained-20261022/embedded-v6-primer-no-openplanet/loose-isolation.json).
- Native packs, the pre-existing user flag, Editor.op and the original proxy were hash-checked before the experiment; [protected files](../artifacts/flag-selfcontained-20261022/embedded-v6-primer-no-openplanet/protected-before.json).

In the original overview, round is on the left, long in the middle and swallowtail on the right. All visibly change shape over time while the camera is fixed. The native primer is at `(16,8,16)`, far outside that view; the custom flags are at height 128 near the initial editor camera.

The swallowtail and round close-ups use two framing copies of the same map. All three custom anchors were translated together by +20 or âˆ’20 metres in X so the selected flag aligns with the native camera's fixed centre. The native primer and the entire six-file embedded ZIP are byte-identical to the original. The zoom itself uses the native editor's free-look tool. The recording cuts between these views; it is not presented as a continuous camera tour. [Framing manifests](../artifacts/flag-selfcontained-20261022/material-remap-shapes-v6-closeup-framing/findings.md).

## What the file solution does

Each custom Item retains the animated mesh, including all five LOD visuals and 86 animation frames per visual. A small ordinary placeholder material provides an exact source FID. An embedded GameSkin and MaterialModifier ask the engine to replace that material with its own packed `ItemFlag.Material.Gbx`. The engine then rebuilds the effective mesh with the structured animation buffers required by the native shader.

All custom payloads travel in the map's embedded ZIP: three Items plus the placeholder material, GameSkin and modifier. The replacement material and native primer already exist in Trackmania. This route does not install a custom shader, modify the native packs, write renderer memory, or require a flag companion plugin.

The corresponding single-custom-flag native-primer map also passed a cold process with the general Openplanet baseline loaded: **5/5 effective structured visuals**, exact native ItemFlag material, and visible waving. [Runtime graph](../artifacts/flag-selfcontained-20261022/embedded-v6-primer-cold3/effective.json); [cold recording](../artifacts/flag-selfcontained-20261022/embedded-v6-primer-cold3/native-primer-cold-60hz.mp4), 474 actual frames over eight seconds, 59.25 fps. The no-Openplanet process was verified through visible motion and module/file isolation, not pointer diagnostics.

## What remains unresolved

### Native-first / no-native / native-last cold comparison

Earlier cold loads without a native anchor failed with general Openplanet loaded: the source meshes remained 0/5 structured and the material remap cached a null result. The same files later worked in a warm session. Adding the native anchor first passed the next cold test, and the three-shape version then passed without Openplanet.

The controlled no-Openplanet comparison has now also run. **Fresh PID 28152**, started **2026-10-22 11:57:40 local**, opened the three-shape map as its first map, with no native anchor. Its complete six-file ZIP is byte-identical to the successful primer map. The game loaded normally, but **only the three poles were visible; all three cloths were absent**. Both module snapshots show no Openplanet and system dinput8; the Openplanet log is unchanged, and no matching loose assets are present. [Comparison verdict](../artifacts/flag-selfcontained-20261022/embedded-v6-no-primer-no-openplanet-retry2/verdict.json); [actual no-primer recording](../artifacts/flag-selfcontained-20261022/embedded-v6-no-primer-no-openplanet-retry2/no-primer-cloth-absent.mp4).

The native-last control has now also passed. **Fresh PID 2272** opened the native-last map as its first map after startup. All three cloths were visible and waving. The only relevant anchor-list change from the native-first source is moving native `Flag16m` from index 0 to index 3; the three custom anchors retain their relative order and all four retain their exact properties. Its entire six-file embedded ZIP is byte-identical to both other comparison maps. Before, after and final saved module receipts show no Openplanet and system `dinput8.dll`; the log is recorded unchanged and no matching loose fixture assets were found. [Native-last verdict](../artifacts/flag-selfcontained-20261022/embedded-v6-native-last-no-openplanet/verdict.json).

| Fresh process, no Openplanet | Native anchor | Embedded ZIP | Observed result |
|---|---|---|---|
| PID 20672 | First, index 0 | Same six payloads | **PASS:** all three custom cloths visible and waving |
| PID 28152 | Absent | Same six payloads | **FAIL:** three poles visible; all three cloths absent |
| PID 2272 | Last, index 3 | Same six payloads | **PASS:** all three custom cloths visible and waving |

The shared complete ZIP SHA-256 is `04C3DDA39315E92728755138FF8FAA82D7D9378F938A2868CFCD0D59B3627654`. Each test used a distinct game process; first-map-after-start is the recorded test procedure, while the process/module receipts independently establish the process boundary and absence of Openplanet.

Removing Openplanet therefore does not fix the no-native cold failure. For these exact files, a native flag in the map restores visible cloth animation, and it need not be the first serialized anchor. This does not prove that a native flag globally switches on a shader, that serialized anchor order guarantees engine build order, or that every possible custom graph needs a primer. Actual engine build order was not instrumented in the no-Openplanet processes.

The first no-primer launch attempt produced a window-capture failure before any map was opened; it is explicitly marked not tested. A later native UI launch restored the game view and the comparison above ran successfully. Neither that aborted attempt nor a failed capture is counted as a rendering failure.

The native code actively loads the replacement material once its FID is found. A failed first remap is cached and falls back to the original mesh. Later valid FIDs and parameters do not prove they were ready during that initial callback. [Cold ordering audit](../artifacts/flag-selfcontained-20261022/cold-order-audit/findings.md).

### Ordinary editor resaving needs payload and reference repair

Trackmania successfully wrote a native-resaved three-shape map, but its ZIP contains only the three Items. Their references still point to the placeholder and modifier, while those files and the GameSkin were omitted. Their increased sizes are explained by uncompressed serialization; the geometry is unchanged, and the dependencies were not internalized. This saved map is therefore not a portable dependency-complete result. [Native save audit](../artifacts/flag-selfcontained-20261022/native-resave-audit/findings.md).

An offline repair candidate restores the three exact dependency payloads alongside the byte-identical native-written Items. Reparse and ZIP byte checks passed. Its earlier "closure" check established payload coverage only under an assumed `ContentLoaded/Items/...` mount and ordinary lexical ancestor resolution; it did **not** prove that the native loader accepts those paths after reopening the map.

Both dependency-only repaired native-save cold tests failed visually, with normal map loading but **no custom poles or cloths visible at their original camera location**:

| Fresh process, no Openplanet | Native anchor | Observed result |
|---|---|---|
| PID 27652 | Absent | No custom poles or cloths visible |
| PID 33392 | First, index 0 | No custom poles or cloths visible; native anchor is far outside the view |

Both runs used the repaired map as the first map after startup, with matching loose assets absent. Adding the native primer alone did not restore these native-written Items. This differs from the original no-native v6 map, where the poles remained visible; the observation must not be attributed solely to the cloth shader. It also does not by itself prove whether Item loading, identity lookup or another rendering condition failed. [No-native verdict](../artifacts/flag-selfcontained-20261022/native-resave-repaired-cold-no-openplanet/verdict.json); [no-native recording](../artifacts/flag-selfcontained-20261022/native-resave-repaired-cold-no-openplanet/repaired-native-save-no-visible-flags.mp4); [native-first verdict](../artifacts/flag-selfcontained-20261022/native-resave-repaired-primer-cold-no-openplanet/verdict.json); [native-first recording](../artifacts/flag-selfcontained-20261022/native-resave-repaired-primer-cold-no-openplanet/repaired-save-native-primer-no-custom-flags.mp4).

Static inspection found a relevant native root restriction. For an ordinary file FID without an alternate archive context, a proposed ancestor folder outside the global data root is rejected; the temporary-drive exception applies only to ancestor zero. Under those runtime conditions, the native-written `ancestor=3` references beginning `ContentLoaded/Items/...` can be resolved from the original Item folder instead, producing a duplicated directory prefix and missing their restored dependencies. A failed `UseFile=false` dependency can abort the entire parent Item before its body is read, including the pole. The current FID flags, archive context and actual failure branch were not measured in these no-Openplanet runs, so this is a concrete hypothesis rather than a proven runtime cause. [Reference-loader audit](../artifacts/flag-selfcontained-20261022/resave-reference-loader-audit/findings.md); [conditional path model](../artifacts/flag-selfcontained-20261022/resave-reference-loader-audit/path-model.json).

A reference-table-only comparison has now **passed** in fresh no-Openplanet **PID 16860**, with the native anchor still first. Ancestor zero and sibling dependency basenames restore all three custom poles and visibly waving cloths. Before, after and final module receipts exclude Openplanet and show the system `dinput8.dll`; the log is recorded unchanged, and matching loose custom assets are absent. First-map-after-start is the recorded test procedure; the process receipts independently establish a distinct game process.

| Fresh process, no Openplanet | Native-written reference layout | Native anchor | Observed result |
|---|---|---|---|
| PID 33392 | Ancestor 3, `ContentLoaded/Items/...` paths | First, index 0 | No custom poles or cloth visible |
| PID 16860 | Ancestor 0, sibling basenames | First, index 0 | **PASS:** all three poles and waving cloths |
| PID 24704 | Ancestor 0, sibling basenames | Absent | Three poles visible; all three cloths absent |

Each native-written Item's exact header, node count and uncompressed raw body remain byte-identical. Serialized record flags 1, node indices 25/26 and `UseFile=0` are unchanged, as are all three dependency payloads. The Item table changes only ancestor/directory layout and paths, shrinking from 289 to 123 bytes; restoring the old table reproduces the complete original native-written Item byte-for-byte. This is one combined reference-table intervention, not a separate test of each sub-edit. Map UIDs/names and header XML also change for the distinct fixtures, while custom anchor transforms/properties are preserved. [Offline splice invariants](../artifacts/flag-selfcontained-20261022/native-resave-sibling-refs/findings.md); [independent byte audit](../artifacts/flag-selfcontained-20261022/native-resave-sibling-refs/independent-raw-splice-audit.json); [cold verdict](../artifacts/flag-selfcontained-20261022/native-resave-sibling-primer-cold-no-openplanet/verdict.json).

This A/B strongly supports a reference-portability correction, without proving that the conditional root-guard branch above executed. Native save is usable as a source for repaired Items, but a portable authoring/export workflow must both collect the omitted dependencies and normalize references after saving. It must not rely on unmodified native save to preserve the six-file graph.

A bounded offline repair CLI is now implemented in [Tests/FlagMapRepair](../Tests/FlagMapRepair/README.md). It accepts only the exact three known native-written flag bodies, requires an existing native `Flag16m`, normalizes their sibling references and restores the three pinned original dependencies. **Eleven offline checks passed**, covering all-three restoration, preservation of unrelated ZIP payloads/metadata/comments and map data, byte-identical repeat repair, and safe refusal cases. It writes a new map plus a hash manifest and never overwrites its source. Serialized validation bytes are preserved; accepted native validation, gameplay and cold loading of each final output still require game tests. [English utility report and checks](../artifacts/flag-selfcontained-20261022/export-repair-utility-check/findings.md); [machine-readable verification](../artifacts/flag-selfcontained-20261022/export-repair-utility-check/offline-verification-02/verification.json).

The normalized no-native comparison has now **failed for cloth visibility** in fresh no-Openplanet **PID 24704**: the map loads normally and all three custom poles are visible, while all three cloths are absent. Before, after and final receipts exclude Openplanet and identify the Windows system `dinput8.dll`; the Openplanet log is recorded unchanged and matching loose files are absent. The operator recorded this as the first map after startup. The two normalized maps share the exact complete six-file ZIP SHA-256 `3EC0A913F47911FBD9D5A3E8716E59B99792F346459EA337106672AC039D3758`, and their three custom anchor records are equal. Removing the native anchor is the relevant scene intervention; separate fixture UID/name/header XML changes prevent a claim of whole-map byte equality. [No-native findings](../artifacts/flag-selfcontained-20261022/native-resave-sibling-no-primer-cold-no-openplanet/findings.md); [verdict](../artifacts/flag-selfcontained-20261022/native-resave-sibling-no-primer-cold-no-openplanet/verdict.json); [original recording](../artifacts/flag-selfcontained-20261022/native-resave-sibling-no-primer-cold-no-openplanet/native-save-repaired-no-native-cloth-absent.mp4); [source-frame contact sheet](../artifacts/flag-selfcontained-20261022/native-resave-sibling-no-primer-cold-no-openplanet/analysis/normalized-no-native-cloth-absent-contact-sheet.jpg).

The negative-control recording contains **315 actual decoded frames over 6.000000 seconds**, measured at **52.5 frames per span-second**, with a nominal 60 Hz capture request. Its raw timestamps and bytes are preserved; no edited video or timestamp repair was created. The contact sheet uses original source frames at decoded best-effort times 1, 3 and 5 seconds. Together, the normalized comparison separates repaired Item geometry visibility from the remaining native-anchor-conditioned cloth rendering. It does not prove which shader/renderbuffer initialization step accounts for that condition or that every possible custom graph requires a native flag.

### Genuine native 8,060 ms validation and save

The gravity fixture completed a genuine native validation run. Before any exit injection, game receipts reported **best/finish time 8060 ms**. The native saved map independently parses with author 8060, gold 9000, silver 10000, bronze 13000, script-validation true, XML `validated="1"`, and a validation ghost whose race time is **0:08.060** and wall-clock recording is **11:31:38-11:31:46 UTC**. Native Save As produced UID `zHvMgTQlzdgDevztEyFlfyqipe8`; the ghost retains its earlier native challenge UID.

Exiting validation used a temporary MLHook layer that sent only `Event_RaceValidation_Quit`, with no time/medal/ghost assignment. This is the exact event used by the native validation menu's Quit button, confirmed in extracted native UI/event/mode scripts. The native mode saves the better author ghost/time before waiting for that menu event. This was an Openplanet-assisted native validation session, not a no-Openplanet run. The unmodified replay recording is preserved, but its sampled screen does not show a numerical author time; the numerical claim comes from game receipts and the saved native ghost.

A separate read-only audit confirms that repair preserves the **entire 113,754-byte map header/reference table**, all native non-ZIP body bytes except three ZIP length words, and every custom Item's native header/raw body. Reversing the ZIP splice restores the complete original raw map body exactly. Three original Item entries become six files by restoring the pinned material/skin/modifier; the three Item reference tables become ancestor-zero sibling references. Author/medal/ghost/validation/blocks/anchors are preserved as serialized data.

The first fresh no-Openplanet local-browser attempt, **PID 31248**, rejected the research folder with "Tracks found in this section are not suitable" **before loading any map**. It provides no race or cloth result. Reopening normally produced the native Save UI reminder that shadows must be computed to play the map; XML lightmap is 0. Native shadow computation and a full save were subsequently completed, followed by bounded repair and successful cold ordinary Local Play acceptance as documented next. Map type/style/title are consistent with official campaign maps; no playability flags have been invented.

[Detailed English native-validation and preservation findings](../artifacts/flag-selfcontained-20261022/gravity-native-save/findings.md); [original native save](../artifacts/flag-selfcontained-20261022/gravity-native-save/TMIS_Gravity_NativeValidated8060_20261022.Map.Gbx); [repaired candidate](../artifacts/flag-selfcontained-20261022/gravity-native-save/TMIS_Gravity_EmbeddedFlags_Validated_Repaired.Map.Gbx); [independent raw-preservation audit](../artifacts/flag-selfcontained-20261022/gravity-native-save/preservation-audit.json); [selected game receipts](../artifacts/flag-selfcontained-20261022/gravity-native-save/receipt-audit.json); [exact native event source](../artifacts/flag-selfcontained-20261022/gravity-native-save/native-event-source.json); [cold browser-rejection video](../artifacts/flag-selfcontained-20261022/gravity-repaired-localrace-cold-no-openplanet/localbrowser-not-suitable.mp4).

### Native shadow-baked map accepted by cold ordinary local Play

After the native missing-shadows reminder, the game computed shadows and performed an actual native full Save As. XML lightmap changed **0 -> 8** and body kind **InProgress -> Multi**; author 8060, medals, script-validation and native ghost remained. A second bounded repair preserves all native non-ZIP bytes, including the computed lightmaps and validation. The exact repaired output is **1,730,875 bytes**, SHA-256 `CA573B32C9894E73EC7462B525E5F8A5C83EC7B36E8F3A6A58578BE148BCFB19`.

Fresh no-Openplanet **PID 25436** accepted that map through the ordinary local Play browser, with the indistinguishable native-original duplicate temporarily isolated. It loaded as the process's first map and a native 8.060-second finish was observed. The preserved post-finish replay visibly displays **00:08:060 on the native finish arch**. Independent before/after/final module, process, log and loose-asset receipts pass. This establishes a native shadow/save -> repair -> cold ordinary Play path on this client; the bake/save changes do not isolate a single causal metadata bit.

[Download the 1080p post-finish replay](../artifacts/flag-selfcontained-20261022/gravity-shadowbaked-localrace-cold-no-openplanet/final/native-shadowbaked-post-finish-replay-no-openplanet-1080p.mp4): all **271 actual source frames**, **5.000000 seconds**, **54.2 captured fps**, full viewport and English captions. No interpolation, fixed-rate conversion or intentional speed change; two output timestamps differ from source decoded best-effort reconstruction by at most 1/15360 second. The recording is a post-finish replay; the 45-second predecessor clip shows the browser only, so there is no continuous selection/load/run recording. **Custom cloth visibility and waving are not established in this race camera view.** This does not replace the separate cold editor rendering/deformation evidence above.

[Detailed English native-save/race findings](../artifacts/flag-selfcontained-20261022/gravity-native-save/findings.md); [accepted repaired map](../artifacts/flag-selfcontained-20261022/gravity-native-save/TMIS_Gravity_EmbeddedFlags_ShadowBaked_Repaired.Map.Gbx); [native shadow-baked original](../artifacts/flag-selfcontained-20261022/gravity-native-save/TMIS_Gravity_NativeValidated8060_ShadowBaked.Map.Gbx); [independent new metadata](../artifacts/flag-selfcontained-20261022/gravity-native-save/shadowbaked-metadata-audit.json); [new raw preservation audit](../artifacts/flag-selfcontained-20261022/gravity-native-save/shadowbaked-preservation-audit.json); [video validation](../artifacts/flag-selfcontained-20261022/gravity-shadowbaked-localrace-cold-no-openplanet/final/validation.json); [receipt/video audit](../artifacts/flag-selfcontained-20261022/gravity-shadowbaked-localrace-cold-no-openplanet/analysis/receipt-video-audit.json).

A later recording from the same PID 25436 session now visibly captures the native **PERSONAL BEST 00:08.060 and author-medal result menu**. [Download the stronger 1080p result-menu video](../artifacts/flag-selfcontained-20261022/gravity-shadowbaked-localrace-cold-no-openplanet/final-result-menu/native-localplay-personal-best-8060-no-openplanet-1080p.mp4): all **1,616 actual source frames**, full **30-second decoded span**, **53.8667 captured fps**; the source's declared 29.983333-second duration omits the end of its final complete frame. No interpolation, fixed-rate conversion or intentional speed change. [Exact timing validation](../artifacts/flag-selfcontained-20261022/gravity-shadowbaked-localrace-cold-no-openplanet/final-result-menu/validation.json); [native result frame](../artifacts/flag-selfcontained-20261022/gravity-shadowbaked-localrace-cold-no-openplanet/analysis/personal-best-menu-at10s.jpg).

A second fresh no-Openplanet process, **PID 35212**, records browser-to-loading-to-map introduction for the same exact repaired file, with independent before/after/final isolation checks passing. Its 35-second source ends before a complete race/finish. [Second cold-load findings and raw recording](../artifacts/flag-selfcontained-20261022/gravity-shadowbaked-localrace-cold-repeat-no-openplanet/findings.md). **Recognizable custom cloth and waving during local Play remain unestablished in these camera views.**

The later PID 35212 sources also visibly record **PERSONAL BEST 00:08.060, delta +00:00.000 and the author medal**, alongside native replay. No complete new run or recognizable custom waving is established in the sampled views. [Second-process result frame and honest source classifications](../artifacts/flag-selfcontained-20261022/gravity-shadowbaked-localrace-cold-repeat-no-openplanet/findings.md).

### Elevated-coordinate native Save As and cold ordinary-race visibility

To put the three custom shapes beside the elevated start road, a native Save As
was performed after the coordinate-only candidate was reopened. The resulting
map has a new native identity, `TMIS_Gravity_Three_Shapes_Start_Visibility_Native`
with UID `e_UKdQdeOKM4jnD0mXmJbTRMqI1`, XML `lightmap="8"`, `Kind=Multi`,
`validated="1"`, four anchors and the same serialized 8.060-second validation
ghost. The exact native source is SHA-256
`431C9F7B40652DE7941E68C0005175443C9E80E8FAEF2BAEC918904A7C157E5C`; the
repaired, dependency-complete candidate is
`F4899504479FEED65073C29D0DA7D16D0F3F703AE743B5A2706CA2E171C7869E`.

The independent audit confirms that the native anchor is at `(16,8,16)` and
the custom Swallowtail, Long250 and Round anchors are at
`(758,44.17434,430)`, `(758,39.88715,446)` and `(802,44.17434,430)`.
The repair retains the native Item headers/raw bodies, validation data and
shadow-cache bytes while restoring the three omitted dependency files and
normalizing Item references. XML/cache presence is serialized readiness metadata
only: an earlier `ComputeShadows bustCache=true` bridge loss means fresh shadow
computation at the translated coordinates is not claimed here. [English native
visibility-save findings](../artifacts/flag-selfcontained-20261022/gravity-start-race-visibility-native/findings.md),
[native source](../artifacts/flag-selfcontained-20261022/gravity-start-race-visibility-native/TMIS_Gravity_Three_Shapes_Start_Visibility_Native.Map.Gbx),
[repaired candidate](../artifacts/flag-selfcontained-20261022/gravity-start-race-visibility-native/TMIS_Gravity_Three_Shapes_Start_Visibility_Native_Repaired.Map.Gbx),
and [hash manifest](../artifacts/flag-selfcontained-20261022/gravity-start-race-visibility-native/artifact-hashes.json).

The subsequent fresh no-Openplanet process loaded this repaired hash as its
first map through the ordinary Local browser. The [90-second source recording](../artifacts/flag-selfcontained-20261022/gravity-start-race-visibility-cold-no-openplanet/ordinary-race-first-map-90s.mp4)
contains browser selection, countdown, the complete native run, finish and
result menu. The car finishes in **00:08.060** and the native result menu shows
the Author medal and Personal Best **00:08.060**. The [1080p proof excerpt](../artifacts/flag-selfcontained-20261022/gravity-start-race-visibility-cold-no-openplanet/final/ordinary-local-race-custom-cloth-8060-no-openplanet-1080p.mp4)
contains 2,084 actual source frames over 40 seconds, with English captions and
no generated frames, interpolation or fixed-rate conversion. The source SHA-256
is `11B68365B9B82B36D85DBA7DF8EA6633E75A7C4FB0809A99DA300E4EE4D04C75`; the
proof excerpt SHA-256 is
`B638CE6803C2924F5E3300016B11921AD43D4FB6D3C99121D8F984A8DE70D9FF`.

During the fixed countdown before the car moves, visible custom cloth regions
change outline and folds while the poles, road and fixed stadium structures
remain stationary. Round and Swallowtail are clearly identifiable in the
camera; the Long250 cloth is visible but its lower portion is partly or mostly
occluded by the railing. The [independent English race findings](../artifacts/flag-selfcontained-20261022/gravity-start-race-visibility-cold-no-openplanet/findings.md)
and [fixed-countdown contact sheet](../artifacts/flag-selfcontained-20261022/gravity-start-race-visibility-cold-no-openplanet/analysis/fixed-countdown-source-comparison.jpg)
record the per-region measurements and this qualification. Cold-process
receipts show no Openplanet module, the system `dinput8.dll`, unchanged
Openplanet log and no matching loose files.

[English cold-race findings and timing audit](../artifacts/flag-selfcontained-20261022/gravity-start-race-visibility-cold-no-openplanet/findings.md); [hash manifest](../artifacts/flag-selfcontained-20261022/gravity-start-race-visibility-cold-no-openplanet/artifact-hashes.json).

This proves embedded custom cloth visibility and deformation in one ordinary
cold Local race on this client. It does not isolate the exact GPU/renderbuffer
write, prove second-client or multiplayer visibility, or remove the native
`Flag16m` anchor requirement. The earlier `ComputeShadows bustCache=true`
bridge loss remains a separate diagnostic event; this race proves final map
acceptance but does not isolate that event as a causal variable.

### Single-Item initialization and kinematics

Static review ruled out three apparent shortcuts: ArchetypeRef replaces the custom entity model with the native one; a native ItemModel is not a supported Prefab child; the legacy group ItemRef parser does not load that item. [Item-owned primer audit](../artifacts/flag-selfcontained-20261022/item-primer-audit/findings.md).

A candidate externalizes only `Dyna.Mesh` into an embedded sibling Mesh.Gbx, preserving the exact v6 material mechanism. Recombining it produces the entire original Item byte-for-byte, and its native class and file references are compatible. This changes the archive/postload boundary without adding a native anchor. Native preload in a zero-anchor map passed: all five source visuals retain 86 frames, the inline ShaderApply is non-null, the GameSkin source FID equals the material FID, and the replacement folder exists. [Candidate review](../artifacts/flag-selfcontained-20261022/item-primer-audit/external-candidate-review/findings.md); [native preload gates](../artifacts/flag-selfcontained-20261022/external-mesh-native-preload/preload-gates.json).

The separate cold anchored test **failed visually** in fresh no-Openplanet **PID 18552**, with this candidate as the first map and no native anchor. The single custom pole was visible but its cloth was absent; loading completed without a crash. All five graph files were embedded, matching loose assets were absent, both module snapshots show no Openplanet and system `dinput8.dll`, and the log remained unchanged. Moving only the mesh across an archive boundary therefore did not remove the dependence in this test. Preload acceptance was not rendering success. [Cold verdict](../artifacts/flag-selfcontained-20261022/external-mesh-cold-no-openplanet/verdict.json); [actual recording](../artifacts/flag-selfcontained-20261022/external-mesh-cold-no-openplanet/external-mesh-cloth-absent.mp4).

The selected-Prefab archive-boundary candidate has now also **failed visually** in fresh no-Openplanet **PID 20140**, as the recorded first map after startup and without a native anchor: one custom pole is visible, but its cloth is absent. Only the selected Prefab is external; VariantList stays inline, and Dyna/Solid2 stay inline inside that Prefab. Reinlining the final parsed Prefab reproduces the entire accepted original compressed v6 Item byte-for-byte. All five custom payloads are embedded. Before, after and final module/absence receipts exclude Openplanet, identify system `dinput8.dll`, record the log unchanged and show no matching loose assets. [Cold English findings](../artifacts/flag-selfcontained-20261022/external-prefab-cold-no-openplanet/findings.md); [verdict](../artifacts/flag-selfcontained-20261022/external-prefab-cold-no-openplanet/verdict.json); [original recording](../artifacts/flag-selfcontained-20261022/external-prefab-cold-no-openplanet/external-prefab-pole-only.mp4); [source-frame contact sheet](../artifacts/flag-selfcontained-20261022/external-prefab-cold-no-openplanet/analysis/external-prefab-cloth-absent-contact-sheet.jpg).

Its separate empty-map native source preflight passed: five visuals with 86 frames each, 0/5 structured source visuals as expected for the placeholder source, non-null cached `CPlugShaderApply`, exact material/GameSkin source-FID equality and a non-null replacement folder. This source preflight did not build or draw an instance, and did not establish an effective remapped mesh or GPU readiness. Its diagnostics belong to a preceding Openplanet-assisted process, not the cold no-Openplanet observation. [Preflight source gates](../artifacts/flag-selfcontained-20261022/external-prefab-empty-native-preflight/preload-gates.json); [independent archive review](../artifacts/flag-selfcontained-20261022/item-primer-audit/external-prefab-review/findings.md). The raw cold recording contains **334 actual frames over 6.000000 seconds**, or **55.666667 frames per span-second**, without reencoding or timestamp repair. The post-test restoration receipt records the original proxy restored exactly and five protected hashes matched.

A normalized-reference native-save comparison **with the native anchor** has passed as described above. Its no-native counterpart loads the poles but fails to show cloth. Both tested external-boundary routes (Mesh-only and selected-Prefab-only) also load a visible pole without visible cloth. An ordinary race run for this normalized-reference comparison remains pending; the separate shadow-baked gravity map described above has since passed cold ordinary Local Play acceptance. These failures apply to the exact tested graphs; they do not determine whether a different graph or initialization route can work without a native anchor. The precise shader/renderbuffer state was not instrumented in the cold no-Openplanet runs.

### Kinematic repeat: visible rotation, failed waving control

The embedded v6 route has now been tested in three separate fresh no-Openplanet processes, each with the genuine native flag first. The exact non-kinematic Long250 baseline visibly deforms. The zero-angle kinematic control remains visible but retains the same observed tip fold, outline and logo geometry over 25 seconds. The rotating control visibly turns through face-on, edge-on and reversed views, with approximately 12-second pose recurrence; independent cloth waving during that rotation is not established.

| Cold control | Fresh process | Source frames / 25 seconds | Observed result |
|---|---:|---:|---|
| Ordinary non-kinematic baseline | PID 30780 | 1,309 / 52.36 fps | Visible cloth deformation |
| Stationary kinematic, zero angle range | PID 21644 | 1,360 / 54.40 fps | Visible, frozen cloth appearance; no deformation observed |
| Rotating kinematic | PID 34144 | 1,382 / 55.28 fps | Rigid rotation visible; independent waving unestablished |

All source frames were measured in fixed original-pixel crops. Tight cloth/background mean adjacent grayscale difference ratios are **35.17x** for the baseline and **0.62x** for the stationary control. A descriptive threshold-65 mask changes by at most **6,439 pixels** from its initial baseline crop versus **80 pixels** for stationary. Visible tip/outline changes and the [actual source-mask contact sheet](../artifacts/flag-selfcontained-20261022/kinematic-v6-cold-comparison/final/baseline-stationary-source-mask-contact.jpg) support that distinction. Small lighting/compression differences remain; distinct frames or global silhouette changes alone are not evidence of waving, particularly during rigid rotation.

The conversion retains the exact cloth mesh, all five 86-frame visual archives, original cloth Dyna/rest transforms, period/phase settings and dependency bytes. Stationary and rotating Items differ only in display name and two angle maxima. Compared with baseline, the conversion also changes the pole to Dyna, sets kinematic bodies, adds world-relative constraints and diagnostic triangle collisions; it does not isolate a single `IsKinematic` bit. The result therefore does not prove the precise freeze mechanism or a universal kinematic limitation. These diagnostic collisions also prevent a vanilla collision-parity claim. [Offline fixture findings](../artifacts/flag-selfcontained-20261022/kinematic-v6-embedded/findings.md).

The [downloadable 1080p comparison](../artifacts/flag-selfcontained-20261022/kinematic-v6-cold-comparison/final/kinematic-v6-three-cold-controls-1080p.mp4) contains all **4,051 actual source frames** over **75.000000 seconds**, in baseline/stationary/rotating order, with a fixed source crop enlarged and English captions. No interpolation, generated motion, frame dropping, fixed-rate conversion or intentional speed change was performed. Sixteen output timestamps differ from per-file decoded best-effort reconstruction by at most 33.333 ms; raw-PTS differences reach 50 ms. Variable timing and the full 75-second duration remain. [Validation and exact timing](../artifacts/flag-selfcontained-20261022/kinematic-v6-cold-comparison/final/validation.json); [detailed English result and original recordings](../artifacts/flag-selfcontained-20261022/kinematic-v6-cold-comparison/findings.md).

This establishes visible kinematic rotation for the embedded/native-primer configuration, but not simultaneous independent waving. A moving parent that preserves the original non-kinematic cloth instance, or a more isolated conversion comparison, needs a separate structural/native test. A translated flight path and waving Skyward banner remain unestablished.

### Combined baked rotation and retained cloth-fold animation

A separate cold no-Openplanet recording now visibly establishes the combined
visual result: the long custom cloth turns around its unchanged static pole
while its folds and tip shape change. The fixed editor camera excludes camera
motion as the cause. The 30-second source contains 1,792 decoded frames at
3840×2160 (59.733 frames per span-second); repeated poses are consistent with
the requested 12-second cycle. [Download the enlarged flag video](../artifacts/flag-selfcontained-20261022/baked-rotate-wave-v6-cold-repeat-no-openplanet/baked-rotate-wave-flag-crop-30s-1080p.mp4)
or the [original 4K source](../artifacts/flag-selfcontained-20261022/baked-rotate-wave-v6-cold-repeat-no-openplanet/baked-rotate-wave-valid-editor-30s.mp4).
The crop retains the same frame count and duration.

This is **baked vertex animation**, not proof that independent physical
kinematics and cloth simulation run together. Each of the five 86-frame cloth
LODs contains the retained source fold positions with one rigid rotation baked
into every frame. The ordinary non-kinematic Dyna host plays this shared
timeline; no LocAnim or kinematic constraint is added, and the pole remains
static. Independent file checks confirm the material/reference route, all LODs,
normals, bounds, and bitwise-equal loop endpoints. No gross disappearance or
attachment break is visible, but endpoint equality alone does not prove a smooth
velocity seam: the offline finite-difference seam change is about 9.06 m/s at
12 seconds, compared with 8.67 m/s in the original fold source. A subtle loop
kink remains possible. The tested map still contains the genuine native
`Flag16m` primer; whole-assembly flight, moving collision and second-client
confirmation remain outside this result. [Independent English video findings](../artifacts/flag-selfcontained-20261022/baked-rotate-wave-v6-cold-repeat-no-openplanet/independent-video-findings.md)
and [independent offline geometry audit](../artifacts/flag-selfcontained-20261022/baked-rotate-wave-v6-embedded/independent-audit.md).

## Exact artifact identities

Original three-shape primer map SHA-256: `DA5980E4669DA8CB5D1589F3081AF24BB8D19B998D32B60701F279BA6B48F1D4`.

Complete embedded ZIP SHA-256, shared by the original and both framing copies: `04C3DDA39315E92728755138FF8FAA82D7D9378F938A2868CFCD0D59B3627654`.

The no-native and native-last comparison maps also retain that complete ZIP hash. Native-last map SHA-256: `BA573FDF78FF93C8602EEDB17FDB08041DCC02DF3DC67A1EB584BE095E95E836`.

Final video SHA-256: `68A850E0291BCEFDB9CCA1AC7A1C74D33EDA8F3BCD1A50796053CE62DD6653F3`.

Native-last labelled video SHA-256: `2E91F867E7092760AD76988F4972CD418F126AAB8E786B94B0DC2F6C0C66D256`. External-mesh no-native map SHA-256: `49DF259C04F1C7236FB9787D447A38AB3799886FBF7BBFFC692012BF09FE5A7B`.

Normalized-reference native-save primer map SHA-256: `43837322D1E3E0CAE2C485FAD34723C5EC798096D071644A6E5799ADB6761AB8`; its complete six-file ZIP SHA-256 is `3EC0A913F47911FBD9D5A3E8716E59B99792F346459EA337106672AC039D3758`. Its labelled video SHA-256 is `E784FA1D366E613A2A279E2CAAA322CB06A99D27B83FDBC9A3C57E4750505663`.

Normalized-reference no-native map SHA-256: `BDF64521502B4846428C42A6F1507E87978A12B4113EBE007CB668D512D2A8EB`; its complete ZIP matches the normalized primer map. Its original recording SHA-256 is `7EBFE431F2A5C4C0AB58128218E35E2E1E40458606FC22BE67ED28F9C4323DA5`.

External selected-Prefab no-native map SHA-256: `D3AC4F05C37517DB23E6097256762F53B2ED43D3C6355C744914E40B5E06A2BF`; its complete five-file ZIP SHA-256 is `F585B0C73D412C6C6B5D3D25BADEBF6E0B220F87CBAD4C5DFFB53BE44DDE01BE`. Its original recording SHA-256 is `518538E9FAB50D01A5478887114D7C178E7FCE00F6A149A336EE142405BABE60`.

Three-control kinematic comparison video SHA-256: `03A08CA9D484781FF10C54AFF3FF6143C586DEF0BB65A4B973D80A7AB40DD085`.

Trackmania build `2026-02-02_17_51`; executable SHA-256 `3FC7D8CDA542BEDA131C44306B123F4004D07D7E22F512B46B762AFC29F6EDDA`. The original Openplanet proxy SHA-256 is `EA33916C724553643A2D8F0C0EB00F332311596050A71CC1006B0854FD82A0D7`.
