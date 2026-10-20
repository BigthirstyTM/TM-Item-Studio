# Audit fix evidence

This is a curated subset of the local `artifacts/fixes-2026-10-20` evidence.
Text is normalized to UTF-8/LF and JSON is formatted for review; original tool
output remains in the local artifacts. Game assets, published application
output, full plugin inventories, and upstream source clones are excluded.

Model/browser logs record actual completed checks. Native JSON must be read
through its nested result/output rather than treating the outer `ok` as a pass.
`mcp-final-reopened-items.json` is the final nine-instance saved-map readback.
The two PNGs show different extension phases from a fixed native playtest
camera; the car did not contact the pusher during those observations.

The crash signature excerpt belongs to the fresh 20 October 16:34 crash.
The unrelated January crash file is excluded. `isolated-export-trial.log`
records the later successful native Back action with EditorDeveloper absent.
`final-restore-fresh.log` and `final-session-parity.json` prove restoration.

See [the report](../../audit-fixes-2026-10-20.md) for supported conclusions,
unverified native behaviors, runtime versions, and reproduction commands.
