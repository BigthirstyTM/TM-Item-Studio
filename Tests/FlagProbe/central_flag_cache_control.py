"""Reversible installation-cache control, explicitly NOT a standalone item.

Adds nine uniquely prefixed entries, never replaces a native ZIP entry. Run
prepare/restore only while Trackmania is stopped. The original ZIP is retained
outside the installation and restored byte-for-byte after the experiment.
"""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys
import zipfile

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'artifacts/flag-file-followup-20261022/central-cache-control'
PACKAGE = ROOT / 'artifacts/flag-native-primer-20261021/local-cache-package'
LIVE = Path.home() / 'Documents/Trackmania/Items/TMIS_LocalCachePackage'
CACHE = Path('C:/Program Files (x86)/Ubisoft/Ubisoft Game Launcher/games/Trackmania/Packs/GpuCache_D3D11_SM5.zip')


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def stopped():
    result = subprocess.run(['powershell', '-NoProfile', '-Command',
        "@(Get-Process Trackmania -ErrorAction SilentlyContinue).Count"],
        capture_output=True, text=True, check=True)
    assert result.stdout.strip() == '0', 'Only change the cache with the game stopped'


def prepare():
    stopped()
    assert not OUT.exists(), 'Preserve previous cache control'
    OUT.mkdir()
    backup = OUT / 'GpuCache_D3D11_SM5.original.zip'
    shutil.copy2(CACHE, backup)
    original_hash = digest(backup)
    assert digest(CACHE) == original_hash
    entries = json.loads((PACKAGE/'compiled-cache-manifest-complete.json').read_text())
    assert len(entries) == 9 and LIVE.is_dir()
    added = []
    with zipfile.ZipFile(CACHE, 'a', compression=zipfile.ZIP_DEFLATED) as z:
        names = set(z.namelist())
        for row in entries:
            source = PACKAGE/'files'/Path(row['relativeFile'])
            assert digest(source) == row['sha256']
            entry = 'Items/TMIS_LocalCachePackage/' + row['relativeFile'].replace('\\', '/')
            assert entry not in names
            z.writestr(entry, source.read_bytes())
            # A source-FID existence control, not a claim to possess native HLSL.
            hlsl = LIVE/Path(row['relativeFile'][:-len('.GpuCache.Gbx')])
            assert not hlsl.exists()
            hlsl.write_text('// Cache-backed loader control. Native source is unavailable.\n')
            added.append({'entry':entry,'sha256':row['sha256'], 'sourceFidFile':str(hlsl)})
    with zipfile.ZipFile(backup) as old, zipfile.ZipFile(CACHE) as new:
        assert set(new.namelist()) == set(old.namelist()) | {r['entry'] for r in added}
        assert all(old.read(n) == new.read(n) for n in old.namelist())
    receipt={'scope':'Installation modification control; not standalone, not a redistributable item',
        'originalSha256':original_hash,'modifiedSha256':digest(CACHE),'added':added,
        'originalEntriesUnchanged':True}
    (OUT/'modification.json').write_text(json.dumps(receipt,indent=2)+'\n')
    print(json.dumps(receipt,indent=2))


def restore():
    stopped()
    receipt=json.loads((OUT/'modification.json').read_text())
    backup=OUT/'GpuCache_D3D11_SM5.original.zip'
    assert digest(backup)==receipt['originalSha256']
    assert digest(CACHE)==receipt['modifiedSha256'], 'Refuse to overwrite an unrelated modification'
    shutil.copy2(CACHE, OUT/'GpuCache_D3D11_SM5.control.zip')
    shutil.copy2(backup,CACHE)
    assert digest(CACHE)==receipt['originalSha256']
    (OUT/'restored.json').write_text(json.dumps({'restoredSha256':digest(CACHE),'byteIdentical':True},indent=2)+'\n')
    print('Restored original installation cache, byte-identical.')


if __name__=='__main__':
    {'prepare':prepare,'restore':restore}[sys.argv[1]]()
