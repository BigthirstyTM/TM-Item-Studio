# FX emission points in item coordinates

The real Sparkler stores `LocalOffsetExpr` in its FX prefab owner's frame. That
owner is rotated approximately -90 degrees around X and translated to item
Y=0.69592285. Consequently its local Z points up in the item, and local Y points
towards item -Z. Writing native `float3(0,3,0)` does not put the outlet at item
Y=3.

[`FxEmitterTransform.cs`](../Models/FxEmitterTransform.cs) converts between the
two frames. It normalizes a finite, nearly unit native quaternion, rotates in
double precision, and uses the transpose for the inverse. A requested item
point `(0,3,0)` resolves to approximately FX-local `(0,0,2.3040771)`, including
the owner's translation. Tiny nonzero X/Y components account for the exact
stored quaternion rather than replacing it with an assumed right angle.

Owner validation reuses `FxEmissionPoint`'s complete bounded graph validation
and `FxMeshPreview`'s strict temporary parse. The temporary copy substitutes a
null model reference for exactly that FX node, retaining all original owner
bytes. It must parse as a direct two-entry prefab with an actual static object
first and the null FX owner second. This ties the recovered rotation/position
to the actual prefab record immediately after the FX archive. A generic scan
for seven plausible float values is never used.

All emitters must have a constant zero `WorldOffsetExpr`. Nonzero or dynamic
world offsets require a separate model and are rejected instead of displaying
an inaccurate static outlet. `FxEmissionPoint` exposes that expression for
inspection while preserving it unchanged during native local-field edits.

`Read`/`TryRead` expose `Inspection.Primary` and `Emitters` in **item space**.
`Inspection.Native` retains the underlying native FX-local expressions.
`Export(bytes, itemPoint)` converts the target to FX-local coordinates and
delegates the exact expression splices to `FxEmissionPoint.Export`. All emitters
receive the same local delta, preserving their relative positions in both
frames. The actual owner transform and all unrelated opaque bytes remain
unchanged. An unchanged item-space point exports the original bytes exactly.

Validation:

```powershell
dotnet run --project Tests/FxEmitterTransform/FxEmitterTransform.csproj
```

The isolated project passed 35 checks, including 64 seeded quaternion inverse
cycles, rotated Sparkler and identity Fogger owners, compressed/uncompressed
containers, large inline image payloads, repeated edits, native inverse-splice
evidence, multiple-emitter spacing, and rejection of malformed owner transforms
or unsupported prefab structure. These checks prove conversion and file edits;
native visible outlet placement remains a separate game-side observation.
