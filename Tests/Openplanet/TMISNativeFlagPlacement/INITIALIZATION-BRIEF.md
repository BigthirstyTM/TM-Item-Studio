# Native package placement probe

- Identity: `TMISNativeFlagPlacement`, one temporary DEV module.
- Purpose: place three fixed native-drive flag package models in an empty disposable map. No animation, shader, visual flag, or material state is modified.
- Target: Trackmania 2020; researched baseline 2026.2.2.1751, Openplanet 1.29.14 Next/Public. No broader compatibility claim.
- Publication: local-only; do not publish this probe or extracted native assets.
- Provenance and AI: newly AI-authored test code; ItemSpec pattern follows existing local probe and Editor++ exports, Unlicense/public domain. No paid-feature bypass; normal editor entitlements remain required.
- Topology/dependencies: one leaf module, required installed `Editor` module. No shared exports, sockets, hooks, settings, UI, retained nods, or dependents.
- Source/staging: this directory is canonical; copy manifest and Main.as byte-identically to the live plugin directory. Machine paths and hashes belong in excluded artifacts.
- Callbacks: bounded synchronous Main, with native engine-only operations and exception logging. No yields or asynchronous resources.
- Gate: READY marker in a fresh log; without the explicit `Items/TMIS_NativePackage/place.enable` file, do nothing. With the gate, require an empty Unnamed map and exactly three known model IDs.
- Validation: LSP exact source/staged parity, fresh load log, pre/post map counts and item identities, screenshots. Engine-only placement has no deterministic unit seam; native observation substitutes. Windowless fixed probe: demo menu N/A.
- Shutdown: unload and remove gate before recording/restarting. No retained state or teardown mutation. Existing empty-map capture and named item identities identify all mutations; discard only this disposable map when done.
- Assets/authentication/settings: N/A. No bundled media, credentials, or public configuration.
- Packaging: no publication or signing; exclude probe from evidence/model download packages. Any later publication is a separate human action.
- Initialization result: canonical manifest and entrypoint present; runtime and LSP receipts stored under the investigation's native-package artifacts. Missing/failed checks must remain explicit in the report.
- Local-state location: artifact tree (repository-ignored), not portable plugin source. Follow-on implementation uses openplanet-dev/control/lifecycle.
