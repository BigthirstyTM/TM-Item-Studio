# Experimental collision arrays and layers (0.5.13)

Choose the experimental collision relay path mode, then open Array along path and Animation layers. Arrays support timing offsets, XYZ spread, shared scale variants, preview scrubbing and draft Undo/Redo. Save these settings in a custom movement template.

Every copy retains the source collision mesh, including its scale. A source with no useful collision cannot acquire it from this feature. Equal-sized visual meshes are shared, but collider branches and animation schedules add file size and runtime work.

Physical Translate, Rotate and Spin layers and Sample route tilt into collision are experimental options, available without an acknowledgement gate. The compiler samples the route into at most 128 segments. Position moves along straight segments; orientation changes in steps at handoffs. Physical layers include route orientation and restart their clocks each lap. A layer period that does not divide the lap can produce a seam. Increasing samples is not a guarantee of safe passenger transport.

**Warning:** cars can slip, fall through handoffs or launch violently. Tests include a 63.5 m/s upward launch with a translation layer and lost passenger support in a longer array run. Do not treat these as reliable passenger platforms.

Moving wings, wheels and other parts are visual animations; the original complete collider stays rigid on the root. Collision does not articulate separately with those parts.

These settings are available to compatible template routes, not every possible configuration. Arrays and physical layers require a loop and zero waypoint pauses. Near-vertical relay segments, microscopic carrier-only sources and exports exceeding geometry/timing budgets remain unsupported. Straight obstacle mode keeps its single native driver. Use scenery mode for unrestricted visual motion without passenger collision.

Native evidence and exact fixture hashes: `evidence/collision-arrays-2026-11-01.json`. Short segment tests establish collision presence, not safe full-lap passenger transport.
