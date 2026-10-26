"""Verify one normally loaded embedded flag; no renderer writes or helpers.

Requires Menu and an offline map manifest. Reusing a staged map is permitted
only when every byte matches. Each evidence directory must be new.
"""
import hashlib
import json
from pathlib import Path
import shutil
import sys
import time

import game
import material_remap_fixture as probe


def main():
    source = Path(sys.argv[1]).resolve()
    out = Path(sys.argv[2]).resolve()
    out.mkdir(exist_ok=False)
    game.destination = out
    manifest = json.loads(Path(str(source) + '.manifest.json').read_text())
    expected_count = manifest['anchoredObjectCount']
    assert expected_count in (1, 2)
    assert hashlib.sha256(source.read_bytes()).hexdigest().upper() == manifest['outputSha256']
    baseline = {tool: game.call(tool) for tool in [
        'GetMode', 'ListPlugins', 'ListToolPacks', 'tm-mcp-pack-epp.Ping']}
    probe.save(out / 'baseline.json', baseline)
    assert baseline['GetMode']['mode'] == 'Menu'
    assert not any('TMIS' in p['id'] for p in baseline['ListPlugins']['plugins'])
    target = Path.home() / 'Documents/Trackmania/Maps/TMIS_MatRemap_Research' / source.name
    target.parent.mkdir(exist_ok=True)
    if not target.exists():
        shutil.copy2(source, target)
    assert target.read_bytes() == source.read_bytes()
    probe.save(out / 'staged-map.json', {'path': str(target), 'sha256': manifest['outputSha256']})
    probe.save(out / 'open.json', game.call('OpenMapInEditor', {'path': str(target), 'waitMs': 1500}))
    for _ in range(120):
        if game.call('GetReadiness', {'want': 'editor'}).get('ready'):
            break
        time.sleep(.25)
    else:
        raise RuntimeError('Embedded flag editor did not become ready')
    info = game.call('GetMapInfo')
    probe.save(out / 'map.json', info)
    assert info['nbItems'] == expected_count and info['name'] == manifest['MapName']
    items = game.call('GetItems', {'limit': 8})
    probe.save(out / 'items.json', items)
    assert items['total'] == expected_count
    assert sum(i['idName'] == manifest['anchor']['itemId'] for i in items['items']) == 1
    prefix = 'MemoryTemp/CurrentMap_EmbeddedFiles/ContentLoaded/Items/' + manifest['embeddedFolder'] + '/'
    loaded = game.call('ControlFids', {'action': 'preload', 'drive': 'fake',
        'path': prefix + 'TMIS_MatRemap_Candidate.Item.Gbx'})
    probe.save(out / 'candidate.json', loaded)
    graph = probe.effective_graph(loaded['nod']['ptr'])
    probe.save(out / 'effective.json', graph)
    native = game.call('ControlFids', {'action': 'get', 'drive': 'game',
        'path': 'GameData/Stadium/Media/Material/ItemFlag.Material.Gbx', 'includeNod': True})
    probe.save(out / 'native-material.json', native)
    cloth = [n for rec in graph['records'] for n in rec.get('nodes', []) if 'cloth' in n]
    assert len(cloth) == 1
    assert cloth[0]['cloth']['structured'] == 5
    assert cloth[0]['earlyMaterials'] == [native['fid']['nod']['ptr']]
    probe.save(out / 'verdict.json', {'normalEmbeddedLoad': True, 'effectiveStructuredVisuals': 5,
        'nativeMaterialIdentity': True, 'scope': 'Runtime graph only; visual motion and clean client require separate checks'})
    print(json.dumps({'embedded': True, 'structured': 5, 'material': native['fid']['nod']['ptr']}))


if __name__ == '__main__':
    main()
