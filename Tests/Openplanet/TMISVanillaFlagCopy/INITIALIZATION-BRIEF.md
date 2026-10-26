# Temporary native vanilla-copy probe

- Identity: TMISVanillaFlagCopy, one windowless DEV-only module.
- Purpose: open exactly the game's Flag16m FID and save a new, fixed destination
  through the installed Editor exports; no change to Editor itself.
- Target: Trackmania 2020; tested environment 2026.2.2.1751, Openplanet 1.29.14.
- Distribution: local-only research, do not publish. Newly authored AI-assisted
  code using documented exports; no copied artwork packaged. No paid-feature
  bypass or external communication. The user's installed editor entitlement applies.
- Dependency: existing Editor module. No exports or reverse dependents.
- Canonical source: this directory, copied byte-for-byte to live Plugins.
- Callback: Main only. No UI/gallery: an engine save and fresh log are the
  relevant evidence surface. Engine-only behavior has no pure unit-test seam.
- Gate: a fixed enable file; no sockets, eval, generic routes or arbitrary paths.
  Missing gate makes Main return. Existing destination is refused.
- Validation: exact source/staged SHA256, fresh load/compile log, native saved
  file, parse and fresh-process inventory/place/render checks. LSP availability
  is recorded separately; a bridge response alone is not success.
- Assets/settings/authentication/shared exports/packaging: N/A.
- Lifecycle: independent leaf; load/unload via existing bridge. Manual Plugin
  Manager fallback remains available. No background job after Main terminates.
- Clone-local receipts: git-ignored artifacts/flag-vanilla-steps-20261021.
- Completion: fresh loaded event plus SAVED log and actual new output, then
  unload and remove the staged probe. Later experiments use the saved copy.
