"""Read-only verification after normal load of the three embedded shape items."""
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
    assert hashlib.sha256(source.read_bytes()).hexdigest().upper() == manifest['outputSha256']
    anchors = manifest['anchors']
    assert len(anchors) == 3
    baseline = {tool: game.call(tool) for tool in ['GetMode', 'ListPlugins', 'ListToolPacks']}
    probe.save(out / 'baseline.json', baseline)
    assert baseline['GetMode']['mode'] == 'Menu'
    assert not any('TMIS' in p['id'] for p in baseline['ListPlugins']['plugins'])
    names = {'TMIS_MatRemap_Placeholder.Material.Gbx', 'TMIS_MatRemap.GameSkin.Gbx',
             'TMIS_MatRemap.TerrainModifier.Gbx'} | {a['itemId'].split('\\')[-1] for a in anchors}
    loose = []
    roots = [Path.home() / 'Documents/Trackmania/Items',
             Path('C:/Program Files (x86)/Ubisoft/Ubisoft Game Launcher/games/Trackmania/GameData/Stadium/Items')]
    for root in roots:
        loose.extend(str(p) for p in root.rglob('*.Gbx') if p.name in names)
    probe.save(out / 'loose-isolation.json', {'roots': [str(r) for r in roots], 'matchingLooseFiles': loose})
    assert not loose
    target = Path.home() / 'Documents/Trackmania/Maps/TMIS_MatRemap_Research' / source.name
    if not target.exists():
        shutil.copy2(source, target)
    assert target.read_bytes() == source.read_bytes()
    probe.save(out / 'staged-map.json', {'path': str(target), 'sha256': manifest['outputSha256']})
    probe.save(out / 'open.json', game.call('OpenMapInEditor', {'path': str(target), 'waitMs': 1500}))
    for _ in range(160):
        if game.call('GetReadiness', {'want': 'editor'}).get('ready'):
            break
        time.sleep(.25)
    else:
        raise RuntimeError('Editor readiness timed out')
    info = game.call('GetMapInfo')
    probe.save(out / 'map.json', info)
    assert info['nbItems'] == 3 and info['name'] == manifest['MapName']
    items = game.call('GetItems', {'limit': 8})
    probe.save(out / 'items.json', items)
    assert items['total'] == 3
    assert {i['idName'] for i in items['items']} == {a['itemId'] for a in anchors}
    graph = probe.effective_graph()
    probe.save(out / 'effective.json', graph)
    native = game.call('ControlFids', {'action': 'get', 'drive': 'game',
        'path': 'GameData/Stadium/Media/Material/ItemFlag.Material.Gbx', 'includeNod': True})
    probe.save(out / 'native-material.json', native)
    native_ptr = native['fid']['nod']['ptr']
    cloth = [n for rec in graph['records'] for n in rec.get('nodes', []) if 'cloth' in n]
    assert len(cloth) == 3 and all(n['cloth']['structured'] == 5 for n in cloth)
    assert all(n['earlyMaterials'] == [native_ptr] for n in cloth)
    probe.save(out / 'verdict.json', {'embeddedItems': 3, 'structuredClothVisuals': 15,
        'exactNativeMaterial': True, 'noMatchingLooseAssets': True,
        'scope': 'Normal runtime graph; general Openplanet loaded; visible motion and no-Openplanet separately gated'})
    print('All three embedded shapes: 15/15 structured visuals, exact native material, no matching loose files')


if __name__ == '__main__':
    main()
