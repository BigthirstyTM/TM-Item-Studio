// Stable setting keys are attached in Razor. Help is shared by the label and its control.
// Unknown engine fields are described as unverified rather than given invented semantics.
window.studioHelp = (() => {
    const fields = {
        identidfilename: 'Unique inventory identifier and export file name. Use a new ID, such as MyRamp_v2, to keep the original item available in Trackmania.',
        identauthor: 'Author identifier stored with the item. It is part of the inventory identity; preserve it unless you deliberately want a different author identity.',
        identcollection: 'Environment/collection identifier. TM2020 normally uses numeric 26 or Stadium2020. Unchanged values preserve their original encoding; edit only when changing the intended collection.',
        itemtypeeitemtype: 'Classifies the item as Ornament, PickUp, Vehicle, etc. This changes metadata, not its model or trigger structures; selecting Vehicle does not create a vehicle.',
        archetypeinventorypath: 'Stored archetype reference used by the engine to associate an item with another inventory/model definition. Keep the original unless you know the target archetype; files are not packaged by this field.',
        internalitemname: 'Internal model name. When exporting with an Ident.Id, Studio uses that ID for the internal name to avoid inventory-name collisions.',
        copperprice: 'Stored historical copper-price metadata. It does not charge currency in Studio; its effect in current TM2020 catalogs is unverified. Usually preserve the original value.',
        description: 'Description saved with the item. Write a short usage note, for example “32 m ramp; pivot at the entry edge”.',
        pagename: 'Stored catalog page/category label, often Items. Use it to organize metadata; the exact folder shown by the game also depends on the item location and catalog.',
        catalogposition: 'Stored catalog ordering value. Change it when comparing the order of related items; Studio does not simulate native catalog sorting.',
        nbavailablemin: 'Stored minimum availability count. Its effect depends on the game mode and item type and is not simulated here; retain the original for ordinary custom scenery.',
        nbavailablemax: 'Stored maximum availability count. This is not a limit on Studio copies; game-mode behavior must be checked in Trackmania.',
        isinternal: 'Stores the internal-asset flag. It may affect native catalog availability; preserve it for ordinary edits unless testing internal asset behavior.',
        needunlock: 'Stores the item unlock requirement flag. Studio does not manage unlocks or player access; its native effect depends on the catalog/game mode.',
        isadvanced: 'Stores the advanced-item flag used by native item/catalog systems. It does not enable more Studio tools; preserve it unless testing native filtering.',
        iconuseautorender: 'Requests engine-generated icon rendering where supported. It does not regenerate the Studio bitmap; drop an image below to replace the stored icon.',
        skindirectory: 'Stored skin-directory reference. Use the game-relative directory of compatible skin resources; setting it does not copy or embed those files.',
        iconquarterrotationy: 'Quarter-turn metadata for the engine-generated model icon around world Y. It is not a rotation command for the stored bitmap; 1 means a 90-degree model-view turn.',
        defaultweaponname: 'Stored default weapon reference for item types that use a weapon system. Studio does not simulate that system; keep the source value for scenery.',
        defaultcamindex: 'Stored default camera index for engine systems that consume it. Studio viewport views are separate; the native meaning for this item type is unverified.',
        disablelightmap: 'Stores the DisableLightmap flag. Native baked-lighting behavior depends on the item/material; export and compare in a map before relying on a lighting change.',
        iconcplugbitmapdxtbuffer: 'The stored inventory thumbnail. Drop a JPG, PNG or WebP to center-crop it to 64×64 pixels. This replaces only the icon, not the 3D model.',
        groundpointx: 'X coordinate of the placement ground point in meters. Move this anchor to align the item sideways when placing; it does not move mesh vertices.',
        groundpointy: 'Y coordinate of the placement ground point in meters. Adjust the anchor height to make the model meet the placement surface.',
        groundpointz: 'Z coordinate of the placement ground point in meters. Adjust the forward/back anchor while keeping the mesh itself unchanged.',
        paintergroundmarginm: 'Stored ground margin in meters for placement/painter behavior. Compare a small change in the native editor; Studio does not simulate its surface-placement rule.',
        orbitalcenterheightfromgroundm: 'Height of the native orbital-preview center above the ground point, in meters. Increase it to center a tall item in the native placement preview.',
        orbitalradiusbasem: 'Base radius metadata for native orbital preview. Keep special source values such as -1 unless testing a known native preview setup; this does not zoom the Studio camera.',
        orbitalpreviewangle: 'Stored native orbital-preview angle. It affects engine preview metadata, not Studio Top/Front/Right; the exact native unit/range is not verified here.',
        gridsnaphstepm: 'Horizontal placement snap interval in meters. For example, 32 matches a stadium block width. This is exported placement metadata, not the viewport grid size.',
        gridsnapvstepm: 'Vertical placement snap interval in meters. For example, 8 matches one stadium block height. Verify the resulting placement mode in the native editor.',
        gridsnaphoffset: 'Horizontal offset of the placement snap lattice in meters. Use half a step to shift the snapped positions between the ordinary grid lines.',
        gridsnapvoffset: 'Vertical offset of the placement snap lattice in meters. Use it to place a snapping level above or below the ordinary grid origin.',
        flyvstep: 'Vertical step stored for native fly/free-placement mode. Set a known interval and compare the native editor; it does not change the mesh height.',
        flyvoffset: 'Vertical offset stored for fly/free-placement mode. Use it with Fly V Step to shift the placement levels.',
        yawonly: 'Placement rotation flag restricting the intended rotation to yaw around the upright axis. This does not bake a rotation into the model.',
        notonobject: 'Native placement flag governing placement on other objects. Studio stores the flag but does not reproduce the native surface-picking rules; compare it in the map editor.',
        autorotation: 'Requests automatic orientation during native placement. Test on a surface or reference object; Studio keeps its own viewport orientation independent.',
        switchpivotmanually: 'Stores the manual pivot-switching flag. Use multiple PivotPositions to define candidate anchors, then test switching them in the native editor.',
        flagnormalizeplacement: 'Replace the existing placement flags, snap and fly values with the measured vanilla flag values. Enable this for a complete flag conversion; geometry, cloth and textures are not created.',
        waypointtype: 'Gameplay classification such as Start, Finish or Checkpoint. Functional checkpoints also require compatible trigger/collision structures; changing this dropdown alone does not create them.',
        curvedetail: 'Number of preview subdivisions used when sampling the drawn path. More detail can increase the compiled segment count; keep the result within the supported four-segment budget.',
        speedms: 'Requested travel speed in meters per second for path conversion. A 16 m segment at 4 m/s takes about 4 seconds before engine timing quantization.',
        maxsegments: 'Maximum segments allowed when compiling a drawn path. At most four are supported; a closing segment counts too. Simplify the path if conversion exceeds the limit.',
        autoclosepathforsmoothlooping: 'Adds a last segment from the final drawn point back to the start. This consumes a segment and does not automatically create a smooth curve.',
        movingitemtemplateitemgbx: 'Loads an existing moving-item template to supply a supported body structure. It is used for authoring a copy; verify its dependencies and native behavior before sharing the export.',
        constrainttemplateconstraintgbx: 'Loads a supported native constraint template. Use a known compatible constraint; arbitrary class/version layouts must be refused rather than guessed.',
        choosemotiontypepreset: 'Choose the body/constraint layout the assistant will generate. The preview shows intended movement; native collision must be tested separately for each exported layout.',
        presetrelay: 'Uses synchronized world-root bodies for successive path sections. It can show a handover; native vehicle contact and timing are not guaranteed by the browser preview.',
        presethandover: 'Moves one root body, then hands the next section to another parked/occluded body. Provide geometry that hides the handover and test native contact.',
        presetchained: 'Uses a parent/child constraint chain for a continuous visual path. Moving child collision is not established by visual playback; test it before using it as an obstacle.',
        presetsingle: 'Generates motion along one axis. Choose axis, distance and duration, then export a copy and test the moving body in the native editor.',
        presetrotation: 'Generates a rotating body around the chosen axis. Set one loop duration and verify the native pivot and collision before gameplay use.',
        axis: 'Axis used by this motion channel. X and Z are horizontal; Y is vertical. Choose Y for a lift, then set its distance and timing.',
        distancem: 'Travel distance in meters for this generated segment. Negative values reverse the displacement along the selected axis.',
        durationsec: 'Time in seconds for this generated segment. Increasing duration slows travel without changing distance.',
        pathlooptype: 'Choose a return along the same route (ping-pong) or a closed loop. Loop closure must stay within the supported segment count.',
        singlepingpong: 'Makes the motion return along the same axis rather than wrap from the end to the start. Preview the turnaround and verify it in game.',
        rotationaxis: 'Axis around which the body turns. Use Y for a horizontal spinner; X or Z produces a vertical wheel-like turn.',
        durationperloopsec: 'Seconds for a complete generated rotation loop. A larger value rotates more slowly.',
        animationtype: 'Legacy preview animation mode. It changes the simplified viewport demonstration; authored constraint timelines are the source of native motion.',
        rotaxis: 'Axis for the simplified rotation preview. This is separate from detailed serialized constraint bindings.',
        anglemindeg: 'Starting angular bound in degrees. Together with the maximum it defines the rotation range, for example 0 to 90 for a quarter turn.',
        anglemaxdeg: 'Ending angular bound in degrees. A 0-to-360 range represents one full turn; the timeline determines how that range is traversed.',
        perioddurationsec: 'Duration in seconds of the simplified preview cycle. It is not a substitute for the detailed exported motion timeline.',
        phaseoffsetsec: 'Time offset into the simplified preview cycle, in seconds. Use it to preview two motions at different starting phases.',
        transaxis: 'Axis for the simplified translation preview. Use the detailed constraint editor to author native translation.',
        translationdistancem: 'Distance in meters shown by the simplified translation preview. The detailed constraint bounds determine serialized native motion.',
        harmoniceasing: 'Applies a smooth acceleration/deceleration to the simplified preview. Native timeline easing is configured per key in the detailed editor.',
        invertdirection: 'Reverses the simplified preview direction. It does not replace per-key Reverse in the authored timeline.',
        pivotoffsetx: 'X offset of the simplified preview rotation pivot in meters. Use the placement/constraint anchor fields for serialized native anchors.',
        pivotoffsety: 'Y offset of the simplified preview rotation pivot in meters. It changes the demonstration pivot, not the mesh vertices.',
        pivotoffsetz: 'Z offset of the simplified preview rotation pivot in meters. It changes the demonstration pivot, not the mesh vertices.',
        readonlyposition: 'Light position owned by serialized skeleton/socket data. This build can inspect it but cannot move it; color, intensity and radius are editable for supported existing lights.',
        color: 'Color of this existing light, stored on export. Choose a color such as warm orange; the browser lighting is an approximation of the native renderer.',
        intensity: 'Brightness multiplier for this existing serialized light. Increase gradually and compare in the native editor; browser and engine exposure can differ.',
        radiusm: 'Range/radius value of this existing serialized light in meters. It changes light data, not the collision or mesh scale.',
        loadtexturefolder: 'Index local texture files for viewport previews. Nothing is uploaded. This does not embed the folder into the item export.',
        fullmateriallink: 'Game material path for this authored slot. Choose an exact compatible game link; dependencies and native shaders determine its final appearance.',
        x: 'World X translation in meters for this source or group. A zero offset preserves its original imported position.',
        y: 'World Y translation in meters for this source or group. Positive values raise it; the imported mesh coordinates remain intact.',
        z: 'World Z translation in meters for this source or group. A zero offset preserves its original imported position.',
        rx: 'Rotation about X in degrees for this source/group. It changes its transform, not the mesh vertices.',
        ry: 'Rotation about Y in degrees for this source/group. Use 90 for a quarter turn on the horizontal plane.',
        rz: 'Rotation about Z in degrees for this source/group. It changes its transform, not the mesh vertices.',
        px: 'X coordinate of the group pivot in meters. Move the pivot to change the center around which group rotation acts.',
        py: 'Y coordinate of the group pivot in meters. Use it to set the group rotation center at a particular height.',
        pz: 'Z coordinate of the group pivot in meters. Moving the pivot changes the group rotation center.',
        active: 'Enables this group motion. Disable it to compare the rest transform against the animated result; verify exported group motion in the native engine.',
        from: 'Lower/start bound of this motion channel. Translation uses meters and rotation uses degrees; the timeline travels through the configured range.',
        to: 'Upper/end bound of this motion channel. Translation uses meters and rotation uses degrees; negative bounds are allowed where the writer supports them.',
        macroblockname: 'Name used for the exported macroblock file. The macroblock references the item identity, so the item must also exist in the native inventory.',
        copies: 'Number of copies created by Fill grid. It does not change existing instances until you run Fill grid.',
        columns: 'Number of copies per row when Fill grid creates instance transforms. For example, 3 columns with 6 copies produces two rows.',
        spacingm: 'Distance between grid-copy origins in meters. Choose a spacing larger than the item bounds to avoid overlap.',
        meshenabled: 'Preview-only master switch for this mesh part. Disable it to inspect the other parts; it does not delete serialized geometry.',
        meshvisible: 'Preview visibility of this mesh part. Hiding it does not remove the mesh from the exported item.',
        meshcollidable: 'Preview collision-role flag for this part. It does not author or remove native physics shapes; inspect Collision for serialized collision data.',
        mesheffect: 'Preview effect-role flag for this part. It does not create native particles, audio or effect nodes.',
        meshwaypoint: 'Preview waypoint-role flag for this part. It does not create a functional checkpoint or trigger shape.',
        meshmovable: 'Preview moving-role flag for this part. Native motion needs a supported serialized constraint; changing this flag alone does not create one.',
        meshmotion: 'Auto follows the imported motion binding. Static/Kinematic overrides affect export only for an unambiguous entity mapping; mixed modes in one entity are refused.',
        segments: 'Number of functions in this timeline, up to four. Reducing the count removes trailing keys; new keys are one second, linear and forward.',
        duration: 'Length of this timeline key in milliseconds. For duration mode, 1000 means one second for this key; end-time mode stores cumulative endpoints instead.',
        easing: 'How this key interpolates through the range: Linear is constant-speed, QuadInOut eases both ends, and Constant holds a value until the boundary.',
        reverse: 'Reverses the direction through the configured range for this key. Use alternating forward/reverse keys for an out-and-back movement.',
        wholeoffset: 'Moves both constraint anchors together by this many meters on the chosen axis. Their relative separation is preserved.',
        pos1: 'Parent-side constraint anchor in meters in its owning coordinate frame. Change one axis to move the parent attachment point.',
        pos2: 'Child-side constraint anchor in meters in its owning coordinate frame. Change one axis to move the child attachment point.',
        pivotposition: 'Local placement pivot in meters. Select it in 3D or edit its axes to define a native placement anchor; it does not move the mesh vertices.',
        alternative: 'Alternative native placement candidate. Its transform changes where the item can be placed; compare candidate switching in Trackmania.',
        scale: 'Uniform scale for this instance. Native macroblock spawns can store scale; embedded prefab copies cannot and export at scale 1 with a status note.',
    };
    const normalize = value => String(value ?? '').toLowerCase().replace(/[^a-z0-9]/g, '');
    const textOf = element => [...element.childNodes].filter(n => n.nodeType === Node.TEXT_NODE).map(n => n.textContent).join(' ').trim();
    let controlId = 0;
    function bind() {
        for (const label of document.querySelectorAll('label')) {
            const control = label.control || label.querySelector('input,select,textarea') ||
                (label.nextElementSibling?.matches('input,select,textarea') ? label.nextElementSibling : null);
            if (!control) continue;
            if (!control.id) control.id = `studio-setting-${++controlId}`;
            if (!label.contains(control)) label.htmlFor = control.id;
            const key = label.dataset.helpKey || normalize(textOf(label));
            if (fields[key] && !control.dataset.helpKey) control.dataset.helpKey = key;
        }
    }
    function describe(element) {
        if (!element) return '';
        if (element.dataset?.helper) return element.dataset.helper;
        const key = element.dataset?.helpKey;
        if (fields[key]) return fields[key];
        const label = element.labels?.[0];
        if (label) {
            const help = fields[label.dataset.helpKey || normalize(textOf(label))];
            if (help) return help;
        }
        const name = element.getAttribute?.('aria-label') || textOf(element) || element.textContent?.trim() || '';
        const normalized = normalize(name);
        if (fields[normalized]) return fields[normalized];
        if (/segment count/i.test(name)) return fields.segments;
        if (/\b(duration|end time)\b/i.test(name) && /key\s+\d/i.test(name)) return fields.duration;
        if (/easing/i.test(name)) return fields.easing;
        if (/reverse/i.test(name)) return fields.reverse;
        if (/translation (axis|minimum|maximum)/i.test(name)) return fields[/axis/i.test(name) ? 'axis' : /minimum/i.test(name) ? 'from' : 'to'];
        if (/rotation (axis|minimum|maximum)/i.test(name)) return fields[/axis/i.test(name) ? 'rotationaxis' : /minimum/i.test(name) ? 'from' : 'to'];
        if (/^(offset [xyz].*whole constraint)/i.test(name)) return fields.wholeoffset;
        if (/^pos[12] [xyz]/i.test(name)) return fields[name.startsWith('Pos1') ? 'pos1' : 'pos2'];
        if (/^select .*\.item/i.test(name)) return 'Make this loaded source active in the inspector. In Composite mode the other sources stay at their original world positions.';
        if (/^select .*variant|^variant /i.test(name)) return 'Inspect this stored model variant. Only the chosen variant is shown for this source; other variants are preserved on export.';
        if (/^select in 3d$|focus 3d/i.test(name)) return 'Select this pivot, light or constraint in the viewport. Dragging changes only supported editable anchors; light positions remain read-only.';
        if (/^add kinematic entity/i.test(name)) return 'Append a supported dynamic body and constraint using this constraint as the template. The export needs native motion/contact validation.';
        if (/^composite world/i.test(name)) return 'Select source items, edit their world transforms, and create groups without changing their original mesh coordinates. Ctrl/Shift adds followers to the selection.';
        if (/^clear all$/i.test(name)) return 'Remove all explicit instances from this list. It does not remove the source item or its mesh.';
        if (/^clear selection$/i.test(name)) return 'Deselect all composite sources and remove their selection overlays and gizmo. The active document stays open.';
        if (/clear path/i.test(name)) return 'Discard the drawn preview path points. Existing serialized motion remains unchanged until a conversion is applied.';
        if (/^diagnostics/i.test(name)) return 'Inspect parser, missing-dependency and unsupported-layout messages. Open a message before relying on a preview or export capability.';
        if (/^identity$|ident.*collector/i.test(name)) return 'Edit item identity, catalog metadata and the stored inventory icon. Use a unique ID when exporting a separate item.';
        if (/^placement|placement parameters/i.test(name)) return 'Edit native placement anchors, snapping, pivots and alternatives. These change placement behavior without moving mesh vertices.';
        if (/^kinematics|waypointtype.*gameplay/i.test(name)) return 'Inspect or author supported motion constraints and timelines. The preview is not a native collision certification.';
        if (/^lights|dynamic real-time lights/i.test(name)) return 'Edit color, intensity and radius of supported existing lights. Socket-owned positions and adding/removing lights are not supported.';
        if (/^sockets|socket authoring/i.test(name)) return 'Read-only capability information. This parser does not expose editable socket ownership; author sockets with a compatible external tool.';
        if (/^collision|phymodel/i.test(name)) return 'Inspect serialized collision shapes and their support status. The panel does not add/remove native collision or prove vehicle contact.';
        if (/materials.*lod|vismodel/i.test(name)) return 'Inspect visual meshes, materials, UV data and LOD. Load textures for a preview and inspect each control to distinguish preview-only edits from exported changes.';
        if (/^audio|cplugfx/i.test(name)) return 'Inspect audio, effect and trigger node presence. Unsupported formats are diagnostic only; this panel does not create those native systems.';
        if (/^macroblock|item instancing/i.test(name)) return 'Create explicit repeated instances. Macroblocks reference an inventory item; multi-instance items embed shared geometry. Source positions are unchanged until you create copies.';
        if (/flag item/i.test(name)) return 'Compare against measured vanilla flag tags and placement. Conversion exports a separate copy; it does not create cloth, a pole or textures.';
        if (element.getAttribute?.('title')) return element.getAttribute('title');
        const actions = [
            [/original motion in viewport/i, 'Switch the viewport to the imported motion settings for comparison. This changes the preview; it does not rewrite the stored timelines.'],
            [/choose local texture folder/i, 'Choose an extracted texture folder on this computer. The viewport matches textures to material links; files stay local and export bytes are unchanged.'],
            [/^wire$/i, 'Toggle wireframe edges in the viewport. This is preview-only and does not alter the exported geometry.'],
            [/^reset camera$/i, 'Frame the current visible item bounds and restore the normal world-up camera orientation.'],
            [/^\.obj$/i, 'Download visible geometry as OBJ for inspection in another 3D tool. Native item metadata, triggers and motion are not an OBJ feature.'],
            [/^(neutral|morning|day|evening|night)$/i, 'Change the viewport lighting mood. This is a visual approximation and does not set the exported map mood.'],
            [/light.*dark|switch.*theme/i, 'Switch the Studio interface color theme. It does not change item materials or game lighting.'],
            [/home|return to home/i, 'Return to the import screen and clear this browser workspace. Export edits first if you want to keep them.'],
            [/export selected file/i, 'Save the active item as GBX using its supported edits. Read any export warnings and check native behavior for motion or physics changes.'],
            [/download debug report/i, 'Download local diagnostic information including model names and referenced filenames. Review it before sharing; nothing is uploaded.'],
            [/fill grid/i, 'Replace the instance list with Copies arranged using Columns and Spacing. This is an explicit instance tool; import positions are not rearranged.'],
            [/add copy/i, 'Append one explicit instance of the active item, then edit its transform.'],
            [/duplicate/i, 'Append a copy of this instance and its transform. Shared geometry is reused.'],
            [/clear (all|path|selection)/i, 'Clear the named workspace selection, drawn path or instance list. Inspect which section this action belongs to before using it.'],
            [/create group/i, 'Group the selected composite sources under one transform. The first selected source is the leader; source mesh coordinates are preserved.'],
            [/ungroup/i, 'Remove selected sources from their composite group while retaining their world placement.'],
            [/connect pivots/i, 'Align selected follower pivots to the leader pivot. Select the leader first, then add followers with Ctrl/Shift.'],
            [/^move$|^rotate$/i, 'Choose translation or rotation for the active transform gizmo. Select an item/group before dragging the corresponding handles.'],
            [/pivotposition/i, fields.pivotposition],
            [/alternative/i, fields.alternative],
            [/start drawing/i, 'Record path points with viewport clicks. Click item geometry or the ground plane, then convert a path with at most four segments.'],
            [/convert path/i, 'Compile the drawn points into supported motion constraints. Simplify the path if the four-segment limit is exceeded; native contact needs testing.'],
            [/convert.*kinematic/i, 'Create a supported dynamic body and constraint on this item, deriving collision where possible. Export a copy and verify its native motion/contact.'],
            [/export.*macroblock/i, 'Download native instance references. The referenced item ID must be available in the game inventory.'],
            [/export.*multi-instance/i, 'Download one item with shared embedded source geometry and an entry per instance. Unsupported instance scale is reported rather than silently promised.'],
        ];
        return actions.find(([pattern]) => pattern.test(name))?.[1] || '';
    }
    return { fields, bind, describe };
})();
