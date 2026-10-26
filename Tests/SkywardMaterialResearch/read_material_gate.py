"""Read-only diagnostics for an already open Skyward material candidate map.

Uses only Get*/ControlFids get/fromNod/DevSafeRead. Does not preload files,
write memory, alter the map/camera/selection, reload a plugin or stage assets.
Usage: python <this-script> <candidate-map-source> <new-output-directory>
All raw offsets are the previously audited Trackmania 2026.2.2.1751 layouts.
"""
import json
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'FlagProbe'))
import game
import material_remap_fixture as probe


def attempt(action):
    try:
        return action()
    except Exception as error:
        return {'error': str(error)}


def material_state(ptr):
    if not probe.ptr_int(ptr):
        return {'ptr': ptr, 'null': True}
    desc = probe.describe_node(ptr)
    assert desc['className'] == 'CPlugMaterial', desc
    count = probe.read(ptr, 0x40, 'u32')
    assert 0 <= count <= 8, count
    table = probe.read(ptr, 0x38)
    slots = []
    for i in range(count):
        cached = probe.read(table, i * 0x38 + 0x30)
        slots.append({'index': i,
            'fidFields': [probe.read(table, i * 0x38 + o) for o in (8, 0x10, 0x18)],
            'selected': probe.read(table, i * 0x38 + 0x28), 'cached': cached,
            'cachedDescription': attempt(lambda: probe.describe_node(cached)) if probe.ptr_int(cached) else None})
    return {'ptr': ptr, 'description': desc, 'materialFid': probe.read(ptr, 8), 'shaderSlots': slots}


def main():
    source = Path(sys.argv[1]).resolve()
    out = Path(sys.argv[2]).resolve()
    assert not out.exists(), 'Preserve prior evidence'
    manifest = json.loads(Path(str(source) + '.manifest.json').read_text())
    out.mkdir()
    game.destination = out
    info = game.call('GetMapInfo')
    assert info['name'] == manifest['MapName'], (info['name'], manifest['MapName'])
    probe.save(out / 'map.json', info)
    graph = probe.effective_graph()
    probe.save(out / 'effective-graph.json', graph)
    prefix = 'MemoryTemp/CurrentMap_EmbeddedFiles/ContentLoaded/Items/' + manifest['embeddedFolder'] + '/'
    fids = {}
    for path in [e['zipPath'].split('/')[-1] for e in manifest['embeddedFiles']]:
        fids[path] = attempt(lambda path=path: game.call('ControlFids', {
            'action': 'get', 'drive': 'fake', 'path': prefix + path, 'includeNod': True}))
    for path in ('GameData/Stadium/Media/Material/ItemFlag.Material.Gbx',
                 'GameData/Techno3/Media/Material/Tech3_Warp_TDiffSpec_VertexTween.Material.gbx',
                 'GameData/Stadium/Media/Texture/ItemSpectator_D.Texture.gbx',
                 'GameData/Stadium/Media/Texture/ItemSpectator_R.Texture.gbx'):
        fids[path] = attempt(lambda path=path: game.call('ControlFids', {
            'action': 'get', 'drive': 'game', 'path': path, 'includeNod': True}))
    probe.save(out / 'fids.json', fids)
    materials = {}
    for row in graph['records']:
        for node in row.get('nodes', []):
            if 'cloth' in node:
                for ptr in node['earlyMaterials']:
                    materials[ptr] = attempt(lambda ptr=ptr: material_state(ptr))
    modifier_states = []
    for name, row in fids.items():
        nod = row.get('fid', {}).get('nod', {})
        ptr = nod.get('ptr')
        if not nod.get('loaded') or not ptr:
            continue
        if nod['className'] == 'CPlugMaterial':
            materials[ptr] = attempt(lambda ptr=ptr: material_state(ptr))
        if nod['className'] == 'CGameItemModel':
            modifier = probe.read(ptr, 0x148)
            state = {'name': name, 'item': ptr, 'modifier': modifier}
            if probe.ptr_int(modifier):
                state['skin'] = probe.read(modifier, 0x18)
                state['folder'] = probe.read(modifier, 0x28)
                if probe.ptr_int(state['skin']):
                    state['sourceCount'] = probe.read(state['skin'], 0x60, 'u32')
                    assert 0 <= state['sourceCount'] <= 16, state
                    table = probe.read(state['skin'], 0x58)
                    state['sourceFids'] = [probe.read(table, i * 8) for i in range(state['sourceCount'])]
            modifier_states.append(state)
    probe.save(out / 'material-gates.json', {'materials': materials, 'modifiers': modifier_states,
        'scope': 'Already loaded FIDs and effective graph only; no preloads or mutation'})
    print(json.dumps({'out': str(out), 'materials': materials, 'modifiers': modifier_states}))


if __name__ == '__main__':
    main()
