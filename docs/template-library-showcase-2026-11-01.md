# Template library and native showcase (0.5.11)

The searchable built-in list now contains thirteen profiles: Rollercoaster, Bird flight, Car body, Boat, Train, Lift/platform, Cable car, Drone/helicopter, Fish, Butterfly, Robot, Spinning ride and Conveyor. The new station profiles use native QuadInOut movement with a configurable wait at every route point. Cable car stays upright with sway; fish adds yaw oscillation; butterfly varies speed and bank; robot adds walking sway; ride adds whole cabin turns. Separate existing mesh parts provide wings, tails, limbs, wheels and rotors. These profiles author animations; they are not physics simulations or automatic mesh recognition.

Sway templates add points on existing straight segments when the input is too sparse to represent the cycles. This preserves route geometry, stays within 128 segments and does not round corners. The effective export must retain the 80-ms minimum per leg. Vertical scenery movement now has a nonsingular orientation basis; the existing vertical collision-relay refusal remains.

## Custom library

Under My movement templates, enter a name/category/description and save the current edited draft. Storage is per browser/origin, up to 100 templates. Duplicate names within a category are refused. Export JSON for a portable backup; import is limited to 1 MB and validates the version and motion plan before storage. Storage failures are visible.

Choose which speed, rotation, timing and part settings to save. Without a route, point values are interpolated by normalized cumulative distance onto the target route; rotation offsets are relative to its direction. Total time and fixed waits can be retained. Including the route restores the complete source plan. Cross-mode profile application is refused. Part indices/pivots are not universal: reusing them is explicit, otherwise configure the new model manually. Saved library data is not sent to a server.

## Tests and evidence

- All repository suites passed; final ItemMotion checks: 62 passed, zero failed.
- Browser matrix individually exercised all thirteen presets and a custom example: selection, apply, moving preview, custom save/apply, persistence, JSON export/delete/import and native item export.
- Compiler tests cover every preset, serialization, exact vertical scenery versus refused vertical relay, sparse-route sway sampling, custom interpolation on a different point count, orientation adaptation, full-route restore and invalid metadata/version.
- Fourteen browser-exported procedural demo items were loaded and placed in one new native Trackmania map. The 268-second recording allocates at least one full cycle to each item. Frame samples show wing, rotor, tail and limb changes. A 53.2-second compilation labels each example. No interpolation or simulated game frames are used.
- Native saving initially wrote small item-reference entries. The delivery map replaces those entries with the complete browser-exported item graphs, retaining the native identities and all non-ZIP map bytes. Verification reparses all fourteen embedded prefabs. The delivery map was reopened in Trackmania, all fourteen placements read back, and played again. Loose demonstration files remained installed during this reopen; independent clean-machine loading is not claimed.

This is a scenery showcase map, without start/finish or a race validation. It demonstrates animation, not passenger collision, train coupling, tyre physics or skeletal walking. Wheels still use mean segment speed and can reset phase at the loop seam. Models are deliberately simple editable demonstration shapes.

## Files and reproduction

Local evidence/delivery directory: `artifacts/template-gallery`.

- `Template-Showcase.mp4`: 53.2-second labeled compilation.
- `TMIS_Template_Showcase.Map.Gbx`: complete embedded models, native placements.
- `TMIS_Demo_*.Item.Gbx`: fourteen browser-exported items.
- `Sources/`: individual editable procedural source models.
- `Templates/`: editable route drafts and custom-template examples.
- `Template-Showcase-Package.zip`: clean delivery package; excludes raw runtime logs and account telemetry.

Generate geometry with `dotnet run --project Tests/CollisionHandoffVariants -- gallery artifacts/handoff-cart/fixtures/TMIS_Cart_Source.Item.Gbx artifacts/template-gallery`, then run `Tests/Browser/template-library.cjs` against a freshly built local Studio. The browser matrix overwrites the generated candidates with real UI exports. The native runner accepts `--gallery-file` and `--shot-file`; it starts only from the game menu and uses a new disposable map. Native inventory requires a game restart when a newly created item folder is not picked up.

Install item files under Documents/Trackmania/Items/TMIS_Template_Showcase, source files in a separate folder if desired, and the map under Documents/Trackmania/Maps. Open the map in the editor and enter test mode to watch the animations.
