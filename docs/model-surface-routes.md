# Route along a model

Open the moving item, then expand **Route along a model** under Gameplay & Kinematics. Load a separate reference .Item.Gbx (for example a track), or select a viewport mesh and capture it. The reference is blue and is never added to the exported item. Choose a connected rail/deck part with the searchable selector. Offset X/Y/Z aligns it with the moving item; Frame reference fits the camera.

Enable Draw / drag points on the reference surface. Left-click creates guide points; dragging keeps them on the selected surface. A ray that misses the mesh does not move a point. Disable surface drawing to return to the normal drawing plane. Snap current points projects all points to the closest triangles, refusing the whole operation if any point exceeds the chosen snap distance.

For automatic routing, draw a start, end and optional intermediate guide points, then choose Generate route along mesh. The tool projects guides to the surface, chooses nearby vertices and finds the shortest connected edge path between them. UV/material seams with coincident vertices are welded at 0.1 mm precision. Disconnected components are never joined through air. Close route adds a return to the first guide point; for a circuit, place guide points around the circuit to distinguish it from a shorter return path.

This does not infer the semantic centreline of arbitrary rails. Supports, intersections and wide decks may provide undesired shortcuts. Select an isolated rail/deck part and add guides to disambiguate. The result follows mesh edges and may zigzag. Approximation removes points within the chosen tolerance and can cut corners or leave a narrow surface: inspect and correct the preview. Zero tolerance preserves bends. At most 128 native segments are accepted; dense routes need a larger tolerance or a simpler reference. Native minimum segment length/time and collision limitations still apply.

References support decoded embedded visuals (first variant for variant items), at most 500,000 source vertices total and 100,000 vertices / 200,000 triangles per part. Unsupported or externally unresolved geometry requires a simpler embedded reference. Reference position is a guide transform, not an edit to either GBX file.

Preview/edit the generated route and use Apply path followed by Export. Save route draft preserves the generated route; the reference itself must be reloaded in a later session. No in-game passenger-safety claim is made by this geometric authoring feature.
