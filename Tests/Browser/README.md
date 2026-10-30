# Browser regression checks

Install with `npm ci --prefix Tests/Browser`. The repository uses Playwright
Chromium; install it with `npx --prefix Tests/Browser playwright install chromium`
if this machine does not already have it.

The new focused suites target an already-running Studio:

```powershell
dotnet run --project Tests/ItemIcons
dotnet build Tests/ProbeAssets
$env:STUDIO_BASE_URL = 'http://localhost:5183'
npm --prefix Tests/Browser run test:workflow-regressions
npm --prefix Tests/Browser run test:helper-coverage
npm --prefix Tests/Browser run test:icon-pipeline
```

The workflow suite covers real pointer deselection/composite source changes,
keyboard focus/NumLock guards, the four-segment ceiling, and missing-placement
flag export/reopen. Helper coverage exercises the reachable settings/actions at
several viewport widths. Icon coverage includes raw/WebP orientation, actual
drop, failed replacement atomicity, missing-header persistence, and export/reopen.

`test:native-fixtures` requires `STUDIO_NATIVE_EXPORTS` (an output folder).
`STUDIO_NATIVE_ID_PREFIX` selects distinct model IDs; `STUDIO_NATIVE_EXTENDED=1`
adds the quadrant icon and flag exports. It produces browser-generated files
and a SHA-256 manifest for manual/MCP native validation, without launching the
game. Native staging uses separate Items and Macroblocks folders. The approved
fixtures in `Fixtures/Approved/TM2020` must be present.

The older `run-*.cjs` commands publish/serve a snapshot themselves; consult each
runner for its port and optional arguments. Native parsing, inventory visibility,
placement, motion, and contact remain distinct acceptance levels. See
[the fix report](../../docs/audit-fixes-2026-10-20.md) for the tested matrix and gaps.

`npm --prefix Tests/Browser run test:waving-flag-converter` builds the bounded
offline source/map fixtures, publishes this checkout and serves only that output
on an OS-assigned localhost port. Headless Chromium performs real uploads and
downloads: all three preset ZIPs must retain the exact audited Item/dependency
bytes; supported source geometry produces an explicitly unverified animation
package; unsupported dynamic geometry is refused; a matching native-saved map
packages with exact decoded payloads and an independently audited raw map splice;
and a map without native Flag16m
is refused without a download. It also runs `workflow-regressions.cjs` against
the same publication, including the older classification-only flag workflow.
Set `STUDIO_WAVING_EVIDENCE` to a new output directory to preserve downloaded
packages and the English JSON validation report. The map fixture generator uses
the pinned native research map documented by `Tests/WavingFlagMapPackaging`;
these browser checks do not launch Trackmania or establish new native rendering.

For an embedded FX carrier extracted from a native map, run
`STUDIO_BASE_URL=http://127.0.0.1:5099 npm --prefix Tests/Browser run test:fx-visibility -- <Item.Gbx> <dependency-folder>`.
The check uploads the local dependency tree, opens the item, and verifies that
the Audio & effects panel reports the opaque `CPlugFxSystem` carrier and its
particle texture references.

For drawn motion paths, run `npm --prefix Tests/Browser run test:motion-path`
with `STUDIO_BASE_URL` pointing to a built studio. This checks real plane
clicks, exports nine kinematic routes, replacement/restore, rejection isolation
and static conversion/export/reopen. Set `STUDIO_NATIVE_EXPORTS` to preserve
exports elsewhere. It does not launch Trackmania; native observations and
limits are in `docs/kinematic-drawn-paths-2026-10-30.md`.

Run `npm --prefix Tests/Browser run test:follow-path-preview` with the same
`STUDIO_BASE_URL` for non-mutating eight-point playback, visible mesh midpoint
checks, pointer dragging and the native proof export. See
`docs/live-follow-path-2026-10-30.md` for the actual ingame video and file hashes.

The desktop and WASM ZIP writers can differ in their Deflate bytes and creator
OS metadata for newly written records. The independent console verifier checks
the exact map header, recovery of the original raw body outside the ZIP and
three length words, exact selected Item/dependency payloads, unrelated raw ZIP
records and byte-exact idempotency. It records concrete output hashes rather
than requiring compression streams from different runtimes to be identical.
