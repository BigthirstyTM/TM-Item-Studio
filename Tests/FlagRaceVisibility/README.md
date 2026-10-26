# Offline ordinary-race flag visibility placement

Run from the repository:

```powershell
dotnet build Tests/FlagRaceVisibility --nologo
dotnet run --project Tests/FlagRaceVisibility -- --generate artifacts/flag-selfcontained-20261022/<new-directory>
python Tests/FlagRaceVisibility/camera_evidence.py artifacts/flag-selfcontained-20261022/<new-directory>
```

The generator pins the final validated, shadow-baked and reference-repaired gravity-map SHA-256. It changes only the nine XYZ float words of the three existing proven custom shapes. It uniquely locates each exact old position inside bounded chunk `03043040`, patches those words in the decompressed body, preserves compression mode, reparses and verifies the result. Restoring those nine words must restore the complete original decompressed body. Header, reference table, all native roads, genuine native primer, validation ghost/times and complete embedded ZIP remain exact. No map or Item node is reserialized. The existing `FlagMapRepair/MapArchive.cs` reader is linked read-only; its ZIP replacement method is not invoked.

The candidate refuses an existing output directory. It never stages or opens the map, calls a runtime provider or controls a process.

Moving the shapes can invalidate the old baked lighting. Preserved source validation/shadow data are provenance, not acceptance of this new candidate. The required native sequence is: open and inspect; revalidate; compute shadows; Save As with a unique identity; repair that actual native export using the bounded flag repair utility; then cold-load it in ordinary Local race with no Openplanet and no loose custom assets.

The coordinates are a camera-placement hypothesis based on the parsed road transforms and earlier car-view receipts. The supplementary plot is explicitly an offline planning diagram, never evidence that cloth has rendered in Local race.
