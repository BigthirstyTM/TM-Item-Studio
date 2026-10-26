# Temporary rejected flag-material diagnostic

This is research support, **not a fix or a normal installed plugin**. The direct
native-material experiment rendered no cloth. Its predecessor aliased resolved
custom materials and also failed to repair the rendering.

Source precedent: the installed Editor.op's
`MeshDuplication::Solid2AliasCustomMatsIfEmpty`, Unlicense/public domain.

The DEV-only entry point requires the exact gate file and map editor, accepts
only `TMIS_DirectMaterials_Grid`, requires one visual and an empty destination
material table, retains both native owners, and restores on all terminal paths.
No socket, eval, arbitrary memory route, exports, dependency or UI is added.

For the recorded test, source and staged Main.as were identical; the game
accepted the code. A fresh log includes load, `DIRECT GAME MATERIALS`,
`RESTORED 1`, and unload. Openplanet LSP parity was not available. The staged
folder and gate have been removed from the live setup.

Reviewer scope: temporary native-array borrowing on an owned disposable fixture.
Wrong editor / wrong visual count / nonempty destination are guarded. The single
happy path and timeout/restore/unload were exercised. Arbitrary map transitions,
engine replacement of the material table, and other game builds were not tested.
Native pointer ownership outside this bounded fixture is unsupported.

See [the runtime report](../../../docs/custom-flag-runtime-2026-10-21.md).
