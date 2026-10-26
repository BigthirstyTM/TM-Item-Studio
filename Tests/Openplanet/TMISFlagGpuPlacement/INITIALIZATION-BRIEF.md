# Plugin initialization brief

- **Identity/folder:** `TMISFlagGpuPlacement`, fixed GPU flag placement probe, version 0.1.0.
- **Purpose:** bounded, opt-in DEV research for three named animated flag fixtures; never a general item loader or public flag fix.
- **Target/tested scope:** Trackmania only; tested game 2026.2.2.1751 / engine 2026-02-02 17:51:19, Openplanet 1.29.14 Next/Public 6fd4200f. Raw offsets require revalidation after updates.
- **Publication intent:** local-only, do not publish.
- **Source/provenance/AI:** newly authored with Codex assistance for this user-authorized investigation; no bundled third-party art, fonts, or binary hooks. Native item assets remain separate research fixtures.
- **Permissions/policy:** normal game/editor entitlements; no entitlement bypass, authentication, external messaging, signing, terms acceptance, or publishing. Disposable test maps are user-authorized.
- **Topology/dependencies:** one module; required existing `Editor` dependency preserved from the fixture manifest. No exports, shared exports, network service, or reverse dependents.
- **Canonical source/staging:** this folder owns `info.toml` and `Main.as`; copy the two files unchanged to the same named live plugin folder. No generation/build step. Compare SHA-256, then run `openplanet-lsp` against the exact staged bytes.
- **Callbacks/ownership:** `Main` owns preparation/wait/restore; `OnDisabled` and `OnDestroyed` also restore. Retained visual handles own only bits that were initially clear. Placement mode is limited to an empty editor map; startup mode requires no active editor. Fixed gate files opt in; expiry is 240/600 seconds. Missing gates are inert.
- **Observable gate/validation:** fresh successful load plus three `PREPARED` lines; exact staged LSP diagnostics, native captures, two independent startup processes, saved-map node identity observation, and fresh restoration/unload logs. Engine-only behavior has no synthetic deterministic substitute. No main window; demo UI N/A.
- **Assets/configuration/secrets:** none bundled; only fixed non-secret enable gates. No secret-store boundary required.
- **Packaging/disclosure/human boundary:** DEV define only. Exclude all temporary models, enable gates, rejected fixtures, logs and machine-local state from public packaging. This is not release-ready; no upload or approval is implied.
- **Initialization result:** manifest and entrypoint loaded successfully. Current result is a restart-assisted proof, not standalone item support. Clone-local evidence is in the repository's ignored `artifacts/flag-round-video-20261021/` tree. Refer to the English persistence report for exact receipts and unresolved transitions.
