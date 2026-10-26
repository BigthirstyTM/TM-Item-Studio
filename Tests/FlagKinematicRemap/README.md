# Embedded v6 kinematic flag research fixture

This isolated offline generator repeats the kinematic question using the accepted v6 material-remap dependency chain and a genuine native `Flag16m` anchor. It does not control, install files into, or load Trackmania.

```powershell
dotnet run --project Tests/FlagKinematicRemap -- --generate <unused-output-directory>
```

The input paths and accepted Long250/dependency hashes are bounded to this repository's recorded research assets. The generator refuses an existing output directory. A new folder identity and map UIDs are generated for every run.

The four generated maps contain an overview and separate baseline, stationary kinematic, and rotating kinematic controls. Each has an adjacent `.manifest.json` sidecar with map hash, embedded payload paths/hashes, expected embedded identities, anchor count and native-primer fields for the cold-test preparation script. Each separate map embeds only its selected Item and the three exact v6 dependency files; the native flag remains a catalog anchor. The baseline Item is byte-exact to the successful v6 Long250 source. The kinematic pair preserves both whole Solid2 meshes, all five complete 86-frame cloth visuals, the original attachment transform, original cloth Dyna body apart from added diagnostic collision, and original 8–16 second / 0–1 phase timing. Normalizing the display name and two rotation upper bounds restores byte equality between the pair.

Both kinematic bodies use tiny diagnostic triangle collisions, and the pole changes from Static to Dyna. The experiment therefore does not establish native collision or physics parity. The rotating control requests a 12-second Y rotation at the original rest transforms; it does not request an orbit or aircraft motion. Actual rotation, attachment behavior and independent cloth deformation require game evidence.

The remaining command arguments are a small read-only Item structure inspector, retained for offline debugging. No shared FlagProbe project files are changed.
