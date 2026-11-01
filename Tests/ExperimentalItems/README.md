# Experimental item fixture generator

This console tool is research infrastructure, not a test asserting gameplay success. Use the repository's bundled parser.

- `<known-prefab-checkpoint.Item.Gbx> <kinematic-cart.Item.Gbx> <output-directory>` creates C-series controls and a directly driven trigger candidate. Carrier collision is microscopic and parked below the mesh to avoid moving the test car. Default generation excludes the known-crashing parent-trigger experiment.
- `--map <template.Map.Gbx> <candidate.Item.Gbx> <output.Map.Gbx> [anchorX]` replaces the template's anchored items in memory and embeds one candidate at (anchorX or 320, 8, 336). The template file itself is not modified. Use a disposable template, not a user work map.
- `--common <known-prefab-checkpoint.Item.Gbx> <output-directory>` makes Ornament, PickUp and two sound-reference probes. Native loading is not gameplay/audio proof.
- `--catalog <extracted-GameData-directory> <report.json>` inventories types and common-item sound references. Parser failures remain recorded.
- `--particle <ParticleModel.Gbx>` dumps readable fields; unsupported graphs may throw.

Native procedure and outcomes: `docs/experimental-items-2026-11-02.md`. Trackmania/Openplanet is required for runtime verification. Generated binary assets are local test artifacts and are not included in the repository.
