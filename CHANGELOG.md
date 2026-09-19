# Changelog

All notable changes to TM Item Studio are documented in this file.

## [Unreleased]

### Added

- Local, lazy vanilla texture preview through a user-selected extracted
  texture folder.
- Native game-material names, links, slots, and visual-to-material mappings in
  the Materials & LOD inspector.
- Read-only capability-fixture scene inspection for material, collision,
  light, and mapping discovery.
- Typed light ownership/value-persistence diagnostics and collision/trigger
  source classification.
- Kinematic-reference and synthetic multi-constraint regression suites. These
  prove serialization and preview invariants only, not in-game chained motion.
- Deterministic local texture matching with safe DDS loading and neutral
  fallbacks for ambiguous or unreadable textures.

### Changed

- Removed the Native Trackmania pivot export section from the product UI. The
  Openplanet bridge remains an internal regression utility under `Tests`.
- Marked unproven controls as read-only or unsupported: light position and
  light/socket authoring, collision toggling, and static/kinematic conversion.
  Existing light color, intensity, and radius edits remain supported.
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
