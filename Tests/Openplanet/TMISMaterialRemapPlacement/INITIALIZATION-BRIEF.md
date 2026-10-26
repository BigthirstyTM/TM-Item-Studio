# Temporary material-remap placement probe

- Identity: `TMISMaterialRemapPlacement`, one windowless local DEV module.
- Purpose: place one already validated user-context item on an empty disposable
  map using ordinary Editor++ ItemSpec APIs, bypassing the stale article cache.
- Target: Trackmania 2020; current research build 2026.2.2.1751, Openplanet Next
  1.29.14. Runtime results belong to the research receipts, not this brief.
- Publication: do not publish. No signing, attestation, uploads or paid-feature
  bypass. User-authorized local authoring research only.
- Provenance: newly adapted from the existing project placement probe and
  Editor++ Unlicense APIs, AI assisted. No bundled media/assets/fonts.
- Dependency: existing `Editor` module, required, loaded before this leaf.
  No exports, shared classes, network, control endpoint, settings or consumers.
- Canonical source: this directory. Stage only `info.toml` and `Main.as` as
  byte-identical folder-plugin files; record SHA-256 and fresh log boundary in
  excluded local research artifacts. No secrets are read or recorded.
- Callback: `Main` coroutine, once per intentional load, with a fixed local
  enable-file gate. Refuses anything except an empty map named `Unnamed` and
  the exact expected model. One fixed position, no cursor preview/autofocus.
- Observable gate: a fresh READY and RESULT log plus exactly one verified
  map item with the expected model and position. Engine-only probe; a pure
  unit test cannot substitute for placement and native model construction.
- Static checks: available Openplanet LSP on the exact staged bytes, followed
  by fresh native compile/load diagnostics. Keep all warnings/parity limits.
- Lifecycle: leaf with no reverse dependents. Use the installed project leaf
  lifecycle action; preserve baseline plugins. Unload after placement, remove
  the enable marker, and prove loaded-set restoration. Never reload Editor.
- Renderer integrity: no Dev writes, hook, patch, shader/material setter or
  buffer adjustment. The file's ordinary MaterialModifier is the experiment.
- UI/demo: N/A; this is a bounded windowless diagnostic whose fresh log and
  map-state receipts are its observable surface. No persistent product feature.
- Completion limits: successful placement is not waving, embedding or restart
  proof. Those require separate effective-graph, viewport and cold tests with
  this helper absent. Cleanup discards only the verified owned disposable map.
- Local capability/results: excluded `artifacts/flag-selfcontained-20261022/`.
  No machine configuration or asset payload is part of a published package.
