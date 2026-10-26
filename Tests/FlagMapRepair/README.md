# Known custom flag map export repair

This offline research utility repairs the embedded references produced when Trackmania saves the three TMIS material-remap flags: Swallowtail, Long250 and Round. It accepts only their exact pinned native-written raw bodies. It is not a general custom-item repair tool.

The map must already contain a genuine native Stadium `Flag16m` item by `Nadeo`. Cold tests currently require that native flag to initialize the flag rendering resources. This utility verifies its presence and does not insert it.

## Requirements

- .NET 8 SDK.
- The existing `Tests/FlagProbe/bin/Debug/net8.0` GBX.NET dependency binaries.
- The original three dependency files from `artifacts/flag-selfcontained-20261022/material-remap-v6-audited`, or an explicit folder containing those exact files. Their SHA-256 hashes are pinned in `Program.cs`.
- A native-saved map containing one or more of the three supported flag assets and a native flag.

## Repair a native export

Run from the repository root:

```powershell
dotnet run --project Tests/FlagMapRepair -- --repair `
  'C:\path\native-saved.Map.Gbx' `
  'C:\path\new-repaired.Map.Gbx'
```

If the original dependency package is elsewhere:

```powershell
dotnet run --project Tests/FlagMapRepair -- --repair `
  'C:\path\native-saved.Map.Gbx' `
  'C:\path\new-repaired.Map.Gbx' `
  --dependencies 'C:\path\original-v6-package'
```

The source is never overwritten. An existing output or adjacent `.manifest.json` is refused. Success returns exit code `0` and writes the new map plus an adjacent manifest; refusal returns `2` with the reason on standard error.

The manifest records the exact input/output and embedded-file hashes, detected native flag count, repaired reference count, restored dependency count and preservation checks. The `outputSha256` and `embeddedFiles[].zipPath` fields are compatible with the existing cold-load evidence scripts.

## What changes

Only the recognized Items' external reference tables are changed to sibling references. The three exact original Material, GameSkin and TerrainModifier files are restored in the same embedded folder when absent. A conflicting dependency is refused.

The Item headers, node counts and raw bodies remain byte-exact. The map header, map reference table, all unknown body bytes, validation metadata, roads, anchors, expected embedded identities and unrelated ZIP records remain unchanged, apart from the embedded ZIP and its three enclosing length words. The source compression mode is retained. No map, Item or dependency node is reserialized.

## Game verification is still required

Preserving the serialized validation data does not establish that the game accepts the map as validated after the embedded content changes. For each final map, open and play the exact repaired output, then cold-load it with Openplanet absent and local copies of the custom files unavailable. Record the output hash with that evidence.

Trackmania may rewrite references or omit dependencies on a later save. Run the repair again after each native save, always to a new output path, and test that final output. The utility alone proves neither runtime animation nor compatibility with a second player's client.

## Offline verification

```powershell
dotnet build Tests/FlagMapRepair --nologo
dotnet run --project Tests/FlagMapRepair/Verification -- `
  . 'artifacts/flag-selfcontained-20261022/export-repair-utility-check/new-verification-run'
```

The verification directory must not already exist. The controlled fixture uses the existing five-road map and exact native-written flag Items, omits all three dependencies and adds an unrelated ZIP entry with extra fields, attributes and entry/archive comments. Checks cover restoration, byte preservation, repeat-repair idempotence, safe refusals, and malformed ZIP guards. These are offline checks; the generated fixture is not evidence of native validation or playability.
