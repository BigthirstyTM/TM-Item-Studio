# Minimal native-validated upload fixtures

These two 1,372-byte items are constructed entirely by `../FixtureGenerator`.
They contain two unshaded triangles, one static body and one kinematic body,
with reversed prefab order in the second file. There are no third-party assets,
external dependencies, material/shading tables, or collision surfaces.

Both exact files passed fresh TM2020 disk preload as `CGameItemModel` and a
subsequent native E++ resave on 2026-09-12. Each preload started from a new,
unloaded FID (`alreadyLoaded=false`), not a cached node. Validation used
Openplanet 1.29.14 Public (6fd4200f), engine 2026-02-03 03:51:19.

| File | SHA-256 of contributed input | Native preload/resave |
| --- | --- | --- |
| animation-static-first.Item.Gbx | `f4956a50d8ed438a3afe2c21e67cf8cc29d5041920defb77fda98efebcb993fc` | Passed |
| animation-static-last.Item.Gbx | `678543f6bef6c8f204aad5da4e5ff7e8859c663da1d3027fe753a681456d607d` | Passed |

The browser runner regenerates the files, requires byte-for-byte equality with
these contributions, then uploads these exact contributed files through the
normal UI. It verifies that the static triangle stays fixed and the moving
triangle changes. Generation itself checks decoded geometry and flags through
the bundled GBX.NET parser and Studio scene extraction.

Native preload/resave is **not** proof of in-game rendering, placement, collision,
or exact animation timing, and does not resolve the reported item-editor
overwrite/save crash. These intentionally unshaded archives are parser/browser
regression fixtures, not finished placeable assets.

## Reproduction

```sh
npm --prefix Tests/Browser run test:animation-upload
```

The generator uses native-observed archive versions and a 12-byte Float3 stride.
An independently E++-constructed constraint supplied the SubVersion 3 reference:
changing only SubVersion 0 to 3 made the previously failing minimal envelope
preload and resave successfully. Parser self-roundtrip had not caught that error.

These bespoke generated fixtures are contributed to the public domain under
CC0-1.0, with permission to relicense them at the maintainer's convenience.
