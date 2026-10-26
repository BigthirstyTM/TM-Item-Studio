"""Package the accepted installation files, never the rejected auto-mount ZIP."""
import hashlib
import json
from pathlib import Path
import shutil
import zipfile

ROOT = Path(__file__).resolve().parents[2]
BASE = ROOT / 'artifacts/flag-file-followup-20261022'
OUT = BASE / 'download/TMIS_CustomFlags_Installation_20261022'
GAME = Path('C:/Program Files (x86)/Ubisoft/Ubisoft Game Launcher/games/Trackmania')


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    OUT.mkdir(parents=True, exist_ok=False)
    staging = json.loads((BASE / 'plugin-free/staging.json').read_text())
    rows = []
    for row in staging['assets']:
        source = Path(row['archived'])
        assert digest(source) == row['sha256'].lower()
        relative = Path(row['path']).relative_to(GAME).as_posix()
        target = OUT / 'files' / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, target)
        rows.append({'path': relative, 'sha256': digest(target)})
    manifest = {'scope': 'Game-directory installation; not portable or auto-mounted',
                'gameExeSha256': digest(GAME / 'Trackmania.exe'), 'files': rows}
    (OUT / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
    (OUT / 'Install.ps1').write_text(INSTALLER, encoding='utf-8-sig')
    (OUT / 'README.md').write_text(README, encoding='utf-8')
    maps = OUT / 'example-maps'
    maps.mkdir()
    for filename in ['TMIS_MinimalFlags_20261022.Map.Gbx']:
        shutil.copy2(Path.home() / 'Documents/Trackmania/Maps/My Maps' / filename, maps / filename)
    shutil.copy2(Path.home() / 'Documents/Trackmania/Maps/000_TMISTest/TMIS_PluginFreeProof_20261022.Map.Gbx', maps)
    archive = OUT.with_suffix('.zip')
    assert not archive.exists()
    with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for p in sorted(OUT.rglob('*')):
            if p.is_file():
                z.write(p, OUT.name + '/' + p.relative_to(OUT).as_posix())
    with zipfile.ZipFile(archive) as z:
        assert z.testzip() is None
        for row in rows:
            assert hashlib.sha256(z.read(OUT.name + '/files/' + row['path'])).hexdigest() == row['sha256']
    (OUT.parent / 'package-validation.json').write_text(json.dumps({
        'archive': str(archive), 'sha256': digest(archive), 'bytes': archive.stat().st_size,
        'runtimeFiles': rows, 'zipIntegrity': 'passed', 'exactAcceptedBytes': True,
        'installerExecution': 'Not implied by ZIP validation; see research report'
    }, indent=2))
    print(archive)


INSTALLER = r'''[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory = $true)][string]$GameDirectory,
    [switch]$Uninstall,
    [switch]$ItemsOnly
)
$ErrorActionPreference = 'Stop'
$flagGameRoot = [IO.Path]::GetFullPath($GameDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar)
if (-not (Test-Path -LiteralPath (Join-Path $flagGameRoot 'Trackmania.exe'))) {
    throw 'Choose the game installation containing Trackmania.exe.'
}
if (Get-Process Trackmania -ErrorAction SilentlyContinue) { throw 'Close Trackmania first.' }
$flagManifest = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'manifest.json') | ConvertFrom-Json
$flagRows = @($flagManifest.files | Where-Object { -not $ItemsOnly -or $_.path.EndsWith('.Item.Gbx') } | ForEach-Object {
    $flagTarget = [IO.Path]::GetFullPath((Join-Path $flagGameRoot $_.path))
    if (-not $flagTarget.StartsWith($flagGameRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Manifest path leaves the selected installation.'
    }
    $flagSource = Join-Path (Join-Path $PSScriptRoot 'files') $_.path
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath $flagSource).Hash -ne $_.sha256) {
        throw "Package file failed integrity check: $($_.path)"
    }
    if (Test-Path -LiteralPath $flagTarget) {
        if (-not $Uninstall) { throw "Refusing to overwrite an existing loose file: $flagTarget" }
        if ((Get-FileHash -Algorithm SHA256 -LiteralPath $flagTarget).Hash -ne $_.sha256) {
            throw "Refusing to remove a changed file: $flagTarget"
        }
    }
    [pscustomobject]@{ Source = $flagSource; Target = $flagTarget; Hash = $_.sha256 }
})
if (-not $Uninstall -and
    (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $flagGameRoot 'Trackmania.exe')).Hash -ne $flagManifest.gameExeSha256) {
    throw 'This research package was tested on a different game executable. Revalidate before installing.'
}
$flagAction = if ($Uninstall) { "Remove $($flagRows.Count) unchanged custom-flag installation files" } elseif ($ItemsOnly) { 'Install three custom flags for saved-map loading' } else { 'Install three custom flags and the inventory addition' }
if ($PSCmdlet.ShouldProcess($flagGameRoot, $flagAction)) {
    if ($Uninstall) {
        foreach ($flagRow in $flagRows) {
            if (Test-Path -LiteralPath $flagRow.Target) { Remove-Item -LiteralPath $flagRow.Target }
        }
    } else {
        $flagCreated = @()
        try {
            foreach ($flagRow in $flagRows) {
                New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($flagRow.Target)) -Force | Out-Null
                Copy-Item -LiteralPath $flagRow.Source -Destination $flagRow.Target -ErrorAction Stop
                $flagCreated += $flagRow
                if ((Get-FileHash -Algorithm SHA256 -LiteralPath $flagRow.Target).Hash -ne $flagRow.Hash) {
                    throw "Installed hash mismatch: $($flagRow.Target)"
                }
            }
        } catch {
            foreach ($flagRow in $flagCreated) {
                if ((Get-FileHash -Algorithm SHA256 -LiteralPath $flagRow.Target).Hash -eq $flagRow.Hash) {
                    Remove-Item -LiteralPath $flagRow.Target
                }
            }
            throw
        }
    }
    Write-Output $flagAction
}
'''

README = '''# Experimental custom flags — installation package

This is a file-only **game installation** solution. It is not a portable custom
item, an auto-mounted ZIP, or a map-only solution. Every client opening the
example map needs the same installed files. Putting this ZIP in Packs does not
install it.

The three accepted models contain their geometry inline: long (2.5x),
swallowtail, and round. They use Trackmania's packed native flag material.
The optional fourth file adds the three identities to the native editor inventory.
No dedicated flag companion is included or required. No stock packed archive
is overwritten. Full vanilla parity has not been established.

A cold game process with Openplanet injection disabled visibly renders all
three custom outlines and animates their cloth. The proof map is included with
its flags positioned in front of the default editor camera.

## Install or remove

1. Extract this package and close Trackmania.
2. In PowerShell, run the script from this folder, selecting the installation:

   `./Install.ps1 -GameDirectory "C:/path/to/Trackmania"`

   Add `-WhatIf` for a read-only preview. The script verifies all package hashes,
   requires the tested game executable, and refuses to overwrite any existing
   loose target. An installation with another inventory overlay needs a manual
   merge and separate validation; do not replace that overlay blindly.
   For loading the supplied maps without adding construction-menu entries, add
   `-ItemsOnly`. That installs only the three Item.Gbx files and does not touch
   the inventory JSON. Use `-ItemsOnly -Uninstall` to remove that installation.
3. Start the game. The item inventory folder is **TMIS Adjacent Test**.
   Optionally copy the example map into Documents/Trackmania/Maps/My Maps.

To remove, close the game and run:

`./Install.ps1 -GameDirectory "C:/path/to/Trackmania" -Uninstall`

Removal checks exact paths and hashes and refuses to remove changed files.
The example map is preserved. After removal, maps using these identities
require reinstalling the files before opening.

## Evidence and limits

The English report is `docs/custom-flag-file-followup-2026-10-22.md` in the
TM Item Studio workspace. Its cold-run receipts distinguish ordinary test
plugins from the separate process with Openplanet injection disabled.

The 1080p overview/close-up video demonstrates the native four-file-per-shape
representation. The later three-single-item installation was tested separately.
Do not infer portable embedding, other game versions, multiplayer compatibility,
all skin combinations, collision parity, or exhaustive LOD/lighting parity.
'''

if __name__ == '__main__':
    main()
