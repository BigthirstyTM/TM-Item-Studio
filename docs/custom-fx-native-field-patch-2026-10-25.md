# Native embedded FX field patch

The NICE/gbx-py schema under the local BlenderMania importer supplied the
missing field layout for an embedded `CPlugFxSystem` (`0x0915C000`). The native
emitter is stored in the FX root's first `CPlugFxSystemNode_ParticleEmitter`
(`0x2F0C4000`). Its position is an expression string:

```text
LocalOffsetExpr = float3(x,y,z)
WorldOffsetExpr = float3(x,y,z)
```

The same graph contains a `CPlugParticleEmitterModel` (`0x090B3000`). The
FogVolumetric fixture's GPU submodel exposes `FogEmissionRate` in
`CPlugParticleGpuModel` chunk `0x090C6002` (version 22). The schema also exposes
the particle spawn, lifetime, size, color, opacity and physics records listed
in [`evidence/fx-settings-matrix.tsv`](evidence/fx-settings-matrix.tsv).

`Tests/FxNativePatch/generate.py` uses that schema to build two offline
candidates from the embedded FogVolumetric rate-0 carrier:

- [`TMIS_Fogger_NativePyramid.Item.Gbx`](../artifacts/fx-clones/fx-native-patched-20261025/TMIS_Fogger_NativePyramid.Item.Gbx)
- `TMIS_Fogger_NativeDiamond.Item.Gbx`

Both are Fogger-derived. The earlier `TMIS_Sparkler_NativeDiamond` name was
incorrect: a diamond-shaped FogVolumetric carrier does not emit Sparkler
particles. That historical artifact must not be presented as a custom Sparkler.

Each candidate keeps the original 15-entry node table, writes the native
`LocalOffsetExpr` (`float3(0,1.5,0)`), changes the first prefab entity to a
typed Pyramid/Diamond `CPlugSolid2Model`, and sets the embedded fog rate to 0.
The generator reparses both outputs and records hashes and fields in
[`fx-native-patched-manifest.json`](../artifacts/fx-clones/fx-native-patched-20261025/fx-native-patched-manifest.json).
The output is an offline parse/build result; native Fids preload, placement,
save/reopen and visible particle output remain separate runtime checks.

`Tests/FxNativePatch/raw_patch.py` now reads the container, decompresses its
body, validates a bounded ParticleEmitter expression tail, changes its
nine-byte `LocalOffsetExpr` (`float3(0)` to `float3(1)`), and recompresses when
needed. The header/reference bytes and all other **decompressed body** bytes
remain exact. The Fogger clone is uncompressed; the Sparkler clone is compressed,
so compressed bytes and output size may differ. The earlier implementation
searched raw ASCII directly inside LZO data and is superseded. Corrected offline
outputs are under
[`fx-native-offset-probe-corrected-20261025`](../artifacts/fx-clones/fx-native-offset-probe-corrected-20261025/).
Their native verdict is pending; historical native results below describe the
earlier artifact hashes and must not be reused as proof for the corrected files.


Runtime follow-up: both mesh candidates passed native `Fids::Preload` as
`CGameItemModel`; the first Fogger placement call returned `false`, so native
mesh placement/rendering remains open. The raw offset probes both passed native
preload, while Fogger also passed placement plus save/reopen. Sparkler offset
placement returned `false`.
