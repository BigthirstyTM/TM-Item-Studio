# Trackmania 2020 texture library builder

This utility creates a curated local texture folder from an Openplanet
`Extract\GameData` directory. It parses every `*.Material.Gbx`, follows the
material's `CPlugBitmap.ImageFile` references, and copies only the referenced
DDS/TGA/PNG/JPEG/WebP files. `manifest.json` records the exact full material
link to texture-role mapping used by the browser preview.

Example:

```powershell
dotnet run --project Tests/TextureLibrary -- `
  'C:\Users\PC\OpenplanetNext\Extract\GameData' `
  'C:\Users\PC\Documents\Trackmania\Items\_BlenderAssets\Trackmania2020_Textures'
```

The generated folder is intentionally outside the repository and is selected
read-only through the **Materials & LOD** panel.
