# Path authoring and projects

Version 0.5.17 adds four tools in the Motion/paths editing stage.

## Bezier handles

Create route points first, then expand Bezier handles. Enable handles and drag the incoming/outgoing coloured controls in the viewport. A locked top/front/side view makes dragging easier. Linked handles mirror each other; disable linking for independent tangents. Release the handle to update the animation preview. Undo/redo includes handle edits.

Detail controls how many straight native motion segments approximate each curve. More detail improves the approximation but uses the existing 128-segment limit and minimum segment duration/distance. Waypoint pauses must be zero while generating Bezier curves. Closed routes use cyclic tangents. Project files retain handles; ordinary route drafts contain the sampled route.

## Look-at target

Enable a fixed XYZ target or use the centre of the selected mesh/reference. The yellow marker identifies it. The model's local +X axis points toward the target at each motion sample, with orientation interpolated between samples. Array followers recalculate their own direction. This is a baked fixed target, not tracking another moving game object. Roll remains editable. Targets lying on the route are refused. Existing collision rotation limitations apply; sampled collision tilt/layers cannot combine look-at with spatial array spread.

## Clearance

Check full route compares animated followers with each other and with the selected reference part. Positive margin requests extra spacing; zero checks contact; negative margin allows some overlap by shrinking the checked envelopes. Sample count controls temporal coverage. Select a reported conflict to scrub to its time.

The test uses world-aligned visual bounding boxes and reference triangles, not native physics collision meshes. Rotation can make these boxes conservative; contacts between samples can be missed. A negative margin is an envelope allowance, not an exact penetration measurement. Recheck after changing the route, model or reference. Large checks are bounded to keep the editor responsive.

## Project files

Save project writes a `.tmisproject` ZIP containing the selected item, editable motion source when applicable, route settings, original control points/handles, look-at, array/layer settings, reference guide geometry, clearance preferences and viewport/playback state. Open project restores the draft; use Apply path before exporting regenerated motion.

Projects save the selected document rather than all unrelated open items. External textures/GameData dependencies are not bundled. Reference geometry is a guide snapshot. Applied custom-template settings travel with the route, but the local template library is not replaced. Keep the exported Item.Gbx for game use; the project is an editor file.

## Verification

Automated model tests cover curve sampling, limits, look-at angles, shifted followers and native serialization. The browser roundtrip exercises actual handle dragging, undo/redo, look-at, clearance, reference offsets, Apply, project save/open, malformed-file refusal, reapply and GBX export. These features have not received a new in-game physics verification.
