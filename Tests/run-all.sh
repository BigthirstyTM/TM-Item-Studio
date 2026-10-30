#!/usr/bin/env bash
# Runs every console test suite in Tests/ and reports a single exit status.
#
#   Tests/run-all.sh              # all suites
#   Tests/run-all.sh ItemMotion   # only the named suites
#
# Suites are the projects that print PASS/FAIL and throw on failure. The tools
# (ItemChunkIndex, PathFollowerDiag, WriterCompatibility) take arguments at run
# time, so they are only compiled here.

set -u

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root" || exit 1

suites=(
    FlagConversion
    WavingFlagExport
    WavingFlagMapPackaging
    ItemEdits
    ItemIcons
    ItemMaterials
    ItemMotion
    ItemScene
    KinematicReference
    MacroblockExport
    ParserSentinels
    ParserSurfaces
    PlacementEdits
    VariantExport
)

# Keep every tracked research/fixture project compiling against the bundled parser.
mapfile -t projects < <(git ls-files 'Tests/*.csproj' 'Tests/**/*.csproj')
tools=()
for project in "${projects[@]}"; do
    directory="${project%/*}"
    name="${directory##*/}"
    is_suite=false
    for suite in "${suites[@]}"; do
        if [ "$directory" = "Tests/$suite" ]; then is_suite=true; break; fi
    done
    if [ "$is_suite" = false ]; then tools+=("$directory"); fi
done

if [ "$#" -gt 0 ]; then
    suites=("$@")
fi

failed=()

for suite in "${suites[@]}"; do
    printf '== %s\n' "$suite"
    if dotnet run --project "Tests/$suite"; then
        printf '   ok\n'
    else
        printf '   FAILED\n'
        failed+=("$suite")
    fi
done

for tool in "${tools[@]}"; do
    [ -d "$tool" ] || continue
    printf '== %s (build only)\n' "$tool"
    if dotnet build "$tool" -v q --nologo; then
        printf '   ok\n'
    else
        printf '   FAILED\n'
        failed+=("$tool")
    fi
done

printf '\n'
if [ "${#failed[@]}" -gt 0 ]; then
    printf 'failed: %s\n' "${failed[*]}"
    exit 1
fi

printf 'all suites passed\n'
