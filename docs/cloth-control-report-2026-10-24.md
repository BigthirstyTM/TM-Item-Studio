# Trackmania cloth and flag control report

Date: 24 October 2026. Report language: English.

## Finding

Trackmania's Stadium flag is a deterministic **VertexTween** asset. The cloth is
stored as 86 baked vertex frames per animated LOD and is interpolated by
`Tech3_Warp_TDiffSpec_VertexTween`. It is not a general purpose per-item wind
simulation. The vanilla cloth entity has no cloth collider, skeleton, `LocAnim`,
or authored wind vector.

The measured cloth instance fields are:

| Field | Vanilla value | Meaning established by the item/runtime tests |
| --- | ---: | --- |
| `PeriodSc` | `8` seconds | Lower loop-period bound and timing input used by the ordinary cloth clock. |
| `PeriodScMax` | `16` seconds | Upper period bound. When it differs from `PeriodSc`, instances may receive different periods. |
| `Phase01` | `0` | Lower phase bound. |
| `Phase01Max` | `1` | Upper phase bound; this is why placed vanilla flags can start at different phases. |
| `IsKinematic` | `false` | The working waving cloth host is non-kinematic. |
| `TextureId` | `0` | Texture/material selection input, not a wind control. |
| `CastStaticShadow` | `false` | Shadow setting, not a wind control. |

These fields are `NPlugDynaObjectModel_SInstanceParams` on `Prefab.Ents[1]`.
The measured structure and values are documented in
[`item-flag-conversion.md`](item-flag-conversion.md).

## What can be controlled in an item

* **Playback speed:** indirectly, by authoring `PeriodSc` and `PeriodScMax`.
  A fixed speed uses equal values. The converter's SourceMesh path writes the
  requested period to both fields and sets both phase values to zero. The
  serialized timing path is established; each new arbitrary period still needs
  a native game playback check before it is treated as a production guarantee.
* **Initial phase:** indirectly, by authoring `Phase01` and `Phase01Max`.
  Equal values make the start phase deterministic; a range allows per-instance
  variation.
* **Amplitude, wavelength and travel direction:** these are not runtime item
  fields. They are encoded in the positions and normals of the stored frames.
  The converter can therefore change them only by generating a new set of baked
  frames. Reversing the frame sequence or changing the deformation axis is an
  offline authoring operation, not a live wind setting.
* **Whole-object rotation/translation:** this is a separate kinematic/`LocAnim`
  path. It does not provide a wind direction to the VertexTween cloth.

The material API contains a `CPlugShader.VIdViewDepWindIntens` visibility bit,
but a direct dump of the native `Tech3_Warp_TDiffSpec_VertexTween` shader shows
`VIdViewDepWindIntens=false`, `VisibleIdFlags=0`, and `RequirementFlags=0` for
the flag shader. This bit is a material view-dependency option, not a serialized
cloth wind vector or a replacement for the VertexTween frame clock.

## Runtime controls that did not solve the problem

The controlled motion fixtures in
[`custom-flag-motion-and-embedding-2026-10-22.md`](custom-flag-motion-and-embedding-2026-10-22.md)
show the current engine boundary:

* A kinematic body and native constraints can rotate the whole flag, but the
  cloth appeared frozen in the stationary control.
* Clearing `IsKinematic` preserves waving, but the tested `LocAnim` and
  constraint variants did not move the cloth with the requested rigid motion.
* Changing phase bounds did not restore simultaneous kinematic rotation and
  cloth deformation.

The Skyward follow-up reached the same boundary: separate constraints,
parent-child prefab composition, and nested prefab ownership all loaded, but the
animated `VertexTween` visual did not inherit the aircraft's live matrix. See
[`skyward-waving-banner-research-2026-10-23.md`](skyward-waving-banner-research-2026-10-23.md).

## Validation performed

The repository checks that cover the serialized controls and generated frames
passed:

* `dotnet run --project Tests/ItemMotion` — 31 checks passed.
* `dotnet run --project Tests/WavingFlagExport` — 32 offline checks passed.
* Direct native shader dump — `VIdViewDepWindIntens=false`, `VisibleIdFlags=0`,
  `RequirementFlags=0` ([captured dump](../artifacts/cloth-control-20261024/native-shader-dump.txt)).

The offline exporter and timing tests do not claim that arbitrary new geometry
has native game parity. The existing cold game recordings prove waving for the
accepted embedded shapes, but do not prove a runtime wind-vector control.

## Practical conclusion

For a portable map-only custom flag, use baked VertexTween frames and set the
period/phase fields. This supports controllable loop speed and start phase. A
wind direction or live wind-strength slider requires either rebaking frames or a
runtime renderer/plugin feature that changes draw timing/vertex data for every
client. Such a plugin would be client-local and would not make the control
portable to players without the plugin. The simultaneous kinematic-parent plus
independent waving-cloth path remains unproven.
