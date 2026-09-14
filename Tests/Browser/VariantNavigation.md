# Variant navigation regression

```sh
npm ci --prefix Tests/Browser
npm --prefix Tests/Browser run test:variant-navigation
```

The runner publishes this checkout and serves it on an isolated local port.
The test uploads the approved SnowCar through the real UI, switches 2 → 3 → 2,
and measures until the real scene renderer finishes. It checks nine compact
buttons on one desktop row, no horizontal page overflow at 390 px, distinct
groups for multiple loaded files, and a real on-demand OBJ download containing
vertices and faces. It also checks retained keyboard focus and disambiguated
headings for two documents with the same filename. The default 5000 ms ceiling leaves headroom for loaded CI
machines; override it with `STUDIO_VARIANT_BUDGET_MS`. Timings are not a promise
for every device. The original dev build took approximately 15–16 seconds per
switch; the revised dev build measured approximately 2–3 seconds on the same host.

`STUDIO_VARIANT_EVIDENCE=/tmp/studio-variants` optionally captures desktop and
narrow screenshots after fonts load. Screenshots and downloads stay outside git.

The typed-viewer suite additionally verifies binary geometry decoding and rejects
malformed buffers without replacing the live geometry. Production transfers
little-endian float32 positions/normals and int32 indices as Blazor byte arrays,
avoiding numeric JSON serialization. Ordinary array-based viewer callers remain
supported. Bounds, normal validation and index validation use the decoded data.

Run `test:kinematic-preservation` as well: it checks real moving geometry, every
SnowCar variant, exact authored constraints, explicit edits, export and reopen.
Selection reuses its freshly built scene snapshot; edit callbacks rebuild it
instead of trusting a cross-selection cache. Neither optimization changes GBX
serialization or the authored source graph.
