"""Isolate the already accepted adjacent controls; game must be stopped.

Only exact, hash-verified files from this investigation's receipts are moved.
The packed native inventory and original game archives are never changed.
"""
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[2]
BASE = ROOT / 'artifacts/flag-file-followup-20261022'
OUT = BASE / 'minimal-installed/isolation'
GAME = Path('C:/Program Files (x86)/Ubisoft/Ubisoft Game Launcher/games/Trackmania')


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    processes = subprocess.run(['powershell', '-NoProfile', '-Command',
        '@(Get-Process Trackmania -ErrorAction SilentlyContinue).Count'],
        capture_output=True, text=True, check=True)
    assert processes.stdout.strip() == '0', 'Stop game before changing installed assets'
    OUT.mkdir(parents=True, exist_ok=False)
    keep = json.loads((BASE/'adjacent-files/staging.json').read_text())
    keep_paths = {Path(r['path']).resolve() for r in keep}
    owned = {}
    for name in ['adjacent-files', 'exact-installed-controls', 'game-body-compression',
                 'native-chain', 'native-chain-fixed-v2', 'native-ladder', 'native-package']:
        for row in json.loads((BASE/name/'staging.json').read_text()):
            path = Path(row['path']).resolve()
            assert path.is_relative_to(GAME.resolve()), path
            assert path not in owned, path
            assert digest(path) == row['sha256'].lower(), path
            owned[path] = row
    catalog = json.loads((BASE/'native-package/catalog-control/manifest.json').read_text())
    overlay = Path(catalog['path'])
    assert digest(overlay) == catalog['sha256'].lower()
    original = Path(catalog['source'])
    assert digest(original) == catalog['nativeSha256'].lower()
    data = json.loads(re.sub(r',\s*([}\]])', r'\1', original.read_text()))
    data['RootChilds'].append({'IsFolder': True, 'Name': 'TMIS Adjacent Test',
        'Childs': [{'Name': 'TMIS_Adjacent_'+s} for s in ['Long','Swallowtail','Round']]})
    replacement = (json.dumps(data, indent=2)+'\n').encode()
    moves=[]
    for path, row in owned.items():
        if path in keep_paths:
            continue
        target=OUT/'archived-game-files'/path.relative_to(GAME)
        target.parent.mkdir(parents=True,exist_ok=True)
        shutil.move(str(path), str(target))
        moves.append(dict(row, archived=str(target)))
    shutil.copy2(overlay, OUT/'previous-native-package-catalog.json')
    overlay.write_bytes(replacement)
    receipt={'archived':moves,'retained':keep,'catalog':{
        'path':str(overlay),'sha256':digest(overlay),
        'previousSha256':catalog['sha256'],'source':str(original)},
        'scope':'Three single-file game-drive items plus native catalog overlay; not portable user items'}
    (OUT/'receipt.json').write_text(json.dumps(receipt,indent=2))
    print(json.dumps({'archived':len(moves),'retained':len(keep),'catalog':receipt['catalog']}))


if __name__=='__main__':main()
