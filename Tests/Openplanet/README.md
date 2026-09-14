# Trackmania native export smoke test

`TMItemStudioSmokeTest` is an Openplanet Developer-mode plugin that uses
Trackmania's native FID loader, rather than GBX.NET, to load both test exports
from:

```text
Documents\Trackmania\Items\BF2_ASSETS\Test_Items
```

It verifies that each file can be preloaded as `CGameItemModel` and has an
entity model. The Openplanet runtime binding does not expose the item's
`Ident` metadata, so the studio's pre-export validation checks that separately.
Results are written to `Openplanet.log` with the `[TMIS-SMOKE]` prefix.

## Run it

1. Enable **Developer** signature mode in Openplanet.
2. Copy the complete `TMItemStudioSmokeTest` folder to
   `%USERPROFILE%\OpenplanetNext\Plugins\`.
3. In **Openplanet → Plugin Manager**, enable the local
   `TM Item Studio export smoke test` plugin, then restart Trackmania.
4. Inspect the results:

   ```powershell
   Select-String "$env:USERPROFILE\OpenplanetNext\Openplanet.log" -Pattern '\[TMIS-SMOKE\]'
   ```

The test intentionally reports the currently checked-in crash-repro fixtures
as failures until they are replaced with game-ready exports. It is a native
load and metadata test. Trackmania exposes no supported public Openplanet API
for selecting an inventory entry and committing an item placement; asserting
that final UI action would require version-fragile private-memory patches or
screen-coordinate automation, so this test does not falsely claim placement
coverage.

## Native pivot save

`TMItemStudioNativeBridge` is the separate native-write integration for
pivot/placement changes. It requires the Editor++ DEV plug-in and saves via
Trackmania rather than GBX.NET. See its README for the installation and
safety constraints.
