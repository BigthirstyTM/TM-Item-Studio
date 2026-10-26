# Custom flag file research — 22 October 2026

Report language: English. Investigation ongoing.

**Later motion/embedding follow-up:** [split native archives, kinematic controls,
and the plugin option](custom-flag-motion-and-embedding-2026-10-22.md). Splitting
the user model into embedded Item/Prefab/Dyna/Mesh archives still leaves 0/5
structured visuals, including a cold test with the loose dependencies removed.
Rigid circular motion and waving cloth have been demonstrated separately;
simultaneous motion and waving are not established.

**A restart-safe, file-only installation control now works for the long,
swallowtail and round flags, without a dedicated flag companion. A portable,
map-embedded custom Item.Gbx is still not established.** These are distinct
outcomes. The successful control installs assets into the game drive and adds
their names to the native item inventory for construction-menu access. A later
test proves that saved-map loading needs only the three Item.Gbx files: the
inventory addition is optional. The map contains no embedded ZIP.

**Latest result:** the same proof map also renders and animates correctly in
cold PID 31804 with only those three files, no inventory overlay, no native
primer anchor and no Openplanet injection. The downloadable package offers
`-ItemsOnly` for this smaller installation.

**Update, 01:56 local:** a cold game process with the Openplanet injection proxy
removed also renders all three custom outlines and animates their cloth. The
accepted minimal installation is three inline Item.Gbx files plus one inventory
file. This establishes a plugin-free installation solution on the tested build;
it does not establish portable embedding.

## Minimal installation and plugin-free proof

The three inline models were isolated from the other 38 experimental installed
assets. Cold process PID 1560 (01:12:07) placed them through the inventory, and
PID 22912 (01:14:32) reopened the saved map. Both showed the correct outlines,
native texture and automatic initialization of all five animated visuals per
model, each retaining 86 frames. These two sessions still had ordinary Openplanet.
The saved map has three custom identities, the user's author ID, and zero
embedded ZIP bytes; Nadeo authorship is therefore unnecessary for this route.

The stronger isolation removed the game's `dinput8.dll` injection proxy before
launch. PID 30924 was an initial distant-view check. PID 20508, started 01:48:21,
opened **TMIS Plugin Free Flags Proof** through the normal game menus. The
camera-friendly copy changes only the map name/UID and the three positions;
the accepted model files are unchanged. It has exactly three custom anchors
and no native Flag16m primer. The stadium's ordinary background scenery is
still present, as in a normal empty stadium map.

- [Plugin-free motion recording](../artifacts/flag-file-followup-20261022/plugin-free/cold2/custom-flags-without-openplanet.mp4).
- [Four captured moments and overview](../artifacts/flag-file-followup-20261022/plugin-free/cold2/motion-contact-sheet.jpg).
- Module lists before/after capture contain no Openplanet module and contain
  only `C:\Windows\System32\DINPUT8.dll`, the legitimate system library.
- Openplanet.log's size and modification time remain unchanged across the run.
- Hashes of all four installation files and the proof map match their receipts.
- The 20-second recording contains 805 captured frames, averaging 40.25 fps
  despite requesting 60 Hz. It is labelled with that actual rate; no frames were
  generated or interpolated. Each 12-second cloth crop contains 482 distinct
  frames out of 483 sampled frames. Correct shape and motion are visible.

Receipts: `minimal-installed/cold1/`, `minimal-installed/cold2/`,
`plugin-free/staging.json`, `plugin-free/proof-map.json`, and
`plugin-free/cold2/{process,modules-before,modules-after,validation}.json`.
The exact injection proxy is restored while the game is stopped after capture.

The separate overview/close-up recording below remains the higher-framerate
presentation video. It demonstrates the earlier native dependency graph, while
this additional clip proves the later minimal installation without Openplanet.

### Saved maps do not need the inventory overlay

A further cold process, PID 7232 started 02:01:50, has only the three accepted
Item.Gbx files installed. The loose inventory JSON is absent. Opening the same
proof map still restores all three identities and all fifteen structured visuals
automatically. The native screenshot shows the correct round, swallowtail and
long outlines. Inventory search returns zero matching entries, as expected:
the catalog controls the construction-menu entries, not saved-map resolution.

Evidence: `catalog-free/staging.json` and `catalog-free/cold1/`, including
`result.json`, `inventory.json`, `process.json`, and `three-flags.webp`.
This particular pass uses the ordinary Openplanet baseline; its result must not
be confused with the separate injection-free runs.

That result was then repeated in PID 31804, started 02:04:18, with the injection
proxy held outside the installation. Normal menus opened the identical proof
map. All three shapes visibly animate. Before/after module lists again contain
no Openplanet module, the Openplanet log is unchanged, the inventory overlay
is absent, and the three item files and map retain their exact hashes.

[Download this smaller-installation proof video](../artifacts/flag-file-followup-20261022/catalog-free/plugin-free/custom-flags-without-openplanet.mp4).
It contains 962 real captured frames over approximately 20 seconds, averaging
48.1 captured fps. Each twelve-second cloth crop has 568 distinct frames out
of 569 decoded frames. The direct capture requested 60 Hz; the caption reports
the actual capture rate. Encoding preserves the captured frame count and adds
only captions. Its final duration differs by one 60 Hz time tick due to muxing.
No motion interpolation or synthetic cloth was used. Receipts and the contact
sheet are in `catalog-free/plugin-free/`.

## Visible evidence and exact scope

- [New overview and close-up video](../artifacts/flag-file-followup-20261022/native-package/video/custom-flags-file-only-installation.mp4).
- [Round flag after reopening the saved map in a fresh process](../artifacts/flag-file-followup-20261022/native-package/cold2/cold2-round.webp).
- [Long flag](../artifacts/flag-file-followup-20261022/native-package/cold2/cold2-long.webp).
- [Swallowtail flag](../artifacts/flag-file-followup-20261022/native-package/cold2/cold2-swallowtail.webp).

The recording is 1920 × 1080, 48.567 seconds, 2,905 captured frames, approximately
59.8 fps (60 Hz requested). It uses direct window capture with no motion
interpolation. The final frame count exactly matches the raw recording.
Stationary close-up samples contain 179/179 distinct frames for long cloth,
179/179 for swallowtail, and 178/178 for round cloth. Their measured region is
the cloth, excluding the clock/HUD. Raw/final metadata and samples are retained
in `native-package/video/video-validation.json`. Its captions identify this as
an installation test, not a portable embedded item.

The disposable scene contains exactly three new item identities and no ordinary
Flag16m primer. Ordinary Openplanet, Editor++, the MCP test tools and the
existing material crash-workaround baseline remain. This is **not a plugin-free
compatibility test**. Dedicated flag helpers and the temporary placement-only
probe are absent in both cold processes and during the new recording.

## Working installation control

The generator starts with the directly extracted native archives and changes
one dependency at each level:

1. A new Item.Gbx has its own name/ID and references its own Prefab.Gbx.
2. That prefab references its own DynaObject.Gbx.
3. The dyna object references its own Mesh.Gbx.
4. The mesh preserves native frame tables, LODs and native early material
   references, changing only the cloth geometry and its bounds.

There are four files per shape, twelve in total. They reside beside the native
files in `GameData/Stadium/Items`, `Media/Prefab/Items/Flag`, and
`Media/Dyna/Flag`. No stock item, mesh, shader or packed game file is overwritten.
The models use the existing packed ItemFlag/ItemFlagNoAnim materials.

A temporary loose `GameData/Stadium/GameCtnBlockInfo/ItemInventory.gbx.json`
adds one folder with the three new item names. It shadows the virtual native
inventory during this control; the original inventory remains in Stadium.pak.
This catalog addition is an installation dependency and must not be hidden in
the definition of “standalone.”

The native collection/author identifiers are inherited from the vanilla archive.
They make these game-drive models native-style catalog assets. They do not
credit the new edits to Nadeo and are not evidence that these are distributable
user items. A normal saved map records the three identities rather than
embedding their assets.

| Observation | Result |
| --- | --- |
| Parsed native item saved again, then name changed | Both load |
| Native mesh saved again; width multiplied by 1.025 and 2.5 | All load; four animated LODs automatically use structured buffers |
| Three complete native-drive shape packages | All twelve files load |
| Hot placement before inventory registration | Normal inventory cannot find the models; direct ItemSpec placement returns false, zero items |
| Cold process PID 28512, started 00:57:00 | Catalog contains the three new names; normal MCP inventory placement succeeds; all shapes and native texture visible |
| Saved map reopened in PID 29516, started 01:00:16 | Same three models/positions; correct shapes visible; twelve animated visuals automatically initialized |
| Saved map | Three native-style item identities, embedded ZIP size zero |

Each directly extracted native mesh has four animated visual nodes with 86
frames each and one static far visual with zero frames. The recorded cold map
has the structured flag enabled on all four animated visuals per model and
disabled on the static far visual, as expected. This differs from the earlier
native-writer user-item fixtures, which contain five animated visuals with 86
frames. Counts must not be mixed between the two representations.

The identical saved map has SHA256
`D4AD9B1DA8F207ED4D7A7B4F684F2B4753B1E7991D98CE4016A8729B4D9FC893`.
It was not regenerated between the save and cold reopen.

Receipts: `native-ladder/files/manifest.json`, `native-ladder/read-results.json`,
`native-chain/read-results.json`, `native-chain/inline-dump.txt`,
`native-chain-fixed-v2/read-results.json`, `adjacent-files/visuals.json`,
`native-package/files/manifest.json`, `native-package/catalog-control/`,
`native-package/cold1/`, `native-package/cold2/`, and the recorded game calls.
All evidence paths are under `artifacts/flag-file-followup-20261022/`.

## The native-primer hypothesis remains rejected

The earlier independent cold tests already showed that adding a native flag
makes cloth appear on uninitialized user models, but turns their custom geometry
into native rectangles. Custom visuals remain 0/5. See the
[native-primer report and recording](custom-flag-native-primer-2026-10-21.md).

Two further triangle-subset tests preserve the native vertex positions and
change only index triangles: rectangle, half, swallowtail, round and a clipped
narrow rectangle. The narrow rectangle is **not** a 2.5-times-longer flag.

- The first set uses the native-writer user-item geometry.
- The second restores the directly extracted native geometry, frame numbering
  and LOD distances, with a valid static far material.

Neither is reliable. Custom-only views show malformed cloth; adding a native
flag still produces jagged outlines and giant black sheets at overview distance.
These do not count as successful custom shapes. Coarse triangle filtering can
also retain a rectangular outline at the sparsest round LOD.

The first attempt at the second generator had a fixture defect: a nonempty
material name suppressed serialization of its supplied UserInst. Placing that
fixture caused a null material access at Trackmania.exe offset `0x4C9579` in
PID 5464 at 00:33:09. It was diagnosed, quarantined and corrected. It is not
evidence that native geometry itself is unsupported. The corrected set loads
and places, but still fails the rendering test. Rejected files must not be
reopened.

## Other file-only routes tested in this follow-up

| Route | Observed result |
| --- | --- |
| Legacy MaterialIds/MaterialsFolderName spellings, versions 15 and 28 | User items load; 0/5 structured visuals; inspected early material arrays empty |
| Early placeholder material plus late UserInst, versions 15/28 | Accepted variants remain 0/5; combined incompatible arrangement returns null |
| Complete extracted local shader dependency tree plus nine real compiled caches | Local cache node loads; local shader does not resolve; item remains 0/5 in a cold process |
| Authored HLSL equivalent compiled with D3DCompile | Host compilation succeeds; it does not establish an initialized native runtime shader; user bootstrap remains 0/5 |
| Uniquely prefixed entries in the installation GPU-cache ZIP | User shader remains unresolved; original ZIP restored byte-identically |
| Direct early native material references from a nested game-drive folder, ancestor 4 / GameData path | Files discovered but preload returns null, including from a temporary Extras ZIP |
| Compressed versus uncompressed body for those three files | All six preload null; compression is not the cause |
| Same custom geometry saved beside native Items, ancestor 1 / Media path | All three load and automatically initialize 5/5; installation-context positive control |
| Exact native item, mesh and shader copied beside their sources | All three load; disproves a blanket inability to load newly added game-drive files |
| Three accepted inline items, all other installed controls isolated, two cold processes | Correct custom shapes and 5/5 animated visuals per item; saved map reopens |
| Embedded-context preload, empty map with zero anchors and no custom installation files | Three early-material reference roots all return null; no candidate placed |
| Newly named ZIP in Packs, cold process | ZIP is not auto-mounted; custom files absent from game FIDs and original packed catalog remains active |
| Legacy MaterialUserInst8 early array | First fixture crashes during preload; quarantined and not retried; next five were not tested |

The Extras ZIP and GPU-cache ZIP were restored before the working catalog test.
The original nested reference arrangement and the adjacent arrangement differ
in both archive ancestor and location. The new result establishes the valid
adjacent route; it does not by itself prove the exact internal reason ancestor
4 failed.

A separate native-archive inlining test initially remapped the reference-table
descriptors rather than every actual reference instance in the parsed node
graph. Saved files still contained unrebased material paths and returned null.
After remapping the actual graph references, both inline-dyna and inline-mesh
installation controls load. This generator mistake is corrected in
`FlagNativeLadder.cs`; it must not be interpreted as an engine limitation.

The embedded-context test deliberately starts with an empty map and embeds
three current-format model files. Their FIDs are discovered beneath
`fake:MemoryTemp/CurrentMap_EmbeddedFiles/ContentLoaded/Items/`. Ancestor 0
with a GameData path, ancestor 1 with a GameData path, and ancestor 1 with a
Media path all fail native preload; the paths remain under the embedded Items
tree. The map itself opens and no anchors are placed. This is a safe negative
loading result, not an embedded rendering success. Receipts:
`embedded-context/files-v2/manifest.json`, `embedded-context/staging.json`, and
`embedded-context/cold1/`.

The separately tested auto-mount ZIP is archived under `single-zip/`. It must
not be confused with the download installation ZIP, which must be extracted
and installed. The latter is a packaging convenience, not a mounted resource.

The legacy early-material crash occurred at 01:06:02 in PID 29516, offset
`0x2EDBA8`, null metadata with an attempted read at `0x30`. Only the first
fixture was actually preloaded. Subsequent connection failures do not count as
tests of the other five files. All six are quarantined in
`legacy-materials/rejected-live-items/`, and the active generator dispatch is
removed. The retained dump and evidence describe a rejected route.

## Mechanism and remaining portable-item requirement

Earlier GPU captures established that the VertexTween vertex shader reads a
structured vertex buffer at VS t0 using SV_VertexID. A normal vertex buffer
alone is insufficient. The transient visual flag at `CPlugVisual+0x24`, bit 27,
is not preserved by the tested visual archive formats.

Solid2 post-load checks the effective shader through the early material arrays
before processing the usual late user materials. Native-drive references
resolve to the packed material/shader at that point and now initialize the new
custom geometry automatically. The old session-folder-pointer diagnostic is no
longer the only positive control. The new installation files contain no process
addresses and survive a real restart unchanged.

For an ordinary portable user item, the unresolved requirement is still a stable
serialized early material/shader route that works in the user or embedded file
context. Ordinary relative, absolute and alias spellings tested so far resolve
under the user's item folder rather than selecting the packed shader. Saving a
working game-drive placement produces native identities with no embedding; it
does not solve that requirement.

Next acceptance gates are: a valid user-context material/mesh representation,
automatic initialization without any installed custom game-drive files,
identical-file cold reload, and embedded-only loading with loose dependencies
removed. Only a newly validated candidate should enter that last test; the
earlier null-shader crash fixture remains quarantined.

The additional serialization review uses pinned upstream GBX.NET 2.4.5 class
definitions and read-only native disassembly. No new portable candidate was
validated by that review. In particular, visual chunk `0900600F`'s mapped
archive flags still omit runtime bit 27; chunk `09006010` stores morph data,
not that bit. The alternate `0x14049A680` routine writes a caller-supplied
declaration mask, so its use of bit 27 must not be promoted into a proven
serialized visual-flag solution. Definitions are retained in
`serialized-route-review/`. No speculative fixture from this review was placed.

Full vanilla parity is not claimed. Additional work remains on portability,
all LOD transitions, skin changes, lighting/normals, collisions and clients
on other game builds or in multiplayer. The two injection-free recordings
remove the earlier test-plugin dependency from the installation claim, but do
not cover those other compatibility surfaces.

## Download, installer verification and restoration

[Download the experimental installation package](../artifacts/flag-file-followup-20261022/download/TMIS_CustomFlags_Installation_20261022.zip).
It contains only the three accepted inline Item.Gbx files, the optional
inventory addition, English instructions, a manifest, a PowerShell installer,
and the original/proof example maps. No shader/cache replacement, flag plugin,
process address, rejected fixture or crash dump is included.

The default install adds three flags plus their construction-menu folder.
`-ItemsOnly` installs just the three flags for saved-map loading and leaves any
inventory overlay untouched. Every client needs the installed item files.
The ZIP is not automatically installed or mounted by the game.

The installer was executed with the game closed. PowerShell AST validation,
full install/uninstall, both WhatIf previews, exact post-copy hashes, and refusal
to overwrite existing targets passed. The ItemsOnly install/uninstall and both
of its WhatIf previews also passed, with the inventory JSON remaining absent.
ZIP integrity and the hashes of all four packaged assets were independently
checked. Receipts: `download/package-validation.json` and
`download/installer-validation.json`. The tested installer SHA256 is
`A4F3B9714D6B5C411491F6E0EE54BB17D1AB3AD7D73428CC157D816310B46242`.

After these tests, the three custom game-directory item files and the loose
inventory JSON are absent again. The earlier 38 installed controls remain
archived. Openplanet's original injection proxy was restored byte-identically
while the game was stopped. Protected asset checks retain the original Editor.op,
XertroV source item, GPU-cache ZIP and Extras ZIP hashes. See
`catalog-free/proxy-restoration.json` and `protected-final-hashes.json`.

Normal process PID 24368, started 02:11:24, is verified in Menu with readiness
clear, a healthy E++ tool pack, no dedicated flag helpers, zero tracked agent
mutations and an empty default tag. Six disposable camera/empty-map copies were
hash-verified and archived. The proof/example maps are retained and require
reinstalling the accepted models before opening. The fresh restored log and
cleanup receipt are in `final-recovery/`. Unrelated baseline plugin self-test
diagnostics are retained in that log; this work does not claim to fix them.

The final C# probe build passes with zero warnings and errors. Tested game:
Trackmania `2026.2.2.1751`, engine `2026-02-02 17:51:19`. Ordinary comparison
sessions use Openplanet `1.29.14` Next/Public `6fd4200f`; the two explicit
plugin-free motion runs contain no Openplanet module. This is a verified
installation solution on this build, while portable map-only delivery and
complete vanilla behavior remain open.
