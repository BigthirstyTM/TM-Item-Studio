"""Observe a saved map in a cold process with no custom inventory overlay."""
import hashlib
import json
from pathlib import Path
import time

import game
from read_flag_visuals import read, model_mesh, mesh_flags

BASE = Path(__file__).resolve().parents[2] / 'artifacts/flag-file-followup-20261022/catalog-free'
OUT = BASE / 'cold1'


def save(name, value):
    (OUT / (name + '.json')).write_text(json.dumps(value, indent=2))
    return value


def main():
    OUT.mkdir(exist_ok=False)
    game.destination = OUT
    for tool in ['ListToolPacks', 'tm-mcp-pack-epp.Ping', 'GetMode', 'GetReadiness']:
        save(tool.replace('.', '-'), game.call(tool))
    assert game.call('GetMode')['mode'] == 'Menu'
    plugins = save('plugins', game.call('ListPlugins'))
    assert not {'TMISFlagGpuPlacement', 'TMISFlagMaterialProbe', 'TMISVanillaFlagCopy',
        'TMISNativeFlagPlacement'} & {p['id'] for p in plugins['plugins']}
    game.call('OpenMapInEditor', {'path': '000_TMISTest/TMIS_PluginFreeProof_20261022.Map.Gbx'})
    for _ in range(90):
        if game.call('GetReadiness', {'want': 'editor'}).get('ready'):
            break
        time.sleep(.5)
    info = save('map', game.call('GetMapInfo'))
    items = save('items', game.call('GetItems', {'limit': 20}))
    inventory = save('inventory', game.call('tm-mcp-pack-epp.FindInventory', {'query': 'TMIS_Adjacent', 'limit': 20}))
    result = {'items': items['total'], 'catalogAbsent': True, 'structured': [],
        'scope': 'Saved-map loading; ordinary Openplanet baseline remains'}
    if items['total'] == 3:
        anchors = save('anchors', game.call('DevGetPointers', {'listAnchoredObjects': True, 'anchoredObjectsLimit': 10}))
        for anchor, item in zip(anchors['anchoredObjects'], items['items']):
            result['structured'].append(dict(name=item['idName'], **mesh_flags(model_mesh(read(anchor['ptr'], 0x158)))))
    save('result', result)
    save('screenshot', game.call('TakeScreenshot', {'currentView': True, 'hideOverlay': True,
        'format': 'webp', 'forceRes': True, 'width': 1920, 'height': 1080}))
    print(json.dumps(result, indent=2))


if __name__ == '__main__':
    main()
