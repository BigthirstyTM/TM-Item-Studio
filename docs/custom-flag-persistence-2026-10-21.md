# Custom flags after a full game restart

21 October 2026. Report language: English.

The swallowtail, long, and round cloth all render after a full restart when a fixed startup companion prepares their visual nodes before the saved map opens. This passed in two separate game processes. A file-only solution is still unproven: the same saved map shows only the poles after a cold start without assistance.

## Downloads and visual evidence

- [Main video: overview, then zoomed close-ups of all three shapes](../artifacts/flag-round-video-20261021/custom-flags-swallowtail-long-round.mp4). 1080p, 48.5 seconds, 2,797 real captured frames, average 57.67 fps.
- [Additional recording after a full restart](../artifacts/flag-round-video-20261021/persistence/custom-flags-after-restart.mp4). 1080p, 19.6 seconds, 936 real captured frames, average 47.76 fps. Its caption explicitly identifies the startup companion dependency. Capture requested 60 Hz; the recorder reports zero duplicated output frames and 67 dropped frames. No interpolation or synthetic cloth animation was used.
- [Video milestone report](custom-flag-round-video-2026-10-21.md).
- [Research models and saved map](../artifacts/flag-round-video-20261021/custom-flags-research-models.zip). These are experimental assets requiring runtime assistance, not finished standalone items. Rejected loader fixtures are excluded.

Without assistance after restart:

![Saved map after a cold restart without the helper: three poles, no cloth](../artifacts/flag-round-video-20261021/persistence/cold-no-helper-overview.webp)

With startup assistance after restart:

![Same saved map after a cold restart with the startup companion: all three custom cloth shapes](../artifacts/flag-round-video-20261021/persistence/cold-startup-helper-overview.webp)

## What was verified

| Test | Observation | Supported conclusion |
| --- | --- | --- |
| Saved three-item map; cold process; helper unloaded and both enable gates absent | All three item paths and transforms are present; native screenshots show only poles | Geometry embedding and map placement survive; correct cloth rendering does not |
| Cold process PID 27656; companion automatically starts; reopen the saved map | All three shapes visible; actual cloth animation recorded | Automatic startup preparation can restore the saved map without recreating its items |
| Second cold process PID 3724; same startup preparation and saved map | All three shapes visible again, including close-ups | The companion result repeated across full process restarts |
| Read-only observation of the saved map in both assisted sessions | `visuals=15 enabled=15 identical-to-preload=15` | All five visuals of each map item are the same nodes prepared at startup |
| Gate removal in the menu, then intentional helper unload | Fresh log: `RESTORED transient bits 15`, followed by unload | The temporary mutation and plugin were cleaned up |

The saved map is `TMIS_RoundFlags_Video_20261021.Map.Gbx`. It contains the three embedded models at the original video positions. The cold tests reopen this saved map; they do not regenerate geometry, replace placements, reapply a D3D hook, or manually reload the startup helper after the process starts. The loose model files remain available during the assisted tests because the companion preloads those exact files. Embedded-only distribution without loose files has **not** been demonstrated.

The main video was delivered in chat with its English report before this persistence investigation resumed, as requested.

## Why initialization still matters

The earlier GPU investigation demonstrated that the VertexTween vertex shader reads a structured vertex buffer at VS `t0`, indexed with `SV_VertexID`. Sending the custom positions through an ordinary input-assembler vertex buffer is insufficient. Setting `CPlugVisual +0x24` bit 27 (`0x08000000`) before the first draw lets the engine create and bind the required structured buffer itself.

The native visual archive does not serialize this transient bit. Saving an item or embedding it into a map therefore does not preserve this runtime initialization. The cold unassisted screenshots reproduce that failure for all three shapes, including the new round cloth.

Native Solid2 post-load code checks the visual's frame count and the resolved material/shader, then calls the structured-buffer setter when the shader requires it. That check occurs before later custom-material processing. This identifies the condition a file-only fix must satisfy; it does not yet prove which material-resolution or initialization detail prevents it in every custom-file arrangement.

## New material and reference tests

Fresh extraction showed that the older research copies differ from this installed game's assets. The current ItemFlag material adds a color-target-table reference. The current shader has chunk `09002020` version 3 rather than version 2 and additional default texture references. Fresh copies and before/after dumps were preserved. A local HLSL extraction placeholder is zero bytes, so it cannot support a legitimate complete local shader package.

| Candidate family | Native result | Limit |
| --- | --- | --- |
| Bare inline material, native material with no shader, inline parent with shader fields null | Loads as `CGameItemModel` | Loader controls only; never placed |
| Local external empty parent with material-reference `UseFile=false` | Loads | Confirms at least one relative external material reference works; no shader or render proof |
| Complete current inline material/parent/shader candidates from the isolation set | Preload returns null | Parser success does not establish native compatibility |
| Absolute game-path material and parent-material references | Preload returns null in a clean process without the GPU helper | Absolute paths do not solve these tested arrangements |
| Inline native parent with external shader reference using vanilla's `UseFile=true`, relative or absolute path | Both load | Correcting the shader reference improves loading, but read-only inspection finds the structured bit clear on all five visuals of each item |

The reference type matters: native shader-file references use `UseFile=true`, while the parent-material reference uses `false`. This is not a global toggle to apply to every reference. The accepted shader-reference candidates were inspected without setting their visual flags. They were not promoted to a rendering success or placed as shader-free controls.

The probe log labels the `+0xD0` count as `material-insts`; retain the raw log as captured. This is the native material-reference array count, not evidence that a user material instance has successfully resolved a compiled shader.

Three rejected experiments terminated the game during preload: removing native shader passes (19:52:21), changing the local empty-parent material reference to `UseFile=true` (20:01:20), and applying that setting to the native parent-material reference (20:02:31). Their exact files/logs were preserved outside live Items. None was placed, and none was retried. The generators no longer generate those rejected modes. No new native crash report was found for these exits; the Openplanet log endings and recorded requests establish the trigger boundary, not a native faulting instruction.

## What is needed for usable custom flags

There are two distinct engineering paths:

1. **Standalone file path:** make a valid material/shader representation available during normal mesh post-load so the engine sets the structured-buffer state automatically. The next focused probe should trace the effective material/shader resolution for the accepted shader-file-reference candidate against the packed vanilla mesh, especially the shader selected at the post-load check. Preserve complete native shader passes and distinguish a file reference from a loaded compiled shader. Any proposed fix must then pass a cold test with no helper and no loose dependency files hidden by caches.
2. **Companion path:** initialize eligible models before their first render on each session/map load. The fixed three-file prototype proves this can work across restarts here. A usable companion still needs supported lifecycle integration, a robust compatibility guard, model discovery, embedded-only loading support, bounded ownership, and mode/map-transition validation. The present DEV probe deliberately expires after ten minutes and restores its changes; it is not a deployable general-purpose plugin.

Neither path currently establishes full vanilla parity for distant LODs, lighting, collision, arbitrary skins, other machines/game versions, or multiplayer clients without the companion. The round cloth visibly deforms from its circular rest outline as its retained animation plays.

## Exact bytes, diagnostics, review, and cleanup

Tested: Trackmania `2026.2.2.1751`, engine `2026-02-02 17:51:19`; Openplanet `1.29.14` Next/Public `6fd4200f`.

The startup helper and read-only reference probe were checked with `openplanet-lsp` on their exact staged bytes, with zero final diagnostics. Fresh logs show successful loads and their observed outputs, with no later attributable compilation failure. The bridge's embedded log-tail response reported a permission error; the readable host-side Openplanet log supplies the lifecycle evidence. Unrelated existing plugin warnings/errors remain in the preserved full startup logs.

These are windowless engine-only probes: a synthetic deterministic test or visual demo window would not prove native GPU initialization. The replacement evidence is the cold-process matrix, read-only node identity/flag observation, real viewport captures, and cleanup receipts. There is no new network/eval bridge, no Editor++ rebuild, and no runtime GPU binding hook in these tests.

Review boundary: the fixed helper owns only bits it adds, retains affected visuals, and restores them on gate removal, timeout, disable, destruction, or partial failure. Its ten-minute expiry while a map is open is an intentional research limitation. General model discovery, embedded-only loading, arbitrary map transitions, and compatibility after a game update remain untested and must not be inferred from the successful fixed-fixture tests.

Both helper plugins were intentionally unloaded with fresh unload logs and archived outside the live plugin directory. Their gates and loose research models were removed from live Items by archiving them. The saved test map and evidence remain available. The agent tag is cleared, and the game is left in the menu. The original XertroV item and supplied `Editor.op` hashes are unchanged.

The principal receipts are under `artifacts/flag-round-video-20261021/persistence/`: `baseline-helper-unload.log`, `baseline-cold-map.json`, `Openplanet-startup-test.log`, `cold-startup-second-Openplanet.log`, the two process identity JSON files, `startup-staged-identity.json`, `startup-lsp.json`, `startup-helper-clean-unload.log`, `reference-native-clean-loads.json`, `reference-inspect-fresh.log`, `reference-inspect-staged.json`, `reference-inspect-lsp.json`, and `cleanup.json`. The raw request/response history is in the parent directory's `game-calls.jsonl`.

---

Next: trace the accepted candidate's effective shader at native mesh post-load; require a helper-free cold-render test before calling any item standalone.
