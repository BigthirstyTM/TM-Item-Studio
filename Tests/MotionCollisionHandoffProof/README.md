# Isolated native collision handoff probes

Run with the same measured +/-4 m cube as MotionCollisionRelayProof:

```powershell
dotnet run --project Tests/MotionCollisionHandoffProof -- <Cube.Item.Gbx> <output-directory>
```

These are experiments, not production path-follow exports. The isolated fixtures
have two 16 x 2 x 16 m decks, independent world-root constraints, and 2 m/s motion.
Place at (400,15,400), zero rotation, Stadium ground Y=8. Active deck top is Y=16.
Spawn with editor testFromCoord (12,10,12), North. Do not apply throttle/steering.

The first deck waits 2 s then moves +16 m X during seconds 2-10. The second deck
starts at (16,0,0). Each timeline totals 120 s; only the first transition is tested,
before the reset at 120 s. This intentionally separates handoff from loop closure.

- StraightHard: at 10 s the first deck folds away and the second unfolds, then
  travels another 16 m along X during seconds 10-18.
- CornerHard: same timing, second deck travels +16 m Z instead.
- CornerOverlap: incoming deck is present from the start. Both decks rest at
  the corner during seconds 10-12, then the second starts moving along Z. The
  outgoing deck folds at 14 s, two seconds after departure.
- CornerNoFold: both decks remain present throughout. The second travels +Z
  during seconds 10-18. This control removes folding but leaves extra physical
  platforms in the scene; it is not a single-visible-item solution.
- CornerClearance: same timing as CornerOverlap, except outgoing folding occurs
  at 19 s (seven seconds after the second deck departs).

SquareOverlap extends the same idea to a 40 s repeating square with four bodies.
Each body moves for 8 s, rests at its end for 4 s, then resets while hidden. Legs
start 10 s apart. Bodies become visible 8 s before their departure and fold away
4 s after arrival, so there are overlapping visible/collidable decks. The last
body's hold wraps across the cycle seam. This is a research test of repeated
handoffs, not a visual equivalent to one platform following a route.

Folding is a constant 0/180 degree X rotation about a pivot 48 m below the deck
centre. Mesh and both collision meshes use the same offset. Tests must record
car trajectory/contact and visible deck movement, not just successful parsing.
Generator assertions check saved/reparsed body bindings and channel budgets.

For a registered item, start at the game menu and record a disposable fixture:

```powershell
python Tests/MotionCollisionHandoffProof/native_probe.py TMIS_CH_CornerOverlap CH_OverlapRepeat --output artifacts/collision-handoff
```

Use a unique label for each recording/map. `--seconds 130` covers three 40 s
cycles for SquareOverlap. The runner requires local tm-control-mcp/E++, the host
call.py helper and ffmpeg. It refuses nonempty/new-map mismatches, checks exactly
one requested item before playing, saves telemetry and RPC evidence, and returns
to the menu after a successful run. Its approximately 4 Hz samples cannot prove
continuous contact at every physics tick. Inspect the video as well.
