# Drawn routes using the single visible collision platform

## Result

The native single-platform experiments establish collision-preserving motion
for specific translation/rotation schedules. They do not establish a general
drawn-route compiler. The next useful product is a **constrained route drawing
mode**, with truthful preview and explicit refusal or approximation when a route
does not fit. A freehand route must never silently become a different circle.

This follow-up adds a reproducible geometric inverse experiment. It does not
write GBX, modify the public editor, or introduce a new ingame success claim.
Native evidence remains the repeated circle and two-line/two-arc fixtures in
[the preceding report](single-visible-collision-research-2026-10-31.md).

## The distinction that matters

A horizontal platform with geometry offset by radius R from its rotation pivot,
and a pivot that slides on one axis, has a centre trajectory of the form:

```
x(t) = slide(t) + R sin(angle(t))
z(t) = centreZ  + R cos(angle(t))
```

These are local coordinates in a chosen planar frame. Native angle sign and
frame must be calibrated before an exporter uses this mathematical convention.
For a planar drawing within a strip narrower than 2R, an inverse is:

```
angle(t) = acos((z(t) - centreZ) / R)
slide(t) = x(t) - R sin(angle(t))
```

Thus one slide plus one rotation is **geometrically sufficient** for many more
routes than circles. It is not sufficient with arbitrary restrictions on the
two signals. The principal acos branch is continuous within the strip and
returns to its initial angle on a closed route; it also ties the platform's
orientation to its position. Facing the tangent is an additional requirement
and is generally not independently available through these same two channels.

The proof uses R greater than half the transverse extent, avoiding the acos
derivative singularity at the strip boundary. Its margin is numerical, not a
tested collision/speed safety bound. Sharp corners require stopping or rounding.
The analysis is 2D; it does not solve arbitrary changing-height 3D routes.

## Why this cannot simply become native keys

The inspected GBX.NET definition of `NPlugDyna_SKinematicConstraint` has one
TransMin/TransMax pair and one AngleMinDeg/AngleMaxDeg pair per constraint.
Each SubAnimFunc stores **Ease, Reverse, Duration**. It has no independent target
position or target angle. The tested engine path uses at most four keys per
channel; a longer serialized array is not proof of native support.

Consequently, adding drawn points does not add new independently selectable
physical positions. Each ordinary monotonic move traverses the same global
range, or its reverse; a constant key holds an endpoint. The schema also offers
Back/Elastic/Bounce easing variants, so counting direction changes alone is not
a mathematical proof of impossibility. Those variants still do not encode an
arbitrary lookup table. ShaderTcAnimFunc is texture selection, not a hidden
physical path channel.

Concrete example: draw (0,0)→(32,0)→(32,24)→(0,24)→(0,0), choose R=15 and
centreZ=12. The bottom straight needs slide=-9→23; the top straight needs
23→-9. But halfway along the right vertical edge slide must be 17, and halfway
along the left edge -15, to cancel the rotational horizontal displacement.
The overall range becomes -15→23; the bottom leg starts at an interior value
(-9), which an ordinary full-range forward/reverse key cannot express.
The required angle is an acos function of the drawn Z coordinate. Arbitrary
subdivision of the drawing does not remove either mismatch.

The previously successful CurvedLoopCage avoids that problem by intentionally
following arcs during the crosswise travel. It does **not** trace the original
rectangle. Its two pivots lie on the same slider line, share a radius and use
one global angular range. Its opposite arc directions are not a conventional
rounded rectangular racetrack. A generic "smooth corners" operation therefore
cannot be assumed to compile to this fixture.

## Practical routes forward

1. **Constrained drawing using the measured native mechanism.** Draw the main
   axis/endpoints and drag width/radius handles. Show the actual two-line/two-arc
   route and platform orientation live. Derive geometry offset, translation
   range, angular range and segment durations from those handles. Initially
   retain the measured easing and conservative dimensions/speeds. Arbitrary
   axis orientation, dimensions and speed changes still require native tests.
   This is a useful real drawing tool, with fewer independent controls than a
   free polygon.
2. **Fit a free drawing to a supported route and show the deviation.** Preserve
   the original drawing, overlay the export route, and report distance error
   along the entire route (both directions), not just at the control points.
   Check timing, orientation and passenger clearance separately. Require an
   explicit user choice to accept approximation; do not label it exact follow.
   The current inverse analyzer is not yet this fitter.
3. **Exact arbitrary drawing needs a new physical signal mechanism.** Candidate
   research includes whether a nonzero physical local animation can compose
   with a world-root constraint, or another native animation consumer offers
   freely sampled transforms. Previous zero-local-animation/child-chain tests
   do not answer that specific composition question. Test one variable on a
   single body before building a new compiler. Extra constraints on one body,
   scenery parent chains and hidden relay decks already have negative native
   evidence; repeating them unchanged would not advance this.

A runtime plugin that moves physics bodies could be a separate experiment,
but would require the plugin when playing and would not meet the standalone
Item.Gbx requirement. No raw-memory transform hack is proposed as a working
solution. The crashing nonkinematic-child fixture remains quarantined.

## Reproducible checks

`Tests/SingleVisibleCollisionProof/drawn_route_analysis.py` accepts a JSON array
of X/Z points, optional slider heading and radius. It samples each drawn line,
computes inverse translation/angle signals and reconstructs the route. Output
always states `nativeExportSupported: false`: exact mathematical reconstruction
does not imply native signal representability or preserved collision.

```
python Tests/SingleVisibleCollisionProof/drawn_route_analysis.py route.json --radius 15
python -m unittest discover -s Tests/SingleVisibleCollisionProof -p test_drawn_route_analysis.py -v
```

Five tests pass: irregular closed route under four coordinate frames, straight
line, square compensation, 25-point drawing, and invalid/singular input refusal.
The diagnostics identify sampled signal reversals and interior extrema; they
do not claim a native key-count lower bound or a verified fitting algorithm.
This establishes that eight drawn points is not the geometric limit. The
remaining limit is expressing and safely executing the required physical signals.

Next native experiment: a world-root linear constraint plus nonzero LocAnim,
with matching mesh/shape and independent control cases. Success must include
both-axis motion of the visible platform **and** sustained car support, not
just a successful load or a collision at the starting position.
