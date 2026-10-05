# Changelog

All notable changes to TM Item Studio are documented in this file.

## [0.4.0] - 2026-10-06

### Added

- Composite Prefab Merging: merge multiple item prefabs into one assembly with
  world translation and rotation offsets directly manageable in the 3D viewport.
- Custom Pivot Point Editing: interactive pivot translation and rotation gizmos,
  personal pivot retention, and pivot connection across multiple selected meshes.
- Composite Group Motion: group selection with combined motion curves and
  hierarchical carrier-body constraint chaining (`World -> Carrier -> Members`),
  allowing group movements and individual member animations to play simultaneously
  without constraint collision.
- Carrier Body Loader Compatibility: minimal renderable visual geometry with
  shaded material mapping on kinematic carrier bodies, preventing Trackmania
  inventory drop and loader crashes.
- Experimental Dual-Body Collision Proxies: swept collision shapes for grouped
  moving members to preserve collision under Trackmania's Havok solver.
- Motion Path Assistant: multi-axis path generation, collision relay, and parked
  handover L-path presets.
- Trackmania PBR Texture Preview: local DDS texture loading for `_D`, `_N`,
  `_R`, and `_I` sets with sRGB base color and roughness/metallic channels.
- Multi-solid material index collision resolution when re-importing merged items.

### Changed

- Expanded viewport settings panel width and separated composite world transforms
  and group motion into dedicated collapsible controls.
- Rebased kinematic slot binding to safely handle multi-prefab assemblies.

## [0.3.0] - 2026-09-15
- Corrected browser contracts for shared geometry, typed variants, and
  asynchronous variant selection.

## [0.3.0] - 2026-09-15

### Added

- Native Trackmania/Openplanet bridge requests for pivot edits, with a bounded
  smoke test for native item loading.
- A model diagnostics report for detected, external, invalid, and unsupported
  item data.
- Authored kinematic-motion inspection and editing UI.
- `Tests/WriterCompatibility`, a controlled parse/save and existing-pivot
  compatibility probe for user-supplied items.

### Changed

- Preserve Trackmania 2020 collection `26` as its numeric value instead of
  rewriting it as the display string `Stadium2020`.
- Expand GmSurf/collision-surface parsing support.
- Preserve full imported variant metadata while improving variant navigation
  and preview performance.
- Standalone GBX exports that re-encode an imported archive now require an
  explicit compatibility acknowledgement instead of being rejected solely
  because they are not byte-identical.

### Verified compatibility

- A Blendermania pivot item exported through the standalone GBX.NET path was
  verified after a fresh Trackmania start: it appeared in the map-editor
  inventory, remained visible after placement, and retained edited pivots,
  kinematics, and lights.

### Notes

- Re-encoded items from other source families still require a fresh
  Trackmania inventory and placement check. Use the native bridge for
  unverified sources.

## [0.2.0] - 2026-09-11

### Added

- English user interface and startup/error messages.
- Embedded multi-variant item detection, viewport switching, and export.
- Compatibility handling for Editor++ item null-node references.
