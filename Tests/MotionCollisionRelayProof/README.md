# Collision path research fixtures

Experimental native physics probes, **not a supported arbitrary-path exporter**.
The source must be the measured 8 m cube (local vertices +/-4 m); arbitrary
source geometry is not normalized by this generator.

```powershell
dotnet run --project Tests/MotionCollisionRelayProof -- <Cube.Item.Gbx> <output-directory>
```

The generator writes six distinct items and reparses each export. It converts the
complete source document to the moving template, changes both visual and collision
vertices, and keeps all moving bodies world-bound. No runtime plugin drives them.

- OrbitPlatform: offset 16 x 2 x 16 m deck, Y rotation, 24 s circle.
- CompoundPlatform: the same rotation plus 16 m X translation and return.
- HelixPlatform: the same rotation plus 16 m Y translation and return.
- RelaySquare: four world-bound decks take turns covering the four 16 m legs of
  a square. Each leg lasts 8 s; cycle 32 s. Inactive decks rotate 180 degrees
  around a pivot 48 m below the deck centre. This hides their mesh AND collision
  below the test terrain, but can create violent contact impulses at handoff.
- RelaySquareCage: the same handoff with four side walls per deck (20 bodies).
- RelaySquareLowPivot: the flat relay with the pivot only 8 m below the deck.
  This isolates pivot distance; other route and timing settings stay identical.

Place the first three items at (388,15,400), relay variants at (400,15,400).
Use Stadium ground Y=8, editor testFromCoord (12,10,12), North. Active deck top
is Y=16. Ground-height decks are unsuitable for proving that they carry the car.
Record position, velocity, wheel contact and input as well as a fixed-camera
video. Check multiple full cycles: native loading or one good handoff is not a
reliable-platform result.

The hiding mechanism is fixture-specific: no global guarantee that hidden
collision stays clear of another map, terrain height, or another route exists.
The square is rotationally symmetric; arbitrary models would visibly rotate
between differently oriented legs. Do not expose this as a production feature
without solving these constraints and measuring handoff stability.
