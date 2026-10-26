# Native probe: vanilla FX carriers and first clones

**Datum:** 25 oktober 2026
**Scope:** native baseline for `ShowFogger*` and `Sparkler*`, followed by the identity-only `FxClone` fixtures.

## Baseline that passed

The local Trackmania/Openplanet MCP stack was healthy (`tm-control-mcp` 0.4.0, E++ pack enabled, editor ready). The inventory contained:

- `ShowFogger8m`, `ShowFogger16m` and both with-light variants;
- `Sparkler8m` and `Sparkler16m`;
- the existing custom `XertrovItems\Fogger_Animated.Item.Gbx`.

Five vanilla items were placed in a disposable map with tag `tmis:fx-baseline-20261025`:

```text
ShowFogger8m          (160, 8, 160)
ShowFogger16m         (224, 8, 160)
ShowFoggerWithLight8m (288, 8, 160)
Sparkler8m            (352, 8, 160)
Sparkler16m           (416, 8, 160)
```

The map was saved as:

```text
C:\Users\PC\Documents\Trackmania\Maps\TMIS_FX_Baseline_20261025.Map.Gbx
```

After closing and reopening the map, the native editor reported all five objects with the same IDs and positions. `Tests/ProbeAssets` also inspected the native-saved file with `--skip-lightmap-cache` and found five anchored objects. The saved map is therefore placement/readback evidence, not a claim that the particle output was visually proven.

The editor screenshots showed the small carrier meshes. No unambiguous smoke or spark plume was visible in the tested editor viewport. A play-context FX capture still needs a map with a valid start and a bounded camera observation.

## Offline clone fixtures

`Tests/FxClone` produced these identity-only clones:

```text
artifacts/fx-clones/final/TMIS_Fogger8m_Clone.Item.Gbx
artifacts/fx-clones/final/TMIS_Sparkler8m_Clone.Item.Gbx
```

The offline verifier passed for both files. Each remained an `Ornament` with one
`NPlugItem_SVariantList` variant, two root references and the original external
prefab carrier. A fresh `ItemChunkIndex` run found the same root chunk sequence
as the corresponding vanilla source (26 chunks for the fogger, 27 for the
sparkler); the earlier pre-fix artifacts with an extra legacy chunk are not the
final fixtures. The neighbouring `.fxclone.json` manifests deliberately retain
`nativeRuntimeVerdict: not-tested`.

## Native clone gate that did not pass

The clone files were copied into unique user Items folders. After a cold game
restart, the inventory indexed the identity-only clones and the native picker
could find their paths, but their collectors stayed empty. `PlaceItem` still
returned `item not found`. The same result held for clones that preserved the
source author/name metadata, same-length byte-patched identities, and an
inline/direct-prefab diagnostic with the FX carrier stripped.

A direct FID probe then called `Fids::GetUser` followed by `Fids::Preload` for
each staged variant. `GetUser` resolved every file, but native preload returned
`null` for the identity-only, metadata-preserving and raw-patched clones. The
official controls passed through the game drive in the same session:

```text
game:GameData/Stadium/Items/ShowFogger8M.Item.Gbx  -> CGameItemModel
game:GameData/Stadium/Items/Sparkler8m.Item.Gbx    -> CGameItemModel
user:.../TMIS_Fogger8m_Clone.Item.Gbx              -> Preload returned null
user:.../TMIS_Sparkler8m_Clone.Item.Gbx             -> Preload returned null
```

This separates catalog indexing from engine loading and shows that the failure
is not explained by the identity string or by GBX.NET's changed file length.
The external `CPlugPrefab`/`CPlugFxSystem` graph is the remaining gate. A
package-layout experiment staged the graph under user `Media`, `MotionParticleEmitterModel`,
`Stadium` and `Techno3` roots, but its prefab and FxSys FIDs also returned
`null`. A final explicit `UseFile`/`GameData/Stadium` root-reference variant
caused the Trackmania process to disappear; it was preserved as a failed
fixture and was not retried.

An additional probe loaded the existing `XertrovItems\Fogger_Animated.Item.Gbx` control. It logged one successful preload and the Trackmania process then disappeared before the remaining item checks completed. The probe was stopped and was not retried. The game was restarted successfully and returned to the menu; all temporary staged files and the live probe plugin were removed.

## Embedded FX control that does load

The external graph failure is distinct from the embedded form. Three existing
FogVolumetric items contain an embedded `CPlugFxSystem` and differ in the
single `FogEmissionRate` fixture value. Filename-only copies were staged under
`Items/TMIS_FX_EmbeddedRateProbe_20261025/` and preloaded through
`ControlFids` after a user-drive `updateTree`:

| Fixture | Native result |
|---|---|
| `TMIS_FoggerEmbedded_Rate0.Item.Gbx` | `CGameItemModel`, loaded |
| `TMIS_FoggerEmbedded_Rate100.Item.Gbx` | `CGameItemModel`, loaded |
| `TMIS_FoggerEmbedded_Rate1000.Item.Gbx` | `CGameItemModel`, loaded |

The exact Fids responses, class IDs and source hashes are recorded in
[`embedded-rate-preload-20261025.json`](../artifacts/fx-clones/embedded-rate-preload-20261025.json).
This is native preload evidence for an embedded FX carrier and the
`FogEmissionRate` control values; it is not yet inventory, placement or visual
play evidence. It gives the next probe a safe baseline: preserve the embedded
FX payload and change one known rate at a time before attempting a full native
fogger/sparkler external-graph clone.

The per-rate native attempt, including the successful rate-100 Save As baseline
and the rate-0 item-editor timeout, is recorded in
[`native-rate-probe-20261025.json`](../artifacts/fx-clones/native-rate-probe-20261025.json).

The rate-0 fixture was then tested from a fresh game session without opening
the item editor. `PlaceItem` resolved the exact `CGameItemModel` at `(640, 8,
766)`, `SaveMapFlow` wrote
`Maps/MCP/TMIS_FX_Rate0_Native_Save_20261025.Map.Gbx`, and reopening that map
returned the same ID and position. `MapInspect` found both the original
Sparkler anchor and the rate-0 FogVolumetric anchor. The report and copied map
are [`native-rate0-save-reopen-20261025.json`](../artifacts/fx-clones/native-rate0-save-reopen-20261025.json)
and [`TMIS_FX_Rate0_Native_Save_20261025.Map.Gbx`](../artifacts/fx-clones/TMIS_FX_Rate0_Native_Save_20261025.Map.Gbx).
The editor viewport still did not show an unambiguous plume, so this proves
native placement and persistence, not visible particle output.

The separate rate-1000 placement attempt is preserved as a negative runtime
result: the call began and Trackmania disappeared before completion. It was not
retried after restart. This is evidence of an unsafe probe route, not proof
that the rate-1000 payload is invalid; its standalone `Fids::Preload` remains
successful. See [`native-rate-placement-crash-20261025.json`](../artifacts/fx-clones/native-rate-placement-crash-20261025.json).

As a separate cleanup-scoped control, the original rate-1000 source was also
placed through E++ by its inventory path and read back by `GetItems` as a
`CGameItemModel` at `(480, 8, 160)`. The tagged object was removed immediately;
no screenshot proved the particle output, so this does not upgrade the result
to visual or playable evidence.

## Native Save Item As breakthrough: embedded carriers

The safe native route is to open an embedded carrier in Editor++ and let the
game's own `Save Item As` writer create the clone. This succeeded for both
carriers:

```text
Items/TMIS_FX_NativeCopy/TMIS_FoggerEmbeddedNativeClone.Item.Gbx   (8834 bytes)
Items/TMIS_FX_NativeCopy/TMIS_SparklerEmbeddedNativeClone.Item.Gbx (2298 bytes)
```

Both files then passed `ControlFids` preload as `CGameItemModel` and were
placed with the DEV `Editor::PlaceItems` bridge at `(780, 8, 766)`. The native
map saves were reopened and `GetItems` read back the same custom IDs and
positions:

```text
Maps/MCP/TMIS_EmbeddedNativeClone_Save_20261025.Map.Gbx
Maps/MCP/TMIS_SparklerEmbeddedNativeClone_Save_20261025.Map.Gbx
```

The per-run hashes, preload results, placement records, screenshots and
reopen readback are in
[`native-embedded-clone-20261025.json`](../artifacts/fx-clones/native-embedded-clone-20261025.json).
The editor screenshots show the carrier geometry (a white cube for the
Sparkler sample) but no unambiguous particle plume. The result is therefore
**parsed + native-preloaded + placed + native-saved + reopened/read back**;
particle visibility and playable output remain unproven.

This also explains the earlier clone gate. An offline GBX.NET identity clone
that merely changes `Ident` is not enough for placement; `PlaceItems` returned
`false` even though `Fids::Preload` returned a model. The native writer emits a
larger, engine-compatible item file whose custom path is accepted by the
editor. The old external `ShowFogger`/`Sparkler` prefab graph still fails the
native preload gate; the breakthrough applies to the embedded carrier form.

## Supplied Sparkler map and save-loss reproduction

The supplied `Codex_Sparkler_Paket_V2.Map.Gbx` contains an embedded Sparkler
item with inline FX and local texture references. It loads with the visible
effect in the editor. A normal native save produces
`Codex_Sparkler_Paket_V2_Saved.Map.Gbx`; its embedded ZIP shrinks from roughly
93 KB to roughly 2 KB, and the texture entries disappear. The effect then
disappears after reopen. This reproduces the reported failure and is separate
from the working external user-item clone route above.

The attached file has a more specific explanation. The original embedded item
uses relative texture references at RefTable ancestor level 0. Native save
rewrites them to `ContentLoaded/Items/...` (ancestor level 4), but the first
saved map contains no matching dependency entries. The item therefore remains
in `GetItems` while its particle textures are unresolved. The repaired copy
restores the four exact texture/GBX entries under `ContentLoaded/Items`; the
inventory and map readback are valid, but a play-context visual check still
needs a map with a valid start. The machine-readable comparison is
[`attached-sparkler-map-analysis-20261025.json`](../artifacts/fx-clones/attached-sparkler-map-analysis-20261025.json).

The Item Studio now exposes this unsupported body instead of dropping it from
the inspector. The Audio & effects panel reports `Particle system / CPlugFx =
Present`, shows `CPlugFxSystem (embedded CPlugFxSystem 0x0915C000)` as opaque,
and lists the two Sparkler texture dependencies. The browser check and visual
evidence are [`fx-visibility.cjs`](../Tests/Browser/fx-visibility.cjs) and
[`fx-visibility.png`](../artifacts/fx-clones/fx-visibility.png).

## Current conclusion

The official carrier is proven to be inventory-visible, placeable, saved and
readable in a native map. The embedded fogger and Sparkler route is now also
proven through native Save As, preload, placement and save/reopen. The external
vanilla prefab graph remains blocked. `FogEmissionRate` has native preload
evidence for 0, 100 and 1000; rate 0 now also has a successful fresh-session
placement and map save/reopen. Rate 1000 placement crashed before completion
and is intentionally not retried. No rate has yet produced an unambiguous
visual/play screenshot, so lifetime/spawn/render changes should only be tested
from the native-saved embedded carrier with the same per-fixture evidence.

## Native emitter-offset isolation probe

The FX schema was recovered far enough to identify the actual emitter position field: `CPlugFxSystemNode_ParticleEmitter.LocalOffsetExpr`. The two native Editor++ Save-As carriers contain the same serialized nine-byte expression, `float3(0)`. `Tests/FxNativePatch/raw_patch.py` makes a length-preserving probe by replacing it with `float3(1)` while leaving the rest of each GBX container and its reference table unchanged.

Both probes passed native `Fids::Preload` as `CGameItemModel`:

```text
TMIS_Sparkler_NativeOffsetProbe.Item.Gbx  -> PRELOAD=ok
TMIS_Fogger_NativeOffsetProbe.Item.Gbx    -> FOG_PRELOAD=ok
```

The fogger probe also placed successfully and survived a native map save and reopen at `(680, 8, 766)`. The Sparkler probe preloaded successfully, but its single placement call returned `false`; it is therefore not promoted to a placement/persistence pass. The probe artifacts and machine-readable manifest are in [`artifacts/fx-clones/fx-native-offset-probe-20261025/`](../artifacts/fx-clones/fx-native-offset-probe-20261025/).

This upgrades the emitter-point result from “sidecar only” to a proven native field edit for the native-saved carrier route. The normal GBX.NET Studio export still preserves unsupported FX bodies and writes the arbitrary X/Y/Z control to `.fxsettings.json`; it does not yet rewrite arbitrary-length expressions in the browser export path. The native probe is intentionally separate and currently supports the same-length expression test.
