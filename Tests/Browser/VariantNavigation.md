# Variant navigation regression

```sh
npm ci --prefix Tests/Browser
npm --prefix Tests/Browser run test:variant-navigation
```

The runner publishes this checkout and serves it on an isolated local port.
The test uploads the approved SnowCar through the real UI, switches 2 → 3 → 2 → 1 → 3,
and measures until the viewer has rendered and the variant controls are ready again. It checks nine compact
buttons on one desktop row, no horizontal page overflow at 390 px, distinct
groups for multiple loaded files, and a real on-demand OBJ download containing
vertices and faces. It also checks retained keyboard focus and disambiguated
headings for two documents with the same filename. The default 5000 ms ceiling leaves headroom for loaded CI
machines; override it with `STUDIO_VARIANT_BUDGET_MS`. Timings are not a promise
for every device. The original dev build took approximately 15–16 seconds per
switch. The first optimization reduced this to approximately 2–3 seconds; shared
geometry further reduces repeated selections. Cold geometry/transform visits and
warm visits are reported separately by the ordered timing samples.

`STUDIO_VARIANT_EVIDENCE=/tmp/studio-variants` optionally captures desktop and
narrow screenshots after fonts load. Screenshots and downloads stay outside git.

The same runner checks the compact workspace: filenames beside the title,
variants inside the viewport toolbar, a viewport and inspector that use the
available desktop height, and a stacked narrow layout. Sixteen long-named files
exercise scrollable variants and a bounded, wrapping export footer. Set
`STUDIO_LAYOUT_EVIDENCE=/tmp/studio-layout` to capture these layouts.

The typed-viewer suite additionally verifies binary geometry decoding and rejects
malformed buffers without replacing the live geometry. Production transfers
little-endian float32 positions/normals and int32 indices as Blazor byte arrays,
avoiding numeric JSON serialization. Ordinary array-based viewer callers remain
supported. Bounds, normal validation and index validation use the decoded data.

Run `test:kinematic-preservation` as well: it checks real moving geometry, every
SnowCar variant, exact authored constraints, explicit edits, export and reopen.
It also exercises the separate translation and rotation segment counts (1–4,
matching E++), including export and reopen after resizing. Retained functions
and timing mode are preserved; reducing the count removes trailing functions.
New functions use forward Linear easing and 1000 ms durations (or successive
1000 ms endpoints). Imported counts outside the editable range are displayed
unchanged. Invalid/overflowing edits leave both the archive and count control
unchanged. An independent archive comparison verifies the serialized result.
Selection traverses the current source graph but reuses geometry by parsed GBX
node reference identity, not by name or snapshot-local ID. Shared local vertex,
normal and index buffers upload once per document/edit epoch; each occurrence
has its own transform, material and motion ownership. The test verifies that
SnowCar's later moving variants transfer no new geometry, even after visiting
its static variant. World-space snapshot arrays are cached per authored transform
for other consumers, including on-demand OBJ export. Legacy array/binary payloads
still work. All preview edit callbacks invalidate the cache conservatively;
replacing documents and disposing the viewer release its GPU geometry pool.
The typed-viewer suite checks shared instances, independent motion, invalidation,
rejected-payload rollback and explicit clear. Neither optimization changes GBX
serialization or the authored source graph.
