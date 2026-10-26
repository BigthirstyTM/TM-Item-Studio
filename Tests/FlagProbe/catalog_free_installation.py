"""Cold control: are the three accepted items sufficient for saved-map loading?

The game must be stopped. No new GBX bytes or material arrangement are created.
"""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[2]
BASE = ROOT / 'artifacts/flag-file-followup-20261022'
OUT = BASE / 'catalog-free'
PACKAGE = BASE / 'download/TMIS_CustomFlags_Installation_20261022'
GAME = Path('C:/Program Files (x86)/Ubisoft/Ubisoft Game Launcher/games/Trackmania')


def digest(p):
    return hashlib.sha256(p.read_bytes()).hexdigest()


def main():
    state = subprocess.check_output(['powershell', '-NoProfile', '-Command',
        '@(Get-Process Trackmania -ErrorAction SilentlyContinue).Count'], text=True).strip()
    assert state == '0'
    OUT.mkdir(exist_ok=False)
    manifest = json.loads((PACKAGE / 'manifest.json').read_text())
    assert not any((GAME / r['path']).exists() for r in manifest['files'])
    rows = []
    for row in manifest['files']:
        if not row['path'].endswith('.Item.Gbx'):
            continue
        source = PACKAGE / 'files' / row['path']
        target = GAME / row['path']
        assert target.resolve().is_relative_to(GAME.resolve())
        assert digest(source) == row['sha256']
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, target)
        rows.append(dict(row, installed=str(target), verified=digest(target) == row['sha256']))
    assert len(rows) == 3 and all(r['verified'] for r in rows)
    (OUT / 'staging.json').write_text(json.dumps({'files': rows,
        'catalogAbsent': True, 'scope': 'Accepted installation files; saved-map reload without inventory overlay'}, indent=2))
    print('Staged three accepted items, no inventory overlay')


if __name__ == '__main__':
    main()
