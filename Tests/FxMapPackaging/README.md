# FX map packaging tests

Run `dotnet run --project Tests/FxMapPackaging/FxMapPackaging.csproj`.
This is an offline, self-contained test suite. It performs no game, inventory,
plugin, map staging or native editor actions.

For a concrete offline packaging output, use:

```powershell
dotnet run --project Tests/FxMapPackaging/FxMapPackaging.csproj -- repair <saved-map> <exact-original-item> <Items-relative-path> <unused-output.Map.Gbx>
```

The command refuses existing output/report files, leaves both input files
unchanged, and writes a `.packaging-report.json` beside the resulting map.
Omit command arguments to run the regular self-contained tests.

`Fixtures/NativeSaveAnyway.Map.Gbx` is the exact native editor Save Anyway
result SHA-256 `633F22C8DB7376EE859D523CFC2F6B82C3BA265BBF3C5EAB84181929E66812F0`.
It has one Sparkler anchor and an empty embedded-files chunk, with no start
block. It is packaging evidence, not a playable demonstration map.

The resolved source Sparkler SHA-256 is
`3C1F8FA89642AD33E918BAD8AC323CA8CE0A3EB2A40204B767C8AC8541863B44`.
The additional custom-mesh Sparkler and Fogger fixtures exercise supported
builtin texture FID strings and both null-ID and full-ID carriers. Accepting
and packaging a Fogger does not establish that native smoke renders.

The tests cover the exact raw chunk054 splice, scoped metadata lookbacks,
empty ZIP bootstrap, existing FX-less item recovery, unchanged unrelated ZIP
local records/central metadata, idempotency, source identity/author matching,
anchor matching, texture whitelist and malformed/unsafe input rejection.
Typed map serialization is used only to construct specific test inputs; the
production API preserves every original map byte outside chunk054.

The supported embedded-files chunk is version 1. Version 0 is explicitly
refused. Existing `club:` metadata identities and club anchors are preserved
in their own engine namespace; they cannot be selected as local FX paths or
injected as ZIP entry names.

**Package again after every native editor save.** Packaging does not claim
to repair the native editor's own embedding behavior.
