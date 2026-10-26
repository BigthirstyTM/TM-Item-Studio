"""Cold-stage an isolated native-motion fixture; never overwrite existing files."""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys

import game

ROOT = Path(__file__).resolve().parents[2]
BASE = ROOT / 'artifacts/flag-motion-embedded-20261022' / sys.argv[1]
GAME = Path('C:/Program Files (x86)/Ubisoft/Ubisoft Game Launcher/games/Trackmania')
OUT = BASE / 'staging'
OUT.mkdir(exist_ok=False)
game.destination = OUT
info = game.call('GetMapInfo')
assert info['name'] in {'TMIS Kinematic Phase Range Controls', 'TMIS Native Local Motion Controls', 'TMIS Native Local Motion V3 Controls', 'TMIS Native Local Motion V3 Function1', 'TMIS Nonkinematic Constraint Controls'}
result = game.call('BackToMainMenu')
if result.get('blockedByDialog'):
    game.call('RespondDialog', {'action': 'no'})
assert game.call('GetMode')['mode'] == 'Menu'
manifest = json.loads((BASE / 'files/manifest.json').read_text())
models = [(ROOT / row['path'], GAME / 'GameData/Stadium/Items' / Path(row['path']).name, row['sha256'])
          for row in manifest['models']]
map_source = ROOT / manifest['map']
map_target = Path.home() / 'Documents/Trackmania/Maps/000_TMISTest' / map_source.name
assert not map_target.exists()
for source, target, sha in models:
    assert not target.exists() and hashlib.sha256(source.read_bytes()).hexdigest() == sha.lower()
stop = subprocess.run(['powershell', '-NoProfile', '-Command',
    "$flagMotionVariantProcess=Get-Process Trackmania; if (@($flagMotionVariantProcess).Count -ne 1) { throw 'Expected one process' }; $flagMotionVariantProcess | Select-Object Id,StartTime | ConvertTo-Json; Stop-Process -Id $flagMotionVariantProcess.Id"],
    capture_output=True, text=True, check=True)
(OUT / 'previous-process.json').write_text(stop.stdout)
receipt = []
for source, target, sha in models:
    shutil.copy2(source, target)
    receipt.append({'path': str(target), 'sha256': sha, 'source': str(source)})
(OUT / 'files.json').write_text(json.dumps(receipt, indent=2))
shutil.copy2(map_source, map_target)
(OUT / 'map.json').write_text(json.dumps({'path': str(map_target),
    'sha256': hashlib.sha256(map_target.read_bytes()).hexdigest()}, indent=2))
print('Staged', len(receipt), 'new models and one isolated map; restart the game.')
