# Vanilla GameData preview

Use **Load GameData dependencies** to select the extracted `GameData` root.
The browser indexes local file handles and reads the selected model's dependency
closure. It also indexes DDS textures and the four Stadium mood folders. Files
stay local; game assets are not included in this repository.

Open `.Item.Gbx`, `.Mesh.Gbx`, `.Solid.Gbx` or static object files with **Open
item files**. Ordinary item geometry follows external prefab/static/solid
references. Materials retain their native names and links for local texture
matching. The Materials panel also accepts a texture directory without replacing
the GameData dependency index.

Vehicle and pilot items use the skin archive declared in the original reference
table and its extracted MainBody/Player mesh. These are read-only static previews,
with the original uploaded bytes retained. Physics, damage and character animation
are not simulated. The pilot archive has an undecoded trailing record; this is
reported as a partial archive. Assets without a decoded scene adapter or a usable
declared skin remain explicitly unsupported. This is not a claim that every
native GBX class can be rendered or edited.

Morning/Day/Evening/Night map to Sunrise/Day/Sunset/Night under
`Stadium/Media/Moods`. The preview reads `Mood.MoodSetting.xml`, `SkyColor.dds`,
`SkyClouds.dds` and `EnvCubicHdr.dds`. Ambient, sun/moon colors, time, latitude
and fog come from those files. BC6H images use the MIT-licensed local
texture2ddecoder.js 1.1.0 decoder. It produces 8-bit pixels, so HDR range is
clipped. Three.js lighting, sky projection and tone mapping approximate the game;
Trackmania's atmosphere, auto-exposure, lightmaps and post-processing are not
reimplemented.

Local regression corpus (requires the user's extracted data):

```powershell
dotnet run --project Tests/VanillaPreview -- 'C:\Users\PC\OpenplanetNext\Extract\GameData'
```

The suite checks two cactus items, direct static/mesh roots, four vehicle items,
the pilot item and a direct female pilot mesh. It asserts decoded finite geometry,
native material identities and unchanged source bytes. Partial character archives
are reported explicitly. Browser screenshots are still required to prove visible
appearance.
