# Hidden FX carrier expansion — 26 October 2026

This follow-up checks additional effect carriers and particle sources from the
hidden FX catalog. A native preload proves that the game owns and can decode a
resource; it does not prove that the resource can be used as a portable custom
item.

## Native item roots

`ShowFoggerWithLight8m.Item.Gbx` and `ShowFoggerWithLight16m.Item.Gbx` both
preload as `CGameItemModel` files from `Stadium.pak`. The 8 m item was placed at
`(740, 8, 700)` in a disposable editor map, saved as
`TMIS_HiddenFX_Catalog_20261026.Map.Gbx`, reopened and observed in the native
playground. The close capture shows the white fog plume and its bright light
after reopen: [`stock-lightfog-close-reopen-20261026.jpg`](stock-lightfog-close-reopen-20261026.jpg).
This proves the stock light-fog carrier's placement, visible effect and map
persistence. It is still a template reference; a custom clone requires a
dependency-preserving package and its own cold-start proof.

The stock `ShowFogger8M` and `Sparkler8m` roots, the `Torch.Prefab.Gbx`,
`Fogger8M.FxSys.Gbx`, and `PodiumSparkler.FxSys.Gbx` resources also pass native
preload. The Podium resource has no separate item root in the extracted catalog.

## Particle-only sources

`PodiumSparkler` and `PodiumSparklerEnd` load as
`CPlugParticleEmitterModel` assets from `Stadium.pak`. `WaterSpray`,
`WaterSprayThin`, `WaterSprayAndSteam` and `WaterSplash` load from
`Maniaplanet_ModelsSport.pak`, but report the native `Unassigned` identifier.
They are vehicle-context resources with no proven item wrapper, placement,
rendering or save/reopen path. They remain candidates for a future carrier
adapter, not editable item settings.

The machine-readable records are in
[`hidden-carrier-expansion-evidence-20261026.json`](../../artifacts/fx-clones/hidden-carrier-tests-20261025/hidden-carrier-expansion-evidence-20261026.json).

The follow-up complete particle-model preload pass is separate from the item
carrier test above. It exercised every parsed row in the 83-model audit and
recorded **54 native preload passes, 7 native preload failures, and 22 parser
skips**. This remains a read-only FID result: it promotes discovery coverage,
not item-wrapper or Studio-writer support. See
[`hidden-particle-native-preload-complete-2026-10-26.json`](hidden-particle-native-preload-complete-2026-10-26.json)
and the row-level
[`fx-hidden-catalog-full.tsv`](fx-hidden-catalog-full.tsv).

## Safe product direction

The next safe expansion is a carrier-template selector for existing native item
roots: Sparkler, Fogger, Fogger-with-light and Torch. It can preserve the whole
opaque dependency graph while allowing the already validated mesh and emitter
origin changes. A general vehicle-particle adapter should only be added after a
new wrapper proves native preload, item placement, visible gameplay and map
save/reopen.
