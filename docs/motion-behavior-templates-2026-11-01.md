# Motion editing and behavior templates (0.5.10)

Choose a searchable template under the path profile, edit its settings, and apply it to the current route. Rollercoaster, bird flight, car body, boat and train profiles remain editable afterward. Templates describe movement, not automatic mesh creation.

The coaster uses `vNext^2 = v^2 - 2*g*heightChange - 2*resistance*distance`, with speed bounds and a backward braking envelope. Mark the powered lift explicitly (or detect the first uphill section). Closed routes solve for a periodic starting speed. Reapplying a template recalculates its profile; manual edits remain until then.

Scenery can accelerate continuously within a leg: a linear carrier contributes `L*r*u`, and an aligned quadratic child contributes `L*(1-r)*u^2`, where `r = 2*vStart/(vStart+vEnd)`. This does not add geometry samples. Collision relay retains sampled movement: child transforms do not establish equivalent physical collision. Passenger handoff launches remain an experimental limitation.

## Editing

- Total path loop time is in seconds, under Path segments & timing. It scales all movement speeds together, retaining relative differences. Fixed start delays and pauses are included in the total and remain unchanged. Native millisecond rounding applies; impossible short legs are rejected.
- Drag speed dots vertically or use range ramps. Sampled detail is adjustable from 1 to 16, within the existing 128-leg and 80-ms minimum limits. Continuous scenery acceleration does not need this subdivision.
- Top, front and side views lock the camera while editing; dragging preserves the hidden coordinate. Active buttons are highlighted. Preview shows a countdown during a start delay.
- Preview and Apply path retain the edited profile. Moving coordinates with the same point count preserves speed, rotation and part settings. Apply the template again to recalculate banking from changed geometry.
- Separate visual meshes can flap, rotate as wheels or spin as propellers. Set their hinge/axle pivot and axis. Fused source meshes need splitting externally. Wheels use mean segment speed and radius; their phase resets at the route seam. Wing and propeller periods run independently of the route loop.

## Verification

All repository suites passed; ItemMotion has 46 passing checks. Browser checks cover all five templates, preview/export matrix parity, mirrored wings with nonzero pivots (including the second loop), main Apply preserving settings, fixed-view pointer dragging, speed-dot dragging, preview resume, doubled total time and start-delay countdown.

Native Trackmania recordings in `artifacts/coaster-inertia` show a full 128-leg scenery coaster lap with several camera positions and a separately exported bird with moving wings. The coaster lap is 92.227 seconds, with a marked lift at legs 84-90. These are visual movement tests, not new passenger-collision proof. Wheels and propellers have compiler checks; no native car demonstration is claimed.

More route samples increase graph size even when the source mesh is shared: the measured cart exports at 16/32/64/128 points are 62,032/69,361/81,546/109,404 bytes. The coarse routes differ geometrically and are not equally accurate copies of the track. Mesh complexity also contributes to size. Continuous speed interpolation avoids adding extra points solely for acceleration; geometric corner smoothing still needs points.

Evidence files: `Coaster_Inertia_Run.mp4`, its embedded `.Map.Gbx`, `TMIS_Cart_Inertia_128.Item.Gbx`, the editable route JSON, and `parts/Bird_BrowserParts.mp4` with its map, exported bird and editable source. Raw runtime logs are excluded from the delivery archive.
