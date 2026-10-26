"""Preload-only loose and embedded split-archive flags; never draw failures."""
import hashlib
import json
from pathlib import Path
import shutil
import sys
import time

import game
from motion_flag_fixture import save
from read_flag_visuals import model_mesh, mesh_flags

ROOT = Path(__file__).resolve().parents[2]
BASE = ROOT / 'artifacts/flag-motion-embedded-20261022/external-graph'


def main():
    out = BASE / (sys.argv[1] if len(sys.argv) > 1 else 'live1')
    out.mkdir(exist_ok=False)
    game.destination = out
    if game.call('GetMode')['mode'].startswith('MapEditor'):
        assert game.call('GetMapInfo')['name'] == 'TMIS Kinematic Waving Flag Controls'
        result = game.call('BackToMainMenu')
        if result.get('blockedByDialog'):
            game.call('RespondDialog', {'action': 'no'})
    assert game.call('GetMode')['mode'] == 'Menu'
    manifest = json.loads((BASE / 'files-v2/manifest.json').read_text())
    live = Path.home() / 'Documents/Trackmania/Items/TMIS_ExternalGraph'
    resumed = live.exists()
    if resumed:
        prior = json.loads((BASE / 'user-staging.json').read_text())
        assert {p.name for p in live.iterdir()} == {r['file'] for r in manifest['files']}
        assert all(hashlib.sha256(Path(r['path']).read_bytes()).hexdigest() == r['sha256'].lower() for r in prior)
    else:
        live.mkdir()
    receipt = []
    for row in manifest['files']:
        source = BASE / 'files-v2' / row['file']
        assert hashlib.sha256(source.read_bytes()).hexdigest() == row['sha256'].lower()
        target = live / row['file']
        if not resumed:
            shutil.copy2(source, target)
        receipt.append({'path': str(target), 'sha256': row['sha256']})
    if not resumed:
        save(BASE, 'user-staging', receipt)
    game.call('ControlFids', {'action': 'updateTree', 'drive': 'user',
        'path': 'Items', 'withFiles': True})
    load = save(out, 'loose-preload', game.call('ControlFids', {'action': 'preload',
        'drive': 'user', 'path': 'Items/TMIS_ExternalGraph/TMIS_ExternalGraph.Item.Gbx'}))
    loose = save(out, 'loose-visuals', mesh_flags(model_mesh(load['nod']['ptr'])))
    # No placement, including when the graph parses successfully.
    source = ROOT / manifest['map']
    target = Path.home() / 'Documents/Trackmania/Maps/000_TMISTest' / source.name
    assert not target.exists()
    shutil.copy2(source, target)
    save(BASE, 'map-staging', {'path': str(target), 'sha256': hashlib.sha256(target.read_bytes()).hexdigest()})
    game.call('OpenMapInEditor', {'path': '000_TMISTest/' + target.name})
    for _ in range(100):
        if game.call('GetReadiness', {'want': 'editor'}).get('ready'): break
        time.sleep(.25)
    assert game.call('GetReadiness', {'want': 'editor'}).get('ready')
    assert game.call('GetItems', {'limit': 10})['total'] == 0
    prefix = 'MemoryTemp/CurrentMap_EmbeddedFiles/ContentLoaded/Items/TMIS_ExternalGraph/'
    save(out, 'embedded-files', game.call('ControlFids', {'action': 'list', 'drive': 'fake', 'path': prefix}))
    result = {}
    try:
        load = save(out, 'embedded-preload', game.call('ControlFids', {'action': 'preload',
            'drive': 'fake', 'path': prefix + 'TMIS_ExternalGraph.Item.Gbx'}))
        result = mesh_flags(model_mesh(load['nod']['ptr']))
    except Exception as error:
        result = {'error': str(error)}
    save(out, 'embedded-visuals', result)
    save(out, 'final-map', game.call('GetMapInfo'))
    game.call('BackToMainMenu')
    assert game.call('GetMode')['mode'] == 'Menu'
    print(json.dumps({'looseStructured': loose['structured'], 'embedded': result}))


if __name__ == '__main__':
    main()
