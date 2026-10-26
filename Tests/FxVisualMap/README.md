# Offline FX visual fixtures

This console tool creates disposable maps for a separate native test. It does
not stage items/maps, operate Trackmania, or establish native rendering.
Supported input item bytes are pinned by SHA-256. Items are embedded verbatim;
only the map is serialized. The native road/start fixture is retained, prior
custom anchors are replaced, and unsupported lightmap cache chunk `0304305B`
is omitted from the generated copy. Input files remain unchanged.

```powershell
dotnet run --project Tests/FxVisualMap/FxVisualMap.csproj -- <native-start-map> <exact-item> <unused-output-directory>
dotnet run --project Tests/FxVisualMap/FxVisualMap.csproj -- pair <native-start-map> <browser-sparkler> <browser-fogger-bell> <unused-output-directory>
dotnet run --project Tests/FxVisualMap/FxVisualMap.csproj -- audit-pair <browser-sparkler> <browser-fogger-bell> <unused-output.json>
```

`pair` accepts the exact E942E5FC browser Sparkler and B48FF619 browser Bell
exports. It checks their item-space emission point `(0.5, 4.25, -0.5)`, puts
them at `(715,8,720)` and `(725,8,720)`, and verifies both exact embedded item
payloads, anchor identities/transforms, embedding metadata and five road
blocks after reparse. No native FX anchor is added.

`audit-pair` inspects their separate display/collision geometry through the
bounded preview helper. Both retain the same original 4 metre cube collision
from `(-2,0,-2)` to `(2,4,2)`. Their new display meshes are smaller and tapered;
the collision was not resized. This file audit does not establish native
driving/collision behavior.

Every generated map has a manifest recording hashes, paths and the actual
scope of the checks. A later native editor save can drop its embedded FX
items; restore the newly saved map using `Tests/FxMapPackaging` or the Studio
packaging feature before retesting or sharing it.
