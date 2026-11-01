# Path authoring / projects verification

Version: 0.5.17. Local build, 2026-11-01.

- `dotnet build -v q`: passed (22 existing warnings, no errors).
- `Tests/run-all.sh`: all 14 console suites passed; tracked research/fixture projects built successfully. ItemMotion: 71 passed, 0 failed.
- Browser `path-authoring-project.cjs`: actual Bezier handle drag, linked controls, undo/redo, look-at, coincident follower detection, separated follower clearance, negative margin storage, reference upload/offset, Apply, save project, fresh-page reopen, invalid ZIP refusal preserving current draft, reapply and GBX export passed.
- Browser `path-array.cjs`, `collision-array.cjs`, `model-route.cjs`: passed.
- Browser `viewer-lifecycle.cjs`: passed after replacing the ambiguous first-file-input selector with the explicit item upload selector.
- Browser `typed-viewer.cjs`: all 19 groups and disposal passed.

Local artifacts: `artifacts/collision-arrays/Authoring-Proof.tmisproject`, `Authoring-Proof.Item.Gbx`, `authoring-editor.png`, `authoring-all-suites.log`, `authoring-build.log`.

No new native game run was performed. Clearance uses sampled visual bounds, not physics collision geometry. Look-at is a fixed target baked at samples. Project files do not bundle external GameData or textures.
