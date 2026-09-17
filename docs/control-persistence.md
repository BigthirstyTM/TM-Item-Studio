# Control persistence audit

Audit baseline: `ef0e070`, `Pages/Home.razor` and `wwwroot/js/meshViewer.js`. The table inventories every editable control and action in those files, including duplicate axis selectors and mouse interactions. It describes the baseline; `Models/ItemEdits.cs` adds the supported operations below but does not itself change the UI. Serializer evidence is from the original bundled GBX.NET DLL (SHA256 `09f87f2c447e4cadd7539f749acc79b73b3f3575cbadc91fcf967e93e5e8d30c`). Synthetic regression fixtures use that DLL directly.

Capability-gating update (`fix/capability-gate-preview-controls`): the preview-only controls this audit flagged are now disabled, read-only, or removed in the UI, as recorded in the **Current UI** column/notes below. Light color/intensity/radius editing, pivot editing, and authored-constraint editing are unchanged and remain the only persisted light/motion writes.

“Persistent field” means an actual serializer field exists, **conditional on its chunk/version being present**. The old setters do not ensure that chunk exists or validate the document's representation. A setter succeeding in memory is not proof that export will retain it. Game behavior is not verified here.

## Item metadata and placement

`Collector` means `CGameCtnCollector`, the base of `CGameItemModel`; `Item` means the latter. Hex IDs identify serializer chunks, with H denoting header chunks.

| Control / source binding | Actual persisted field and serializer | Baseline behavior / integration requirement |
|---|---|---|
| Identifier, Author, Collection | `Collector.Ident` (Id, Author, Collection), H2E001003 / 2E00100B (legacy 2E001002) | `UpdateIdent` constructs all three at once but silently catches errors. Validate, display failures, retain original on failure. |
| Item Type | `Item.ItemType`, H2E002000 | Direct enum setter, **not** graph conversion. Other representation uses `ItemTypeE` in 2E002015. Disable incompatible type changes unless graph compatibility is established. |
| Name | `Collector.Name`, H2E001003 version >=7 / 2E00100C | Persistent field, chunk dependent. |
| Copper Price | `Collector.CopperPrice`, H2E001003 versions 3..5 / legacy 2E001007 | Persistent field; absent from modern version-8 header. HTML min is not model validation. |
| Description | `Collector.Description`, 2E00100D | Persistent field, chunk dependent. |
| Page Name | `Collector.PageName`, H2E001003 / 2E001009 | Persistent field, chunk dependent. |
| Catalog Position | `Collector.CatalogPosition`, H2E001003 version >=3 (Int16) / legacy 2E001007 (Int32) | Validate storage width for actual chunk; setters do not create chunks. |
| Available Min / Available Max | `Collector.NbAvailableMin`, `NbAvailableMax`, H2E001003 versions 3..5 (Byte/Int16) / legacy 2E001007 (Int32) | Absent from modern version-8 header; validate width, nonnegative/order as product policy. |
| Internal Item | `Collector.IsInternal`, 2E001011 / legacy 2E001007 | Persistent field, chunk dependent. |
| Need Unlock | `Collector.NeedUnlock`, legacy 2E001007 | No modern serializer inferred from the UI. |
| Advanced Item | `Collector.IsAdvanced`, 2E001011 | Persistent field, chunk dependent. |
| Auto Icon | `Collector.IconUseAutoRender`, 2E00100E | Persistent field; do not imply icon regeneration occurs. |
| Icon Y quarter rotation | `Collector.IconQuarterRotationY`, 2E00100E | Persistent field; validate 0..3 in code. |
| Skin Directory | `Collector.SkinDirectory`, 2E001010 | Stores a string; does not package dependencies. |
| Default Weapon | `Item.DefaultWeaponName`, 2E002019 version >=3 | Persistent ID field, not proof of gameplay support. |
| Default Camera Index | `Item.DefaultCamIndex`, 2E002006 | Persistent field, chunk dependent. |
| Disable Lightmap | `Item.DisableLightmap`, 2E00201F version >=6 | Persistent field only at supported version; does not regenerate lightmaps. |
| Icon upload | `Collector.Icon` / `IconWebP`, H2E001004 | JS crops/resizes to 64x64; C# assigns both. Serializer prioritizes WebP when nonnull. Ensure existing header support, validate decode and captured upload target; no chunk is created by old handler. |
| Ground Point X/Y/Z | `Item.GroundPoint`, 2E002012 | Handler writes one vector component; baseline accepts nonfinite parsed floats. |
| Painter Ground Margin | `Item.PainterGroundMargin`, 2E002012 | Persistent scalar, chunk dependent. |
| Orbital Center Height | `Item.OrbitalCenterHeightFromGround`, 2E002012 | Persistent scalar, chunk dependent. |
| Orbital Radius Base | `Item.OrbitalRadiusBase`, 2E002012 | Persistent scalar, chunk dependent. |
| Orbital Preview Angle | `Item.OrbitalPreviewAngle`, 2E002012 | Persistent scalar; baseline UI does not specify angular units. Do not infer degrees from the display. |
| Horizontal/Vertical Grid Step | `DefaultPlacement.GridSnapHStep`, `GridSnapVStep`, 2E020000 | Persistent floats when placement/chunk exist. |
| Horizontal/Vertical Grid Offset | `DefaultPlacement.GridSnapHOffset`, `GridSnapVOffset`, 2E020000 | Persistent floats when placement/chunk exist. |
| Fly Vertical Step / Offset | `DefaultPlacement.FlyVStep`, `FlyVOffset`, 2E020000 | Persistent floats when placement/chunk exist. |
| Yaw Only, Not On Object, Auto Rotation, Switch Pivot Manually | `DefaultPlacement.Flags` bits 1, 2, 3, 4 respectively, 2E020000 | Setters preserve other bits; no inferred placement class rewrite. |
| Pivot X/Y/Z and pivot drag | `DefaultPlacement.PivotPositions`, paired with `PivotRotations`, 2E020001 | Old `SyncPivotsToGbx` replaces only positions, invents origin for empty source; use validated operation-specific methods below. |
| Add Pivot / Remove Pivot | Same two arrays, 2E020001 | Old deletion omits matching quaternion and forbids index 0. New paired operations support first/last/middle deletion and zero pivots. |
| Waypoint Type | `Item.WaypointType`, 2E00201F | Direct enum setter; collision/trigger graph is not constructed. |

Archetype is explicitly readonly. Visual/LOD, extras, original-motion property listing and node-information panels contain no other editable model controls. Richer placement fields (`CubeCenter`, `CubeSize`, `PivotSnapDistance`, placement-class/group/layout data) remain preserved and are **not** exposed comprehensively by this slice.

## Motion, lights, sockets, physics

| Control | Baseline write path | Required behavior | Current UI (capability gating) |
|---|---|---|---|
| Convert To Kinematic / Convert To Static | Tries nonexistent enum names; toggles moving DTO flags and arbitrary last part | Unsupported conversion. Disable/label; never report a saved conversion. | Buttons disabled with explanation; handlers removed; "trajectory nodes" promise removed. |
| Rotation axis (toolbar and gameplay selectors) | `OnAxisChanged` -> reflected `RotAxis`, then JS axis | Actual `NPlugDyna_SKinematicConstraint.RotAxis` exists; typed motion integration must verify target, axis enum and supported mode. | Unchanged; authored constraints edit real `RotAxis`/`TransAxis`. |
| Animation Type (spin/oscillation) | Preview bool; may write default -45/+45 via shared callback | No direct persisted spin/oscillation bool. Replace with supported typed timeline mode, not preview-only conversion. | Unchanged legacy preview for items without authored constraints; labeled as viewport-only. |
| Min Angle / Max Angle | Reflected `AngleMinDeg`, `AngleMaxDeg` | Real scalar fields in degrees; no validated bounds/target contract in old UI. T02 owns typed edits. | Unchanged; authored-constraint editor binds `AngleMinDeg`/`AngleMaxDeg`. |
| Period / Phase | Attempts constraint `Period`, `Duration`, `Phase`, `PhaseOffset` | These properties do not exist on that constraint. Preview-only old controls. Instance timing belongs to typed params; key durations to animation subfunctions. | Unchanged legacy preview; segment durations write real `SubAnimFunc.Duration`. |
| Translation Axis | Reflected `TransAxis` | Real enum field; use T02 validation. | Unchanged. |
| Translation Distance | Attempts `TransDist`, `TranslationDistance` | Neither exists on actual constraint. Real values are scalar `TransMin` / `TransMax`; old UI only previews distance. | Unchanged legacy preview; authored editor binds `TransMin`/`TransMax`. |
| Harmonic Easing / Invert Motion | DTO/JS booleans, no source write | Replace with supported independent timeline easing/reversal; no global persistence claim. | Unchanged legacy preview; segment editor writes real per-function `Ease`/`Reverse`. |
| Pivot Offset X/Y/Z (motion) | DTO/JS only | Separate from placement pivots; owner constraint entry transform must be edited explicitly by typed motion adapter. | Unchanged legacy preview. |
| Light color | `SyncLightsToGbx`: RGB HTML bytes /255 -> `CPlugLightUserModel.Color`, 090F9000 | Supported existing model only. Old extraction quantizes/clamps HDR; retain source floats unless deliberately edited. | Unchanged and editable. |
| Light intensity | `CPlugLightUserModel.Intensity`, 090F9000 | Supported existing model; zero valid. Old extraction replaces zero with 2. | Unchanged and editable. |
| Light radius | `CPlugLightUserModel.Distance`, 090F9000 | Supported existing model distance; zero valid. Old extraction replaces zero with 15. Preview radius is not a physics hull. | Unchanged and editable. |
| Light X/Y/Z and light drag | DTO only; same callback rewrites unrelated light color/intensity/distance | No `Position` on `CPlugLightUserModel`. Require explicit owning entry or certified transform mapping; otherwise disable movement. | Read-only display inputs; gizmo remains selectable but never draggable (`editable:false` payload flag); `OnGizmoMoved` light branch and `updateLightRealtime` position args removed. |
| Add Light / Remove Light | Adds/removes only DTOs | Unsupported authored graph mutation. Disable/label; removing a preview light does not remove source. | Buttons and handlers removed; panel labels "existing serialized lights only". |
| Socket tag | DTO text only | No persisted field or write path. Unsupported. | Removed; sockets tab is a read-only capability note. |
| Socket X/Y/Z / drag | DTO/JS only | `DefaultPlacement.Sockets` property does not exist in bundled API. Unsupported. | Removed; socket gizmos, drag branch, `updateSocketRealtime`, and socket layer removed. |
| Add Socket / Remove Socket | DTO list only | Unsupported authored socket topology. Disable/label. | Removed; `SocketDto` and extraction reflection deleted. |
| CollisionEnabled checkbox | No bind/event handler at all | Unsupported toggle; replace with readonly diagnostic. Node substring detection does not establish collision capability. | Replaced with read-only `ItemScene` collision inventory (representation/state/path); no user toggle. |

## Viewer and document actions

| Interaction | Persistence classification |
|---|---|
| File upload | Parses documents; no user field write. Baseline root/variant extraction drops null entry views. |
| Variant selection | Baseline wrongly replaces `Item.EntityModel` with selected root. Integrate the document-preserving selection module before export. |
| GBX Export | Baseline re-syncs pivots/lights, then reconstructs multi-file variants with empty tags/version 0 and filters non-prefabs. Replace with original-document save; never flush lossy preview DTOs. Combine must be explicit with metadata source/dependency checks. |
| OBJ Export | Downloads extracted geometry text, no GBX mutation. Baseline lacks normals/UVs and current pose; material/geometry integration owns extensions. |
| Mesh / Pivot / Light visibility | Preview only, `toggleLayer`; source remains unchanged. Socket layer removed (no socket gizmos exist). |
| Play/Pause; original-motion toggle | Preview only (`setAnimationPlaying`, `setMotionPreview`); never revert/rewrite saved model. |
| Wireframe; Reset Camera; orbit/pan/zoom | Preview only; not authored transforms. |
| Gizmo focus buttons, pointer selection | Select preview object / open tab only. Drag invokes `OnGizmoMoved` for pivots; light gizmos are selectable but never draggable. |
| JS `setAnimationSpeed` | Preview-only API, no rendered speed control in baseline. |
| JS `updateLightRealtime` (color/intensity/radius only), `updatePivotRealtime`, `renderStudioScene` | Rendering helpers, not independent persistence mechanisms. `updateSocketRealtime` and the light-position realtime path were removed. |
| Sidebar tabs | UI state only. |

## Supported module and exact integration contract

`ItemEdits` lives in `TM_Item_Studio.Models` and accepts explicit typed source/owner handles. It never traverses/resolves a graph, accesses private fields, changes root ownership or clones nodes. Source identity is distinct from occurrence path. UI must show `SharedSourceScope`; for entry movement also show `OwnerPositionScope` because moving an owner moves all its contents. A repeated prefab entry edit affects every occurrence of that same entry; moving one of two distinct entries leaves the other unchanged even when their model is shared.

- `ReadPivots(placement)` returns copies of valid position/quaternion pairs, with no synthetic origin. `AddPivot`, `EditPivot`, `RemovePivot` operate on paired arrays and preserve the untouched order/orientations. Supplied quaternions are finite unit X,Y,Z,W (tolerance 0.0001 squared length); no normalization is silently applied. New origin pivot requires an explicit identity quaternion from UI. A valid zero-pivot source can gain its first pair; deleting the last pair yields two empty arrays. Existing opaque pivot chunks are refused.
- `EditPivotPosition(placement, index, localPosition)` is the coordinate/drag path. It changes one position and preserves the entire original rotation array reference and contents, including missing/mismatched rotations. Show that pairing is unavailable and disable structural/rotation operations in that case; do not block independent coordinate editing. `ReadPivots` failure must not hide the raw existing positions.
- `EditLight(source, color, intensity, distance)` validates every input before writing three fields. RGB allows finite nonnegative HDR values; intensity/distance allow zero. Existing serializer chunk/version/unknown values, emission parameters and `NightOnly` are preserved. Use separate model values and color-picker display values; never run this method during export merely to flush an unchanged HTML color.
- `EditOwnerPosition(prefab, entry, localPosition)` validates owner membership and unit entry rotation, then edits only `EntRef.Position`. Do not describe this as independent light movement when the owner contains meshes/other lights. Convert world gizmo coordinates through the actual inverse parent transform, excluding viewer framing, before calling. Reject unsupported/noninvertible coordinate spaces in the caller.
- `EditLightTransform(solid, lightRecord, localPosition)` edits only TX/TY/TZ of an explicit `CPlugSolid2Model.Light.U05` matrix, preserving its 3x3 basis and unknown fields. The record must be in `solid.Lights`, serialized by 090BB000 version >=8, with finite nonsingular matrix. This is **not** a resolver from `LightInst` to a socket. `LightInst.ModelIndex` / `SocketIndex` are preserved; available API does not establish the mapping to private skeleton socket/joint arrays. Expose a position control for a user-model light only if discovery independently certifies the owner relationship; otherwise label position unsupported. Never index `Lights` using `SocketIndex` as a guess.
- `EditVariant(list, variant, tagChanges, hidden?)` patches keys (null removes; empty values allowed), preserves unmentioned keys and sibling variants, and never touches `EntityModel` / `EntityModelFile`. Version 0 permits tag edits but rejects any hidden-flag request without applying tags or upgrading version; version 1 permits both. Unknown list versions fail. New controls must retain explicit list/variant handles, show hidden-flag capability and apply tags/hidden together only when supported. Existing tagged/hidden variants remain intact through normal document export.

All methods validate before source assignment and throw `ArgumentException`/`NotSupportedException` on refusal. Catch those at the UI boundary, show the message and reload rejected preview values from source. Treat persistence capability as operation-specific, not a single “editable” boolean. Save does not run edit operations. Missing placement/light records remain absent rather than being created through a fallback DTO.

Opaque pivot refusal includes a recognized `Chunk2E020001` with non-null `Data`, as can result from `SafeSkippableChunks` partial recovery. GBX.NET serializes this raw buffer instead of interpreted properties. All four pivot mutation methods refuse it without clearing raw bytes, replacing arrays, or changing chunk membership; a successful read of interpreted positions is not sufficient evidence that they are writable. The other edit targets use non-skippable `Chunk090F9000` / `Chunk090BB000` or direct prefab/variant serialization, so this specific typed retained-buffer path does not apply to their own serializers. Discovery must still certify reachability through serializable owner edges before offering any source handle for editing.

## Verification and limits

Run `DOTNET_ROLL_FORWARD=Major dotnet run --project Tests/ItemEdits -m:2` (under the shared build lock during coordinated work). The standalone net8.0 project links production module sources and uses the unchanged bundled DLL. Synthetic source/save/reparse assertions cover zero/paired pivots, middle/last deletion, absent and mismatched rotations, invalid inputs and byte-identical rejected writes, shared light owners, owner transforms, unknown fields, tags/manual-cycle flags and shared entity topology. No private assets are required.

Raw-data regressions cover all pivot operations with a typed retained buffer and an actual safe-recovered malformed rotation array. They assert refusal, stable source/chunk references, byte-identical export, and preserved save/reparse results.

This module does not wire the UI, certify in-game light/placement behavior, expose skeleton sockets, add/remove lights, convert physics/kinematics, regenerate collision, or claim persistence for legacy/general controls whose chunks are absent. Those limits must remain visible after integration.
