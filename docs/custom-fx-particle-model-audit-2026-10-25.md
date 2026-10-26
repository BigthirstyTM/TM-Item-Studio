# Particle-model audit — 25 October 2026

`Tests/FxParticleModelAudit` scanned the local game extraction for all
`*.ParticleModel.Gbx` assets. The machine-readable result is
[`docs/evidence/fx-particle-model-audit-2026-10-25.json`](evidence/fx-particle-model-audit-2026-10-25.json).

The scan found **83** particle models. GBX.NET could read 61 of them as
`CPlugParticleEmitterModel`; 22 models (mostly ShootMania variants) failed in
the public reader with a truncated/unknown body. Sixty parsed models have
external texture/material references. The audit writes every parsed model to a
fresh buffer and reparses it, but only one output is byte-identical. This is
expected for an opaque or newer particle graph and is a warning against using
the normal typed serializer as an FX writer.

The public model surface exposes only a small, safe subset on these assets:
`IsSplashMode`, `ShadowMapTexelSize` and `GameVersion`. Nested spawn, lifetime,
size, colour, alpha, physics and render records are not proven writable through
GBX.NET. The Item Studio therefore keeps those fields read-only/preserved and
only writes the independently validated native `LocalOffsetExpr` emitter point.

The result is useful for discovery and dependency inventory, not a claim that
an asset can run as a standalone item. A candidate still needs native preload,
placement, visible gameplay and save/reopen evidence before it can be promoted
to an editable FX carrier.
