"""Stage and inspect only the two owned kinematic installation controls."""
import hashlib
import json
from pathlib import Path
import shutil
import sys
import time

import game
from read_flag_visuals import read, mesh_flags

ROOT = Path(__file__).resolve().parents[2]
BASE = ROOT / 'artifacts/flag-motion-embedded-20261022/kinematic'
GAME = Path('C:/Program Files (x86)/Ubisoft/Ubisoft Game Launcher/games/Trackmania')


def save(out, name, value):
    path = out / (name + '.json')
    assert not path.exists(), path
    path.write_text(json.dumps(value, indent=2))
    return value


def setup():
    out = BASE / 'hot1'
    out.mkdir(exist_ok=False)
    game.destination = out
    assert game.call('GetMode')['mode'] == 'Menu'
    manifest = json.loads((BASE / 'files/manifest.json').read_text())
    targets = [(ROOT / r['path'], GAME / 'GameData/Stadium/Items' / (r['name'] + '.Item.Gbx'), r['sha256'])
               for r in manifest['models']]
    for source, target, sha in targets:
        assert not target.exists() and hashlib.sha256(source.read_bytes()).hexdigest() == sha.lower()
    receipt = []
    for source, target, sha in targets:
        shutil.copy2(source, target)
        receipt.append({'path': str(target), 'sha256': sha, 'source': str(source)})
    save(BASE, 'staging', receipt)
    game.call('ControlFids', {'action': 'updateTree', 'drive': 'game',
        'path': 'GameData/Stadium/Items', 'withFiles': True})
    results = []
    for _, target, _ in targets:
        load = game.call('ControlFids', {'action': 'preload', 'drive': 'game',
            'path': 'GameData/Stadium/Items/' + target.name})
        item = load['nod']['ptr']
        variants = read(item, 0x288)
        assert read(variants, 0x48, 'u32') == 1
        prefab = read(read(variants, 0x40), 0x18)
        assert read(prefab, 0x48, 'u32') == 4
        dyna = read(read(prefab, 0x40), 0x50 + 8)
        result = dict(name=target.name, **mesh_flags(read(dyna, 0x20)))
        results.append(result)
        save(out, target.stem + '-visuals', result)
    assert all(r['structured'] == 5 for r in results), 'Never draw failed candidates'
    source = ROOT / manifest['map']
    target = Path.home() / 'Documents/Trackmania/Maps/000_TMISTest' / source.name
    assert not target.exists()
    shutil.copy2(source, target)
    save(BASE, 'map-staging', {'path': str(target), 'sha256': hashlib.sha256(target.read_bytes()).hexdigest()})
    game.call('OpenMapInEditor', {'path': '000_TMISTest/' + target.name})
    for _ in range(80):
        if game.call('GetReadiness', {'want': 'editor'}).get('ready'):
            break
        time.sleep(.25)
    assert game.call('GetReadiness', {'want': 'editor'}).get('ready'), 'Map opening did not complete; inspect dialog'
    save(out, 'items', game.call('GetItems', {'limit': 10}))
    save(out, 'map', game.call('GetMapInfo'))
    game.call('ControlOverlay', {'action': 'hide'})
    print(json.dumps({'structured': [r['structured'] for r in results], 'map': str(target)}))


if __name__ == '__main__':
    setup()
