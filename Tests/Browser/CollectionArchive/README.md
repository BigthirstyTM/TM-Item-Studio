# Collection export regression

Run from the repository root:

```sh
npm ci --prefix Tests/Browser
npm --prefix Tests/Browser run test:collection-export
```

Requires .NET and Playwright Chromium (`npx playwright install chromium` from
`Tests/Browser` if needed). The runner publishes the current checkout and serves
only that build on an ephemeral localhost port. It does not use a running dev
server. Temporary files and the server are cleaned up on success or failure.

The browser uploads an item normally, optionally edits identity fields, clicks
Export, and reopens the downloaded file through the normal upload/viewer path.
This helper parses the downloaded bytes to distinguish numeric collection IDs
from string-defined IDs: both `new Id(26)` and `new Id("Stadium2020")` display as
`Stadium2020`, but only the former resolves to the expected native collection.

Before the fix, the no-op case failed with numeric `26` becoming string
`Stadium2020`. Explicit numeric editing also failed with string `26`. Tests cover
identity-only edits, unknown numeric IDs, literal strings (including numeric-looking
strings), empty IDs, deliberate edits, and the GBX lookback-marker boundary.

Inputs are temporary identity variations of the existing public bespoke
`animation-static-first.Item.Gbx`, plus both public reproductions already in
`Test Exported items`. They are not new native-validated fixture contributions.
These tests prove browser/archive behavior, not native placement or rendering.

Optionally set `STUDIO_COLLECTION_EXPORTS` to a new output directory to retain
the two repaired public reproductions for native testing. Only those two
downloads are retained; synthetic test variations are always cleaned up.
