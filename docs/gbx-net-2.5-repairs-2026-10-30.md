# GBX.NET 2.5 repair verification

## Implemented

- Preserve modern custom physics and visual model references when serializing chunk `2E002019`. The reproducible parser patch handles the legacy layouts separately. Ten version/type regression cases cover versions 8, 9, 11, 12 and 15.
- Validate shared vertex strides only for shared-buffer versions; version 1 uses independent buffers. Preserve the version 0 malformed-data refusal.
- Support typed magnet locations, retain flag placement references and preserve serialized empty identities despite the new display-name fallback.
- Accept edition-only crystal items for export; avoid drawing both edition and runtime models.
- Remove the incorrect application of a deformation control frame to the next geometry layer. Enabled deformation layers now produce an explicit unsupported-preview diagnostic. This does **not** implement evaluated deformation geometry.
- Search material and FX dropdowns by name/path, bound rendered choices to 200, preserve the selected value while filtering, and show empty-result feedback.
- Edit authored crystal material links, clearing the alternate named-material representation and creating the serialization chunk when needed. A save/reopen test verifies the actual stored link.
- Restore helper coverage and wait for asynchronous variant selection in browser tests.
- Migrate all tracked test/research projects to the 2.5 reference API and pin the patched parser against transitive LZO dependencies.
- Gate Pages publication on the regression workflow, fix viewer cache invalidation, and run all regression suites before proposing automatic parser updates. Compile every tracked test/tool project in the test runner.

## Measured locally

- All 14 central console suites pass; the changed parser/material/scene suites also pass their added regressions.
- All 51 tracked test/tool projects build successfully.
- 7,539 of 7,550 local `.Item.Gbx` files parse, save and reparse, compared with 7,510 before these repairs. The remaining 11 failures are in earlier experimental FX/car probe directories. This test does not prove semantic equality or native game compatibility.
- Browser checks pass: viewer lifecycle, workflow regressions, helper coverage, editor shell, compact layout, icons, typed viewer (19 groups), variant navigation, material texture preview, and searchable material editing.
- Raw logs and corpus failure details: `artifacts/repair-gbx250/` (local, ignored).

## Outstanding acceptance work

- Evaluate Mesh Modeler deformation masks, boxes and control frames against a native reference. The supplied displaced-mesh case is not yet confirmed visually correct.
- Native placement, rendering, collision and gameplay checks for exported changes. Trackmania was not running and MCP refused the connection. The attempted launch command was blocked by automatic execution policy.
- Investigate the 11 experimental files individually; do not call these supported or silently discard their unknown data.
- Universal import/edit/export remains a development objective, not a completed compatibility claim. External game assets, unsupported modifiers and special vehicle/FX graphs still need explicit capability boundaries and native evidence.

These repairs are a verified regression-fix release, not proof that every available item and every feature works in Trackmania.
