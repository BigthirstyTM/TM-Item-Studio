# Item types, moving waypoints, audio and inventory metadata

## What ItemType actually changes

The enum is shared across engine/game generations. It selects a model category; it does not manufacture the corresponding physics, animation, actions or gameplay controller.

A controlled test changed the header ItemType on the same cart prefab and saved/reparsed each variant:

| Type | Save/reparse | Native FID load | What this proves |
| --- | --- | --- | --- |
| Undefined, Ornament, PickUp, Character, Vehicle, Spot, Cannon, Group, Decal, Turret, Wagon, EntitySpawner | Passed | Passed | The loader accepts this prefab with these labels; specialized gameplay is NOT demonstrated |
| Block | Refused: EntityModel cannot exist for Block | Not attempted | This prefab is incompatible with the Block serialization branch |

Ornament is the normal starting point for custom decorative, collidable or kinematic items. A visible cart on a path remains a kinematic item; choosing Vehicle or Wagon does not add driving or rail logic. A checkpoint needs waypoint metadata and a real trigger shape; the ItemType label alone does not create one. Vehicle/Character/Turret/Cannon need suitable model/action/physics graphs and title support. Do not equate the names with available TM2020 custom-item templates.

## Moving checkpoint research

Candidates were generated from the same known-loadable kinematic cart:

- No-trigger control: 16 m X motion, 8 s ping-pong; native load passed.
- Separate NPlugTrigger_SWaypoint plus motion: native load refused.
- Additional kinematic constraint targeting the trigger: native load refused.
- Trigger and body in a nested prefab driven by the existing constraint: native load refused.
- Common-item trigger entity nested in the prefab: native load refused.
- Common-item root with static mesh and trigger shape: native load refused.

The initial primitive-box shape and a second batch using the original PlatformWater Checkpoint_Trigger.Shape.Gbx did not establish a loadable trigger baseline. Therefore these results do NOT prove that moving checkpoints are impossible, or that the movement attachment itself caused the failure. They identify an earlier native serialization/structure blocker. No trigger crossing, respawn or checkpoint counting was proven. Unsupported candidates stay outside the Items inventory, under Maps/TMISCapabilityResearch; they are not presented as working product exports.

Next experiment: obtain a known playable custom checkpoint item, preserve its exact trigger structure, first prove an unchanged save/load and crossing, then modify only the attachment. Verify crossing at the displaced position AND absence of crossing at the old position. Collision relay success alone is not evidence for checkpoint triggers.

## Audio

The current Audio panel is inspection, not sound-file import or a guaranteed audio emitter.

GBX.NET 2.5 exposes useful structures:

- CGameCommonItemEntityModel: SoundRefSpawn, SoundRefUnspawn, SoundRefGrab, SoundRefSmashed, SoundRefPermanent and SoundLocPermanent.
- CPlugLocatedSound: a CPlugSound reference and location.
- CPlugSound: audio resource, loop/playback/attenuation settings depending on chunk version.
- CPlugSoundSurface: material impact/skid sound configuration; this is distinct from ambient music.
- CGameCtnMediaBlockSound: map MediaTracker audio, a separate authoring workflow.

Potential tools include a positional looping emitter, spawn/contact event sounds and audio dependency inspection. Before exposing an importer, prove an accepted native carrier, supported resource/reference format, actual audible playback, spatial attenuation, and persistence in a shared/embedded map. Merely embedding WAV/OGG bytes or a CPlugSound node does not connect it to playback. No custom item audio playback is claimed in this update. Map music/MediaTracker remain separate alternatives.

## Archetype and inventory location

ArchetypeRef is stored reference/identity metadata, not the disk directory. Empty is valid for standalone custom items. The current ordinary export normalizer may fill an empty reference from the normalized item ID; special flag/opaque export paths have their own preservation rules. An empty UI value is not evidence of a broken item.

Organize custom inventory entries by placing the exported file in Documents/Trackmania/Items and its subfolders. PageName/catalog metadata can affect presentation but do not move files. The field is now labelled Archetype reference with this distinction explained nearby.

## Reproduction and limits

`dotnet run --project Tests/ItemCapabilities -- <cart.Item.Gbx> <output-directory> [checkpoint.Shape.Gbx]` generates enum fixtures and optional trigger research files. `--inspect <file.Gbx>` inventories parsed classes. The audit is intentionally a research tool, not a test that asserts all native candidates work.

Native results are recorded in docs/evidence/item-capabilities-2026-11-01.json. The MCP bridge version was 0.4.0; it remained in Menu for bounded FID loading. No map placement or gameplay result is claimed. Browser verification covers model fitting, all bundled moods without local GameData, direct inspector tabs and existing typed-viewer behavior.
