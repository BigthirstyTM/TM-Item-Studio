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
