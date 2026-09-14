# Approved-item browser and TM2020 batch — 2026-09-14

Baseline: PR #16 at `3f73f69f7c3a067ab52a826987b24174e3b6da8c`.
Candidate: that code with the pinned GmSurf parser described in
[parser provenance](../parser-provenance.md). All named items were explicitly
approved by the user for these tests and screenshots.

Each item used the actual browser file-input, export download and reupload.
No injected scene data or direct archive writer replaced the UI export.
Exports were staged with available matching mesh/shape dependencies before a
fresh TM2020 launch. E++ `InspectUserItemModel` performed `Fids::Preload`, then
`PlaceItem` was checked against the actual map-item count. Screenshots were
visually inspected; a successful preload alone was not counted as placement.

| Item | TMIS upload/export/reopen | Native preload | Placement/visual result |
| --- | --- | --- | --- |
| Custom vanilla pusher | Baseline fails surf 10; development parser passes with geometry | Pass | Placed, orange/black geometry visibly present |
| Blimp | Pass, geometry visible | Pass | Placed, yellow blimp visibly present |
| Cat, unchanged collection | Pass, pivot only; external legacy mesh not rendered | Pass, StadiumMP4 | Placement refused; not a full E2E pass |
| Bretzel, unchanged collection | Pass, pivot only; external legacy mesh not rendered | Pass, TMCommon | Placement refused; not a full E2E pass |

The two placed items were saved in `StudioApprovedBatch_20260914.Map.Gbx`.
Reopening the map retained both items (count 2). This verifies map-editor
placement/rendering, not a full driving/collision/animation simulation.

## Visual evidence

Screenshots were captured and visually inspected for the pusher's initial
import error, successful browser preview, and native placement, and for the
blimp's browser/native previews. Test captures are retained outside git for
the PR discussion rather than added to the source tree.

## Downloaded bytes

| Item | SHA-256 |
| --- | --- |
| Pusher | `a2dda3552d14dee0998d7b11884fc156e16a17acfaa01301faa510b39911b416` |
| Blimp | `2cd91f5c7c302dfadce7f8e0f0943f99831a8ce2e5d438a0229fa2c528b879f8` |

The archive identity checker preserves numeric collection 26 for both. The
helper's loaded DLL is now checked against `lib/GBX.NET.dll` before testing.

## Turbo limitations are separate from surf support

Cat references `Meshes\Cat.Mesh.gbx` by a legacy string; Bretzel uses the same
legacy representation. Uploading only an item cannot supply its external mesh.
No sibling image/texture files were found in the tested Cat/Bretzel dependency
folders. This does not establish that no textures exist elsewhere.

An absent mesh cannot be fixed by a fallback texture. The separate diagnostics
change makes the distinction visible and exports dependency names and model
types. Collection-26 repair cases are recorded separately from these unchanged
exports; preserving an original non-TM2020 collection is not a parser regression.

### Explicit collection-26 repair checks

The separate Cat and Bretzel cases used the real identity input to set collection
26, then exported and reopened through the browser. With their matching external
mesh/shape dependencies installed, both exports preloaded and placed in TM2020.
Close camera views confirmed textured geometry for both, not merely pivots or
item-count changes. These edits were explicit compatibility repairs; unchanged
exports retain the original collections and the results in the table above.

The existing editor map was backed up before testing. The two repaired items were
placed far from its original item, whose properties remained unchanged; block
count remained 2306 and item count increased from 1 to 3. No map was replaced to
perform the placement checks.

**Remaining limitation:** TM reported `Error while saving items into the map file`
for both repaired legacy items. A separate local test map was saved with the
visible **Save anyway** action. This does not demonstrate portable embedding:
the map requires locally installed item dependencies. Reopening this unembedded
test save failed because existing custom-block resources were also no longer
embedded. Therefore the repaired Cat/Bretzel cases are **placement/rendering
passes, not map-round-trip passes**. The backup remained byte-identical.
Screenshots and the native receipt are retained outside git.
