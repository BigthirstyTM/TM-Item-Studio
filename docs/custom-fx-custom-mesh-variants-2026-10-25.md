# Custom FX mesh-variant follow-up

**Date:** 25 October 2026  
**Scope:** separate mesh authoring and the opaque FX carrier gate.

The native embedded Sparkler and Fogger carriers are now exportable and
placeable with their `CPlugFxSystem` body preserved. The body is still an
unknown GBX.NET class, so replacing its visual entity with a typed custom mesh
would require rewriting the carrier graph. The native clone files named
`TMIS_Custom_Fogger_Pyramid` and `TMIS_Custom_Sparkler_Diamond` therefore keep
the original carrier body; their pyramid/diamond names describe the requested
emission-point presets, not a verified mesh replacement.

Two real typed mesh shells were generated separately as a safe fallback:

- [`TMIS_FX_PyramidShell.Item.Gbx`](../artifacts/fx-clones/fx-mesh-shells/TMIS_FX_PyramidShell.Item.Gbx)
- [`TMIS_FX_DiamondShell.Item.Gbx`](../artifacts/fx-clones/fx-mesh-shells/TMIS_FX_DiamondShell.Item.Gbx)

They are ordinary `Ornament` items with 5 and 6 vertices respectively. The
offline generator and reparse check is `Tests/FxMeshShells`; the manifest is
[`fx-mesh-shells-manifest.json`](../artifacts/fx-clones/fx-mesh-shells/fx-mesh-shells-manifest.json).
`Tests/FxClone --verify` passes for both. They can be placed beside an embedded
FX carrier in a macroblock or test map to demonstrate distinct housing shapes,
but they are separate map objects and do not make the FX carrier standalone.

The supported standalone deliverable remains the embedded carrier plus its
`.fxsettings.json` sidecar. The sidecar records the editable local emission
point while `nativeOffsetWritten=false`; the opaque emitter offset is preserved
and not silently rewritten.

## Follow-up native offset result

A separate byte-preserving probe now edits the native emitter expression in the
already loadable Save-As carriers. `LocalOffsetExpr=float3(0)` was changed to
`float3(1)` without changing container size or the reference table. Both
Sparkler and Fogger probes passed native preload; the Fogger probe additionally
placed, saved and reopened. This does not make the typed Pyramid/Diamond shells
part of the carrier. The shells remain separate Studio-rendered companion
items until a native prefab mesh replacement passes the preload gate.

## Native mesh-candidate gate

The generator now preserves the original 15-node table while replacing prefab
entity 0 with a typed `CPlugSolid2Model` mesh. The resulting Pyramid and Diamond
files reparse cleanly and both pass native `Fids::Preload` as `CGameItemModel`.
The first Fogger placement call returned `false`, so native placement/rendering
of the embedded replacement mesh remains open. The files are kept as native
preload candidates in `artifacts/fx-clones/fx-native-patched-20261025/`; the
separate typed shells remain the safer Studio visual fallback.
