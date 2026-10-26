# Waving flag converter verification

Date: 22 October 2026. Scope: the new Item Studio exporter, embedded-map repair,
shareable research assets and headless browser integration. Reports are in English.

## Result

The converter and saved-map packaging workflow are implemented and pass their
offline and published-browser checks. The three presets retain the exact
previously observed Item bytes. Newly converted SourceMesh geometry and new
browser-generated maps have **not** received a native game test. The game and
aircraft research remain paused; no game control was used for these checks.

Use [the technical guide](waving-flag-converter.md) for the material graph,
deformation algorithm, supported inputs, map packaging and kinematic limitation.
The separate [native evidence report](custom-flag-embedded-no-openplanet-2026-10-22.md)
documents the proven cold embedded maps and videos.

## Executed checks

| Check | Result |
| --- | --- |
| `dotnet run --project Tests/WavingFlagExport` | 32 checks passed |
| `dotnet run --project Tests/WavingFlagMapPackaging` | 32 checks passed |
| `dotnet run --project Tests/FlagConversion` | All 74 existing checks passed |
| `dotnet build TM-Item-Studio.csproj` | Succeeded; no errors |
| `npm --prefix Tests/Browser run test:waving-flag-converter` | Passed, including the existing browser workflow regression suite |

The Blazor build emits the native LZO/AOT configuration warning because native
references are not linked in this configuration. These workflows explicitly use
managed MiniLZO; real browser imports and downloads passed. The focused console
projects build without warnings.

The successful browser run is
[attempt-06](../artifacts/waving-flag-converter-20261022/attempt-06/published-workflow-validation.json),
with [the runner log](../artifacts/waving-flag-converter-20261022/attempt-06-runner.log).
It publishes an isolated app and serves that output to headless Chromium,
without using the user's browser session. Its application WASM SHA-256 is:

```text
D3A768F05D07A6BF5A78A20A3BC38F5F9753FD525CA97287AE5D8477019A379E
```

## What was verified

The export suite checks exact preset payloads, distinct preset filenames,
deterministic packages, source isolation, geometry budgets and refusal of
unsupported/malformed graphs. It checks every generated frame and LOD for
preserved topology/UVs, fixed attachment coordinates, closed loop endpoints,
valid normalized normals and conservative bounds. All four X/Z attachment
directions, missing-normal reconstruction and rigid source transforms are covered.

The map suite uses a tracked genuine native save, independent of ignored
research artifacts. Sequential repair of all three presets reproduces the
exact ingame-tested full map:

```text
F4899504479FEED65073C29D0DA7D16D0F3F703AE743B5A2706CA2E171C7869E
```

It checks raw header/body preservation, five road blocks, four anchors, the
native 8.060-second author time, script validation data and validation ghost.
It verifies idempotence and unchanged-input refusal for missing/conflicting
dependencies, ambiguous names, malformed archives, graph changes and maps
without a native Flag16m. Full normalized prefab matching rejects changes to
auxiliary cloth fields, static-pole parameters and alternate root representations.
A genuine SourceMesh export with one cloth entity and a synthetic native-save
reference layout also passes offline repair; this is not native saving evidence.

The browser suite performs real file uploads and downloads. It verifies all
three preset Item/dependency hashes, a supported SourceMesh export with chosen
wave controls, unsupported-source refusal, actual map download, downloadable
JSON audit, no-native refusal without download and no browser page errors.
The older selection, keyboard and classification-only export/reopen workflow
passes against the same published app.

## Cross-runtime map verification

The browser-generated map differs in whole-file bytes from the desktop-generated
single-flag repair. A separate desktop verifier confirms exact map header,
complete restoration of the original raw map body outside the ZIP splice,
all embedded GBX payloads and the untouched ZIP local/central records.
The browser output is also byte-identical after a desktop no-op repair.

Within the decoded map body, the differences are confined to rewritten/new
ZIP entries and their three enclosing length words: Deflate stream representation,
host-creator metadata (DOS versus Unix) and default attributes on newly created
dependency records. The outer LZO representation can also vary across runtimes.
Therefore the browser assertion uses an
independent layered binary audit rather than cross-runtime compressed-file
equality. This does not weaken the exact preservation checks for unchanged
map bytes, native-written Item headers/bodies or dependency payloads. The
desktop three-preset reproduction still matches the proven map hash above.

The successful run retains the downloaded map, JSON packaging audit, independent
preservation report, export manifests and headless screenshots under its evidence
folder. These are file/browser results, not new native rendering evidence.

## Remaining native acceptance work

The supplied proven embedded race map and baked rotating fixture can be tested
directly using their English handoff README. For new converted geometry, the
next native checks are loading/placement, visible deformation, all LODs and
culling distances, cold reload after packaging, and a second client with no
loose assets. Multiplayer delivery remains untested.

A genuine native Flag16m remains required by the demonstrated cold-start route.
The authoring ZIP contains one Item and three dependencies. The rotating proof
uses combined baked vertex animation; independently controlled physical
kinematics plus waving cloth and the aircraft integration remain unresolved.
