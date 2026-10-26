# Vanilla FX carrier clone

`FxClone` makes an identity-only copy of an extracted vanilla fogger or
sparkler item. It deliberately leaves the variant entity, external prefab,
placement class, RefTable and all effect dependencies untouched. This is the
baseline fixture for native effect probes; it does not edit a particle value.

Example:

```powershell
dotnet run --project Tests/FxClone -- `
  --source 'C:\Users\PC\OpenplanetNext\Extract\GameData\Stadium\Items\ShowFogger8M.Item.Gbx' `
  --output 'artifacts\fx-clones\TMIS_Fogger8m_Clone.Item.Gbx' `
  --id 'TMIS_Fogger8m_Clone' `
  --name 'TMIS Fogger 8m Clone'
```

The command reparses the output and writes a neighbouring `.fxclone.json`
manifest. The manifest records the source/output hashes, root references,
variant count, whether an external prefab carrier survived, and the explicit
`nativeRuntimeVerdict: not-tested` marker.

The checked-in final fixtures are under `artifacts/fx-clones/final/`. Their
root chunk sequences match the vanilla fogger/sparkler sources; the native
loader result is recorded separately in
`docs/custom-fx-native-probe-2026-10-25.md`.

Use `--verify <clone.Item.Gbx>` to repeat the offline checks. Native loading,
inventory visibility, placement and visual FX behavior must still be tested in
Trackmania with a fresh log boundary and screenshot evidence.

The probe also has two opt-in reference experiments:

- `--rooted-game-graph` serializes the root table with ancestor level 3;
- `--game-fid-refs game|stadium|media` rewrites typed root NodeRefs and marks
  them as `UseFile` references.

These options are diagnostic fixtures only. They do not make a user item
portable; the native probe must be stopped if a game-process failure occurs.

For items whose body contains the currently unsupported `CPlugFxSystem`
(`0x0915C000`), `--opaque-preserve` performs a partial item parse with
`IgnoreExceptionsInBody` and `ReadRawBody`, edits only the item name,
description and identity, and verifies that the raw body bytes are preserved:

```powershell
dotnet run --project Tests/FxClone -- `
  --opaque-preserve `
  --source 'FogVolumetric1_FogEmissionRate_1000.Item.Gbx' `
  --output 'TMIS_FoggerEmbedded_Clone.Item.Gbx' `
  --id 'TMIS_EmbeddedFoggerClone' `
  --name 'TMIS Embedded Fogger Clone'
```

This mode is intended for embedded FX carriers and remains an offline/native-
preload fixture path. It does not parse or edit the particle payload itself;
each setting must still be changed in a separate, independently verified
fixture.
