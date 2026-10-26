# Standalone custom flags: shader resolution and cold-load tests

21 October 2026. Report language: English.

**No genuinely standalone, restart-safe custom flag file has been established.** The new positive control does show that the native engine can initialize and animate a custom long flag without our GPU flag helper. It succeeds when its shader reference resolves to the actual packed vanilla shader early enough. The working diagnostic reference contains a process-specific folder address, so it is unsuitable for distribution or reuse after a restart.

This investigation narrows the remaining problem to valid, stable early shader resolution. It does not establish that every possible standalone representation is impossible.

## Video and visible result

- [Download the new native-initialization diagnostic video](../artifacts/flag-standalone-20261021/native-initialization-diagnostic.mp4): 1920 x 1080, 12.000 seconds, 707 real captured frames, average **58.92 fps**. Capture requested 60 Hz; FFmpeg reports zero duplicated output frames and three dropped frames. No interpolation or synthetic cloth animation was used.
- [Two actual video frames showing different cloth positions](../artifacts/flag-standalone-20261021/diagnostic-motion-frames.jpg).
- [Native viewport screenshot](../artifacts/flag-standalone-20261021/native-initialization-control.webp).
- [Download the report, video, and selected evidence as a ZIP](../artifacts/flag-standalone-20261021/custom-flag-standalone-evidence.zip). This excludes pointer-bearing items, rejected maps, and the minidump.

The recording shows the **long flag only** in the editor playground. Its English caption explicitly says that the shader reference is session-specific and is not standalone or restart-safe. The diagnostic removes optional custom texture material, so the cloth is gray/metallic; this is animation and geometry evidence, not texture/skin parity.

At recording, `TMISFlagGpuPlacement`, `TMISFlagMaterialProbe`, and `TMISVanillaFlagCopy` are absent from the plugin registry. The read-only diagnostic had already been unloaded. The ordinary Openplanet/Editor++ test baseline remains installed, including an existing material/sprite crash-workaround plugin. This was not a completely unmodified or plugin-free game. A read-only package audit is retained in `baseline-material-patch-audit.json`; no existing package was changed.

For the earlier overview and close-ups of all three shapes, see [swallowtail, long, and round video](../artifacts/flag-round-video-20261021/custom-flags-swallowtail-long-round.mp4) and the [earlier video report](custom-flag-round-video-2026-10-21.md). Those three-shape results use temporary runtime initialization.

## What works and what does not

The counts below refer to five animated visual nodes in one model. A native load, a flag count, visible animation, and persistence are separate claims.

| Test | Native loading / initialization | Visible animation | Restart-safe / standalone |
| --- | --- | --- | --- |
| Packed vanilla flag mesh | Loads; 5/5 structured flags | Established vanilla behavior | Normal game asset |
| Earlier custom long, swallowtail, and round models with startup companion | 15/15 prepared across three models | All three recorded | Repeated in two cold processes **with companion**, not standalone |
| Earlier saved three-shape map after cold start without companion | Items/transforms survive; cloth initialization fails | Poles only in the cold screenshot | Fails the required rendering test |
| Five ordinary shader-path variants | All load as items; each 0/5 | Not placed as successes | No working candidate |
| Local extracted shader copy | Item loads; 0/5; explicit shader preload returns null | No success claimed | No working candidate |
| Session-folder reference to packed shader | 5/5 automatically set by native loading, repeated with a newly generated reference in a second process | Long cloth placed and recorded after diagnostic unload | **No**: file contains a current-process address |
| Address-free explicit embedded-only map, cold process, no loose test items or flag helpers | Crashes during map loading | No placed-state or image obtained | Rejected; quarantined |

The prior restart results and the companion's exact role are documented in [Custom flags after a full game restart](custom-flag-persistence-2026-10-21.md). The companion is a temporary local Openplanet script that prepares transient visual flags before the first draw. Saving the map does not save those flags.

## Why the shader reference is decisive

Earlier GPU captures established that the VertexTween vertex shader reads a structured vertex buffer at **VS t0**, indexed by **SV_VertexID**. Uploading custom positions to an ordinary input-assembler vertex buffer alone does not supply that data.

`CPlugVisual +0x24`, bit 27 (`0x08000000`), tells the engine to construct/bind the structured buffer. Native visual archive chunk `0900600F` does not serialize this transient bit. Encoding it as an ordinary saved visual flag therefore cannot preserve the working state across restarts.

Native Solid2 post-load checks animated visuals and their effective shader before later custom-material processing. If the correct shader is available then, it enables the structured-buffer route itself. The new control preserves the same custom geometry and complete inline native material/parent, changing only how the external shader reference reaches its folder. Native preload then reports **5/5** with no GPU/visual flag writes by the diagnostic. This supplies a positive control for the automatic native initialization route.

### Ordinary shader paths

`Tests/FlagProbe/FlagShaderPaths.cs` generates five one-variable path probes with the complete native parent material, shader `UseFile=true`, and optional custom material removed:

| Mode | Reference | Native result |
| --- | --- | --- |
| Relative0 | `GameData\Techno3\Media\Shader\Tech3_Warp_TDiffSpec_VertexTween.Shader.Gbx`, flags 0 | Loads; 0/5 |
| Absolute0 | Full Trackmania installation path, flags 0 | Loads; 0/5 |
| Absolute1 | Same installation path, flags 1 | Loads; 0/5 |
| GameAlias0 | `:game:\GameData\...`, flags 0 | Loads; 0/5 |
| GameDataAlias0 | `:GameData:\Techno3\...`, flags 0 | Loads; 0/5 |

The read-only native FID observations explain the failure: these strings resolve underneath `Documents\Trackmania\Items\TMIS_Standalone\`. Even the apparent absolute path and aliases become literal descendants of the custom item's folder. They do not select the packed game shader. Packed vanilla reports a shader FID with `<virtual>` as its filename representation and 5/5 structured flags.

The path matrix was checked in a fresh process (PID 5820), and ordinary failures repeated in the later control process. Receipts: `shader-paths/path-manifest.json`, `shader-paths-startup.log`, `compare-fids-fresh.log`, and `Openplanet-before-embedded-only-restart.log`.

### Fixed resource-table route

Native reference flag 4 selects a global fixed resource table. Static inspection and bounded read-only memory observations found 38 entries in this session; the actual packed flag shader FID was not among them. No random resource indices were loaded. This route currently supplies no proven stable reference to the required shader.

Receipts: `ref-resource.asm.txt`, `resources-global.json`, `resource-root.json`, and `resource-table.json`.

### Session-folder positive control

The native reference loader accepts folder index `-1` followed by an eight-byte folder pointer. `Tests/FlagProbe/standalone_file_controls.py` uses this only as a diagnostic: it patches the external reference table to select the actual packed shader's parent folder, preserving the shader filename and `UseFile=true`. It never writes process memory.

- PID 5820: folder `0x1F52D5439C8`, native structured initialization **5/5**.
- PID 26692: newly generated file using folder `0x2258D8636E8`, native initialization **5/5** again.

These are two independently generated session controls. They are **not the same file surviving a restart**. The address is meaningful only inside its originating process. The generator checks the live PID, refuses to overwrite an existing session output, and requires `--stage-session` to copy the pointer file to live Items. Pointer-bearing fixtures were archived before restarting.

Early placement attempts made no map changes because the new folder was not yet registered in the native inventory. The separate direct-placement probe also returned false with zero items. After a fresh startup registered a safe placeholder in that folder, the current-session diagnostic was generated and normal MCP `PlaceItem` succeeded, changing the disposable map from zero to one item. The item was read back at `[1480, 8, 1360]`, then observed in the editor playground. This is the placement used in the new video.

Receipts: `controls-fresh.log`, `file-controls/session-26692/manifest.json`, `Openplanet-before-embedded-only-restart.log`, `native-initialization-scene.json`, `recording-unassisted-state.json`, `diagnostic-video-info.json`, and `diagnostic-capture.log`.

### Local shader copy

The expected user-relative shader folder was populated with the actual extracted VertexTween shader and its DefRead/DefWrite shader files. The item still reports 0/5 structured flags, and explicitly preloading the local Shader.Gbx returns null.

The main shader has 16 external references, including HLSL and texture/shader dependencies. The available extracted HLSL placeholder is zero bytes. No fake HLSL or unrelated substitute was created. This rejects the tested local-copy arrangement; it does not prove that a complete, valid shader package could never work.

Receipts: `shader-tree.txt`, `controls-fresh.log`, `game-calls.jsonl`, and the archived `held-items/TMIS_FileControls/GameData/` tree.

## Embedded-only cold-load test and native crash

First, saving the diagnostic placement normally as `TMIS_Standalone_NativeMaterial_20261021.Map.Gbx` produced a map without an embedded ZIP. No reason for that omission has been established; it is not an embedding success.

To test embedding separately, a temporary generator explicitly inserted one **ordinary, address-free Relative0 item** into the ZIP of a copy of that map, matching its single item identity. The resulting `TMIS_Standalone_EmbeddedOnly_20261021.Map.Gbx` contains an item whose legacy filename includes `SessionFolderPointer`, but whose actual reference is:

```text
ancestor=0
GameData\Techno3\Media\Shader\Tech3_Warp_TDiffSpec_VertexTween.Shader.Gbx
flags=0 useFile=True
```

The embedded item is 577,144 bytes, SHA256 `5611EF321D4B2EB8E7F8A3D586006017340DE12E0C1338FF6B96A704F6AF499B`. It contains no session pointer. Its misleading legacy filename must not be used to infer its reference representation.

For the cold test, all dedicated flag helpers were removed from live Plugins and both new loose candidate folders were removed from Items. In fresh process PID 2032, opening the explicit embedded-only map crashed at 22:43:24 local time. No placement readback or visible result was obtained. The arrangement was not retried.

The native crash report and Windows events identify a concrete immediate cause:

```text
Exception: Trackmania.exe, 0xC0000005
Module offset: 0x3E7E50
Instruction at preferred address 0x1403E7E50:
    mov r8d, dword ptr [rcx + 0x238]
RCX = 0; attempted read = 0x238

Caller:
    0x14021EB13 call 0x14040F750  ; effective material shader resolver
    0x14021EB1D mov rcx, rax
    0x14021EB20 call 0x1403E7E50  ; no intervening null guard
```

The effective shader resolver returned null and an unchecked shader query dereferenced it. This establishes the immediate native failure, rather than attributing the crash to the unrelated BetterLoadingScreen deprecation at the end of the Openplanet log. It does not establish the validity of every other possible embedded shader/material arrangement.

Both experimental maps are quarantined under `artifacts/flag-standalone-20261021/rejected-embedded-control/`, marked **DO NOT OPEN IN THE GAME**, and absent from live My Maps. The rejected embedding dispatch was removed from active source; its exact used code is archived there. These maps and the session-address items are not working downloads and are excluded from the evidence download bundle.

Receipts: `embedded-only-manifest.json`, `embedded-only-references.txt`, `pre-cold-cleanup.json`, `Openplanet-cold-embedded-only-failed.log`, `cold-embedded-native-crash.txt`, `cold-embedded-windows-events.json`, `embedded-crash-function.asm.txt`, `embedded-crash-caller.asm.txt`, and `cold-embedded-rejection.json`. The original minidump remains local and is not bundled.

## Correction to earlier raw memory interpretation

Material device record `+8` contains a **CSystemFidFile**, not a shader node. Some earlier exploratory fields in `material-shader-memory.json`, including `shader84` and `passes168`, interpreted that pointer incorrectly and are not shader-field evidence. The corrected diagnostic logs its actual FID type and folder.

For the tested build, the material's device table is at `+0x38`, count at `+0x40`, record stride `0x38`; record `+8`, `+0x10`, and `+0x28` are shader FIDs, while `+0x30` is the cached resolved shader node. These raw layouts are research observations for this exact version, not supported authoring APIs.

## Remaining requirement and acceptance tests

A standalone solution must provide a **stable, address-free shader/material representation that resolves during native mesh post-load**, before the structured buffers are created. The positive control proves this route can initialize custom geometry. Neither a stable reference to the packed shader nor a complete valid local shader package has been established.

The remaining file-only research is therefore narrowly defined:

1. Find a supported serialized route from a user item to the packed shader, or an alternative native material representation that resolves it before the post-load check. Verify the resolved FID and effective shader first; do not resume blind path/index changes.
2. Alternatively obtain a complete, valid dependency graph for a local shader package, including real HLSL/compiled shader data as required by the native loader. The zero-byte extraction placeholder is insufficient. A loose dependency package would also need to be distinguished from a single-file solution.
3. For any new valid candidate, prove automatic initialization and animation in a disposable map; save it, then cold-test the identical file in at least two fresh processes with all dedicated helpers absent. Never count a regenerated process-address reference as persistence.
4. Test embedded-only loading with all loose model/dependency files absent. Only do this with a newly validated arrangement, not the quarantined null-shader fixture. Then verify all three shapes, close and distant views/LODs, textures/skins, map transitions, and another installation without test plugins before claiming vanilla parity or distributability.

Full lighting, collision, skin support, all distant LODs, other game versions, and multiplayer clients remain unproven. The new diagnostic cannot be promoted to a usable standalone item.

## Validation and final cleanup

Tested baseline: Trackmania `2026.2.2.1751`, engine `2026-02-02 17:51:19`; Openplanet `1.29.14` Next/Public `6fd4200f`.

The final C# probe build passed with zero warnings/errors. Python syntax validation passed without executing the session-fixture generator. `openplanet-lsp` reports zero diagnostics on the exact staged diagnostic script/manifest; fresh game logs show successful diagnostic loads, observed outputs, and intentional unload before the recording, with no attributable compilation failure. Full logs preserve unrelated baseline diagnostics.

`final-probe-identity.json` identifies the exact script and manifest. The initialization brief now documents the `Editor` dependency, separate gates, read-only comparisons, failed direct-placement branch, session-address limitation, and cleanup. These are temporary windowless DEV probes, not a published plugin or production authoring feature.

After quarantining the rejected map, the game was relaunched as PID 19960 and verified responding in **Menu**, with readiness clear, no tracked test items, and an empty agent tag. The three dedicated flag research plugins, their enable gates, and the new loose candidate folders are absent from live paths. The previously delivered three-shape map remains present.

Protected asset hashes are unchanged:

- `Editor.op`: `A07D1450FF06CA281BBFD5B1F44B7C08E647A2C13D472E1EFD7AC4E4036FA644`.
- Original `XertrovItems/ztest-flag3.Item.Gbx`: `3B14E7E9D8DDF237E8CA2D885031CC26D9D08BE1205E2FFBDF69F6C0DF1223FF`.

Final recovery receipts: `final-cleanup.json`, `final-recovery-bridge.json`, and `Openplanet-final-clean-recovery.log`. The standalone objective remains unresolved; the new deliverable is a verified native-initialization control and a substantially narrower file-loader failure diagnosis.
