# Kiesel's custom NICE: Mesh Modeler findings

Reviewed the user-provided `Kiesels custom NICE.zip` on 30 October 2026.
Attribution supplied by the user: Kiesel's custom version of ShadocAlex's NICE,
used with Blendermania. Archive SHA256:
`14d2d937a781f4c37201cc9e974683e460b6f2e75fe8a66ba6e07d739b89858c`.
The archive has Python sources but no commit/version manifest or license file.
It is retained locally under `artifacts/nice-research/`, not vendored into the app.
The triangulation implementation added to Item Studio is independently written;
no NICE or Blender implementation was copied.

## Findings and application

| Evidence in archive | Meaning for Item Studio | Outcome |
| --- | --- | --- |
| `src/gbx_structs.py:2463–2552`, `src/utils/content.py:624–669` | Crystal geometry stores polygons, material indices and face-corner UVs, not just independent triangles or UVs per shared vertex. Version 37 uses an indexed UV table; older versions have inline UVs. | GBX.NET already decodes the corner data. Added seam/duplicate-face regression coverage. |
| `blender/importer.py:239–278` passes complete faces to BMesh | Blender receives n-gons and handles their display; Studio's triangle fan is insufficient for concave faces. | Replaced the fan with bounded ear clipping in the authored polygon plane. Both windings and all projection axes are tested. |
| `src/gbx_structs.py:2610–2618` | A deformation starts with counted `(GroupIndex, LayerId)` masks, then deformation version, six bounds floats, start Iso4 and counted control Iso4 frames. The surrounding type version is the modifier version. | Confirms the mask/frame representation used by the existing bend fix. Do not scan to a sentinel or attach the modifier to the following geometry layer. |
| `src/utils/content.py:635–669` | The supplied extraction path only emits Geometry and Trigger layers. It does not evaluate deformation, scale, rotation, translation, mirror or smoothing. | This ZIP does **not** provide the missing general bend evaluator. Keep the native-measured bend implementation and unsupported-control diagnostics. |
| `src/gbx_structs.py:2553–2606` | Visibility, collision, trigger layers, masks, modifier parameters, spawn positions and light placements are distinct data. | Useful basis for a future layer inspector with separate visual/collision/trigger views; not evidence that modifier runtime behavior is implemented. |
| `src/utils/content.py:163–170`, `blender/importer.py:151–195,220–238` | User materials carry link, physics/gameplay IDs and color; the importer resolves Blender asset materials through local `.blend` libraries. | Preserve those meanings separately. The ZIP does not supply game textures or eliminate the need for texture dependencies. |
| `blender/importer.py:136–147,239–245` | Blender conversion is position `(x,-z,y)` and quaternion `(w,x,-z,y)`. | This is a Blender basis conversion. Do not apply it to Studio's existing game-coordinate scene or it would rotate models again. |
| `src/parser.py:16–53` | External files can be cached per import and missing files requested via a host hook. | Useful pattern for lazy dependency loading, with normalized-path cache keys, per-import lifetime and browser-granted file access. |

## Changes made after the review

`CrystalPolygon` triangulates simple concave polygons while preserving each
corner's index, UV and winding. It projects using the dominant Newell normal,
normalizes coordinates and rejects self-intersections, nonfinite positions,
degenerate faces and excessive corner counts. Triangulation happens **before**
deformation so a curved result is not incorrectly treated as a planar source.
Invalid faces produce a diagnostic rather than fabricated triangles. Authored
GBX geometry and the export path are untouched.

Tests cover a notched polygon on all three coordinate planes, both windings and
three scales; correct area and empty notch; malformed input; independent UVs at
shared positions; duplicate faces; and source-data isolation. The native bend
oracle remains in the same suite.

This improves the preview of simple authored polygons. It does not establish
Blender-identical triangulation of arbitrary nonplanar faces, polygons with
holes, or native equivalence for every Mesh Modeler item.

## Next useful additions, in order

1. A compact layer inspector with a searchable layer selector. Expose type,
   enabled state, visibility, collision, group membership and modifier masks;
   keep preview visibility separate from edits to exported data.
2. Separate visual, collision and trigger preview modes. NICE's labels provide
   a useful organization, but trigger/collision editing needs native tests.
3. Mask-aware translation, rotation and scale evaluation, then mirror and richer
   deformation. Verify pivot/independent-group semantics in Trackmania first.
4. Material diagnostics that show link, physics/gameplay IDs and resolution status,
   with searchable choices and lazy loading from user-selected dependencies.
5. Differential parser fixtures covering old inline UVs and version-37 indexed
   UVs, multiple materials and successive layers after deformation.

The archive is not a complete standalone Blender addon: it imports parent addon
modules that are absent here, and mentions a separate GBXBPY layer that is also
absent. The included extractor assumes version-37 UV tables and checks
`isVisible` before dispatching layer types when `visible_only` is enabled;
not every modifier has that field. It also does not apply `is_enabled` in the
crystal extraction loop. These are reasons to use this research selectively,
not replace the current GBX.NET parser or treat this importer as a universal oracle.
