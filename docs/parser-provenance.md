# Bundled GBX.NET parser

## Current build: GBX.NET 2.4.5

`lib/GBX.NET.dll` is built from upstream tag
[`v2.4.5`](https://github.com/BigBang1112/gbx-net/releases/tag/v2.4.5),
commit `edbcf1b7ae394900075791bfb6b16511e98d46c4`, plus Studio's
[null-class-ID patch](../patches/gbx-net-null-class-id.patch). The exact source
tag, target framework, and patch hash are recorded in
[`lib/GBX.NET.version.json`](../lib/GBX.NET.version.json).

- Target: net8.0, Release.
- The binary hash is intentionally not part of the version contract: compiler
  metadata can vary with the temporary checkout path. The source tag and
  applied patch in the manifest are the reproducible identity.
- Upstream MIT terms are retained in [GBX.NET.LICENSE](../lib/GBX.NET.LICENSE).

The source includes the modern `CPlugSurface`/GmSurf support used by the
browser geometry importer. The local patch remains necessary for valid Editor++
indexed null node references. Rebuild or update it with
`eng/Update-GbxNet.ps1`; that script selects the newest stable NuGet version,
checks out its matching upstream tag, applies the patch, and rebuilds the DLL.
The scheduled workflow validates the parser and Studio build before opening an
update pull request. See [gbx-net-updates.md](gbx-net-updates.md).

Verification on 2026-10-19:

```bash
DOTNET_ROLL_FORWARD=Major dotnet run --project Tests/ParserSentinels
DOTNET_ROLL_FORWARD=Major dotnet run --project Tests/ParserSurfaces
npm ci --prefix Tests/Browser
npm --prefix Tests/Browser run test:collection-export
npm --prefix Tests/Browser run test:animation-upload
```

The shape suite processes all 23 exact native-validated contributions with the
bundled parser, comparing the complete decompressed archive on save/reparse.
The browser suite covers the pusher's real upload/export/reopen and geometry
export, and verifies the archive helper is using the exact bundled DLL. Its
previous transitive LZO dependency could silently select official GBX.NET.
See [native/browser evidence](evidence/approved-item-batch.md).

## Historical investigation of the previous bundled DLL

The remainder records the pre-GmSurf DLL and is retained as provenance for the
null-class-ID compatibility patch. Its hashes and replacement cautions refer
to that earlier artifact, not the current development build above.

The reviewed `lib/GBX.NET.dll` identifies as GBX.NET 2.4.4 (assembly version
2.4.4.0). It is not identical to the official NuGet net8.0 assembly.

| Artifact | SHA-256 |
|---|---|
| Bundled DLL | `09f87f2c447e4cadd7539f749acc79b73b3f3575cbadc91fcf967e93e5e8d30c` |
| Official 2.4.4 net8.0 DLL | `669c6aad644adf25b38aacf1fa7d203a51fc98a905f92728216a0adf967bd0d0` |

The [official package](https://www.nuget.org/packages/GBX.NET/2.4.4) records
upstream commit `975272fa085ba32dc65080c6904a1ebeee903201` in
[BigBang1112/gbx-net](https://github.com/BigBang1112/gbx-net), licensed under MIT.
This identifies a tested source base, not the original bundled DLL's build environment.

## Identified behavior

Comparing full C# decompilations with ILSpy 11.0.0.9375, with both DLLs isolated
from adjacent dependencies and XML documentation, found 665 identical source
files and one changed file. The sole decompiled source difference removes the
encapsulation requirement from the `rawClassId == uint.MaxValue` null check in
`GbxReader.ReadNodeRef(out GbxRefTableFile)`.

The [source patch](../patches/gbx-net-null-class-id.patch) implements that change
at the pinned revision. It affects the general node-reference reader, not just
surface data. An ordinary node index of -1 already means null; the patch also
accepts an indexed node followed by a class ID of `0xFFFFFFFF` as null. Neither
path should consume bytes belonging to the following field. Other unknown
class IDs and truncated inputs must still fail.

Decompiled-source comparison is not binary identity, a PE-resource audit, or
proof of exhaustive behavioral equivalence. Synthetic tests do not establish
real-item or in-game validity.

## Run the bounded regression suite

Requires a .NET 8-compatible SDK/runtime and NuGet access on first restore:

```bash
dotnet run --project Tests/ParserSentinels
```

If only a newer .NET runtime is installed, explicitly opt into runtime roll-forward:

```bash
DOTNET_ROLL_FORWARD=Major dotnet run --project Tests/ParserSentinels
```

The suite prints the loaded DLL hash and exits nonzero on failure. Its six cases
cover both null encodings, marker preservation, another unknown class ID,
truncated class/index and empty input. Fixtures are synthesized in source; no
third-party assets are needed. `--official` switches only the expected behavior
for the patched sentinel so the official DLL can be tested as an A/B control.

## Build from pinned source without replacing Studio's DLL

These Bash commands use a fresh temporary directory. Keep the upstream license
with any redistributed upstream source/binaries. Git, the .NET SDK and network
access to GitHub/NuGet are required. This builds upstream source and generators.

```bash
studio="$PWD" # Run from the Studio repository root.
scratch="$(mktemp -d)"
git clone https://github.com/BigBang1112/gbx-net.git "$scratch/gbx-net"
git -C "$scratch/gbx-net" checkout --detach 975272fa085ba32dc65080c6904a1ebeee903201
git -C "$scratch/gbx-net" apply --check "$studio/patches/gbx-net-null-class-id.patch"
git -C "$scratch/gbx-net" apply "$studio/patches/gbx-net-null-class-id.patch"
dotnet build "$scratch/gbx-net/Src/GBX.NET/GBX.NET.csproj" \
  -c Release -f net8.0 --nologo -m:2 -p:GeneratePackageOnBuild=false
rebuilt="$scratch/gbx-net/Src/GBX.NET/bin/Release/net8.0/GBX.NET.dll"
dotnet build Tests/ParserSentinels -m:2 \
  -p:GbxAssemblyPath="$rebuilt" --output "$scratch/parser-tests"
dotnet "$scratch/parser-tests/ParserSentinels.dll"
```

Use `DOTNET_ROLL_FORWARD=Major` on the last command if necessary. For the
official control, build the same test project with `GbxAssemblyPath` pointing
to the official package's `lib/net8.0/GBX.NET.dll`, use a different output
directory, and pass `--official` to the resulting executable. Without that
flag the official DLL should fail exactly the indexed-null-class case.

The pinned source rebuilt successfully during investigation. Four initial
sentinel probes matched the bundled DLL, and 37 variant-preservation checks
from a separate candidate fix passed with the rebuilt DLL, including reparsing
previous browser downloads. That separate suite is not part of this branch.
The browser was not rerun with the rebuilt parser, and no game test was run.
The initial rebuilt DLL hash was
`2f5a5d944434fe4315baedf2a16b3686a997f16356b35d72d36b79397981bc61`;
build environment differences may change it. No bit-for-bit reproduction is claimed.

The packaged patch was also applied to a fresh checkout at the pinned revision:
exactly one source line changed, the net8.0 build succeeded, and this branch's
six tests passed against its output. The six tests also passed against the
bundled DLL and against official 2.4.4 with `--official`. Without `--official`,
official 2.4.4 failed exactly the indexed-null-class test (exit status 1).

The upstream build produced generated-source warnings and a NU1902 advisory
for build dependency `Microsoft.Build.Tasks.Git` 10.0.102
([GHSA-23fw-v26w-5fgq](https://github.com/advisories/GHSA-23fw-v26w-5fgq)).
This is not evidence of a shipped Studio runtime vulnerability. Any dependency
upgrade should be separately reviewed from this compatibility reconstruction.

The updater deliberately stops if the compatibility patch no longer applies.
The additional `patches/gbx-net-skin-weights.patch` preserves Float3 skin blend
weights when reading/writing a vertex stream. The bundled v2.4.5 assembly includes
both patches; their hashes are recorded in `lib/GBX.NET.version.json` and the
updater applies both. This enables geometry extraction from the original pilot
skin. Its archive still has an undecoded trailing record: the UI marks the
character preview as a partial, read-only static rest pose. It does not claim
native character animation or editable vehicle/character serialization.

Every generated update must still pass the parser sentinel suite, shape suite,
Studio build, browser workflows, and relevant real-item/game compatibility
checks before it is merged.
