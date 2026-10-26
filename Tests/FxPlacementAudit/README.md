# Native FX mesh and placement audit

These are offline research fixtures. They never stage an item into the game,
reload a plugin, or call Openplanet.

`audit.py` compares the original FogVolumetric item, native Save-As clone and
Python synthetic-mesh candidates. It also produces two 8834-byte copies of the
native Fogger: an identity-only control and a tapered tower. The native header
and body identities both use the exact matching Items-relative path. Tower
positions are changed in place; all other mesh/FX/placement bytes stay exact.

```powershell
python Tests/FxPlacementAudit/audit.py
```

The audit report is written under
`artifacts/fx-clones/fx-native-mesh-proof-20261025/placement-audit.json`.

`deform_mesh.py` applies a bounded mesh deformation to a compressed or
uncompressed source, using the container implementation in `Tests/FxStandalone`.
It decodes only the isolated vertex stream, verifies a unique Float3 position
block and reparses the modified stream. It retains the complete header/reference
prefix and all decoded body bytes outside positions.

```powershell
python Tests/FxPlacementAudit/deform_mesh.py `
  --source '<Sparkler candidate.Item.Gbx>' `
  --output '<Sparkler mesh candidate.Item.Gbx>' `
  --shape sparkler-flare
```

Available shapes are `sparkler-flare` for the actual 24-vertex Sparkler source
and `fogger-tower` for the 129-vertex FogVolumetric source. Neither changes the
emitter origin or tries to register a new native inventory identity. Materials,
UVs, normals and index topology remain source-exact. The original conservative
mesh bounds still contain the smaller deformed geometry. Native visual tests
must assess the retained source normals and materials.

Native preload, placement, particle visibility and map save/reopen are separate
gates. The files and their manifests must not be described as finished custom
FX items until those gates pass.
