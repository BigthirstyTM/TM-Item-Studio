"""Bounded native material-remap experiment; evidence is append-only.

No process writes or flag helper plugins. Raw/source preload is deliberately
separate from the later effective instance build.
"""
import hashlib
import json
from pathlib import Path
import shutil
import sys
import time

import game
from read_flag_visuals import read, model_mesh, mesh_flags

ROOT = Path(__file__).resolve().parents[2]
BASE = ROOT / 'artifacts/flag-selfcontained-20261022'
GEN = sys.argv[2] if __name__ == '__main__' and len(sys.argv) > 2 else 'v1'
assert GEN in {'v1', 'v2', 'v3'}
FILES = BASE / ('material-remap-' + GEN)
LIVE = Path.home() / 'Documents/Trackmania/Items' / ('TMIS_MatRemap_20261022' + ('_' + GEN if GEN != 'v1' else ''))


def save(path, data):
    assert not path.exists(), 'Preserve prior evidence: ' + str(path)
    path.write_text(json.dumps(data, indent=2) + '\n')


def ptr_int(value):
    return int(value, 0) if isinstance(value, str) else int(value)


def describe_node(ptr):
    assert ptr_int(ptr) != 0 and ptr_int(ptr) % 8 == 0
    try:
        result = game.call('ControlFids', {'action': 'fromNod', 'from_ptr': ptr})
    except RuntimeError as error:
        envelope = json.loads(str(error))
        result = envelope.get('data', {}).get('result', {}).get('output', {})
        assert result.get('hasFid') is False and result.get('nod', {}).get('loaded'), envelope
    assert result['nod']['ptr'] == (ptr if isinstance(ptr, str) else f'0x{ptr:016X}')
    return result['nod']


def effective_graph(expected_item=None):
    scene = game.call('DevSceneComponents', {'maxSlots': 8})
    managers = [m for m in scene['components'] if m['name'] == 'NGameItem_SMgr']
    assert len(managers) == 1
    manager = managers[0]['ptr']
    count = read(manager, 0x40, 'u32')
    assert 0 <= count <= 8
    table = ptr_int(read(manager, 0x38))
    records = []
    for i in range(count):
        rec = table + i * 0x138
        model = read(rec, 0)
        if expected_item is not None and model != expected_item:
            continue
        handle = read(rec, 0x128)
        row = {'index': i, 'record': f'0x{rec:016X}', 'item': model,
               'position': read(rec, 0x2C, 'vec3'), 'source': read(rec, 0x120),
               'handle': handle, 'cacheChain': []}
        if ptr_int(handle):
            cursor = handle
            visited = set()
            for _ in range(16):
                value = ptr_int(cursor)
                assert value and value % 8 == 0 and value not in visited
                visited.add(value)
                result = read(cursor, 0x10)
                next_ptr = read(cursor, 0x48)
                row['cacheChain'].append({'entry': cursor, 'result': result, 'next': next_ptr})
                if ptr_int(result):
                    break
                if not ptr_int(next_ptr):
                    result = read(handle, 8)
                    row['fallbackToOriginalEntryBase'] = True
                    break
                cursor = next_ptr
            else:
                raise RuntimeError('Model cache chain exceeded bound')
            row['effective'] = result
            row['nodes'] = walk_graph(result) if ptr_int(result) else []
        assert read(rec, 0) == model and read(rec, 0x128) == handle, 'Record changed during sampling'
        records.append(row)
    return {'scene': scene, 'records': records}


def walk_graph(root):
    nodes = []
    visited = set()

    def visit(ptr, depth):
        assert depth <= 8 and len(visited) < 64
        if ptr_int(ptr) in visited:
            return
        visited.add(ptr_int(ptr))
        node = describe_node(ptr)
        row = {'ptr': ptr, 'class': node['className'], 'classId': node['classIdHex']}
        nodes.append(row)
        if node['className'] == 'CPlugPrefab':
            count = read(ptr, 0x48, 'u32')
            assert count <= 16
            table = ptr_int(read(ptr, 0x40))
            row['entities'] = count
            for i in range(count):
                child = read(table, i * 0x50 + 8)
                if ptr_int(child):
                    visit(child, depth + 1)
        elif node['className'] == 'CPlugDynaObjectModel':
            child = read(ptr, 0x20)
            if ptr_int(child):
                visit(child, depth + 1)
        elif node['className'] == 'CGameObjectVisModel':
            child = read(ptr, 0x1B0)
            if ptr_int(child):
                visit(child, depth + 1)
        elif node['className'] == 'CPlugSolid2Model':
            count = read(ptr, 0xB0, 'u32')
            assert count <= 8
            row['visualCount'] = count
            if count == 5:
                row['cloth'] = mesh_flags(ptr)
            early_count = read(ptr, 0xD0, 'u32')
            assert early_count <= 8
            early = read(ptr, 0xC8)
            row['earlyMaterials'] = [read(early, i * 8) for i in range(early_count)]

    if ptr_int(root):
        visit(root, 0)
    return nodes


def preflight():
    out = BASE / 'runtime-preflight'
    out.mkdir(exist_ok=False)
    game.destination = out
    results = {}
    for tool, args in [
        ('ListToolPacks', {}), ('tm-mcp-pack-epp.Ping', {}),
        ('GetMode', {}), ('GetReadiness', {}), ('ListPlugins', {}),
    ]:
        results[tool] = game.call(tool, args)
    assert results['GetMode']['mode'] == 'Menu'
    forbidden = {'TMISFlagGpuPlacement', 'TMISFlagMaterialProbe',
                 'TMISVanillaFlagCopy', 'TMISNativeFlagPlacement'}
    assert not forbidden & {p['id'] for p in results['ListPlugins']['plugins']}
    save(out / 'baseline.json', results)
    for name in ['CGameCtnAnchoredObject', 'CGameItemModel',
                 'CPlugGameSkinAndFolder', 'CPlugGameSkin',
                 'CGameObjectVisModel', 'CPlugPrefab']:
        save(out / (name + '.json'), game.call('GetTypeInfo', {'className': name, 'limit': 400}))
    expected = json.loads((ROOT / 'artifacts/flag-file-followup-20261022/protected-final-hashes.json').read_text(encoding='utf-8-sig'))
    hashes = []
    for record in expected:
        actual = hashlib.sha256(Path(record['Path']).read_bytes()).hexdigest().upper()
        assert actual == record['Hash'], record['Path']
        hashes.append({'path': record['Path'], 'sha256': actual})
    save(out / 'protected-assets.json', hashes)
    print(json.dumps({'mode': 'Menu', 'flagHelpers': False, 'protectedAssets': len(hashes), 'out': str(out)}))


def prepare_editor():
    out = BASE / 'editor-setup'
    out.mkdir(exist_ok=False)
    game.destination = out
    assert game.call('GetMode')['mode'] == 'Menu'
    save(out / 'entry.json', game.call('EditNewMap', {'vista': 'stadium155-day'}))
    for _ in range(80):
        if game.call('GetMode')['mode'] == 'MapEditor':
            break
        time.sleep(.25)
    else:
        raise RuntimeError('Map editor did not become ready')
    before = game.call('GetMapInfo')
    save(out / 'empty-map.json', before)
    assert before['nbItems'] == 0 and before['name'] == 'Unnamed'
    save(out / 'readiness.json', game.call('GetReadiness', {'want': 'editor'}))
    game.call('tm-mcp-pack-epp.SetAgentTag', {'tag': 'tmis:material-remap-20261022'})
    save(out / 'camera-set.json', game.call('SetEditorCamera', {
        'x': 128, 'y': 16, 'z': 128, 'distance': 16,
        'hAngle': 0, 'vAngle': 0, 'animate': False,
    }))
    time.sleep(.5)
    save(out / 'camera.json', game.call('GetEditorCamera'))
    save(out / 'scene.json', game.call('DevSceneComponents', {'maxSlots': 8}))
    print(json.dumps({'map': 'Unnamed', 'items': 0, 'out': str(out)}))


def stage_preload():
    out = BASE / ('preload-' + GEN)
    out.mkdir(exist_ok=False)
    game.destination = out
    assert game.call('GetMode')['mode'] == 'MapEditor'
    assert game.call('GetMapInfo')['nbItems'] == 0
    assert not LIVE.exists(), 'Refuse existing user asset folder'
    source = FILES
    manifest = json.loads((source / 'manifest.json').read_text())
    LIVE.mkdir()
    for record in manifest['files']:
        name = record['file']
        assert Path(name).name == name
        src = source / name
        assert hashlib.sha256(src.read_bytes()).hexdigest().upper() == record['sha256']
        shutil.copy2(src, LIVE / name)
        assert hashlib.sha256((LIVE / name).read_bytes()).hexdigest().upper() == record['sha256']
    save(out / 'staged.json', {'folder': str(LIVE), 'files': manifest['files']})
    log = Path.home() / 'OpenplanetNext/Openplanet.log'
    save(out / 'log-mark.json', {'file': str(log), 'offset': log.stat().st_size})
    save(out / 'refresh.json', game.call('ControlFids', {'action': 'updateTree', 'drive': 'user', 'path': 'Items'}))
    probe_preloads(out)


def probe_preloads(out=None):
    if out is None:
        out = BASE / ('preload-resume-' + GEN)
        out.mkdir(exist_ok=False)
    game.destination = out
    assert game.call('GetMapInfo')['nbItems'] == 0
    manifest = json.loads((FILES / 'manifest.json').read_text())
    results = {}
    for name in [manifest['control'], manifest['candidate']]:
        loaded = game.call('ControlFids', {'action': 'preload', 'drive': 'user', 'path': 'Items/' + LIVE.name + '/' + name})
        assert loaded['nod']['className'] == 'CGameItemModel', loaded
        item = loaded['nod']['ptr']
        mesh = model_mesh(item)
        value = {'loaded': loaded, 'sourceMesh': mesh_flags(mesh)}
        assert read(mesh, 0xD0, 'u32') == 1, 'Expected one early CPlugMaterial'
        mat = read(read(mesh, 0xC8), 0)
        value.update(earlyMaterial=mat, earlyMaterialFid=read(mat, 8))
        modifier = read(item, 0x148)
        value['modifier'] = modifier
        if name == manifest['candidate']:
            assert ptr_int(modifier) != 0
            skin = read(modifier, 0x18)
            assert ptr_int(skin) != 0
            assert read(skin, 0x60, 'u32') == 1, 'Expected one source-FID mapping'
            fid = read(read(skin, 0x58), 0)
            folder = read(modifier, 0x28)
            value.update(skin=skin, sourceFid=fid, replacementFolder=folder,
                         sourceFidMatchesMaterial=(fid == value['earlyMaterialFid']))
            save(out / (name + '.observed.json'), value)
            assert fid == value['earlyMaterialFid'] and ptr_int(fid) != 0, 'Mapping must match the actual material FID'
            assert ptr_int(folder) != 0, 'Global replacement folder was not resolved'
        else:
            assert ptr_int(modifier) == 0
        results[name] = value
        save(out / (name + '.json'), value)
    save(out / 'results.json', results)
    print(json.dumps({name: {'structured': value['sourceMesh']['structured'],
                            'modifier': value['modifier']} for name, value in results.items()}))


def build_offscreen():
    out = BASE / ('offscreen-build-' + GEN + '-retry')
    out.mkdir(exist_ok=False)
    game.destination = out
    manifest = json.loads((FILES / 'manifest.json').read_text())
    accepted = json.loads((BASE / ('preload-' + GEN) / 'results.json').read_text())
    assert accepted[manifest['candidate']]['sourceFidMatchesMaterial']
    before = game.call('GetMapInfo')
    save(out / 'map-before.json', before)
    assert before['name'] == 'Unnamed' and before['nbItems'] == 0
    game.call('SetEditorCamera', {'x': 128, 'y': 16, 'z': 128,
                                'distance': 16, 'hAngle': 180, 'vAngle': 0, 'animate': False})
    time.sleep(.5)
    camera = game.call('GetEditorCamera')
    save(out / 'camera-before.json', camera)
    position = [1460, 8, 1360]
    direction = [t - p for t, p in zip(camera['target'], camera['position'])]
    towards_item = [t - p for t, p in zip(position, camera['position'])]
    assert sum(a * b for a, b in zip(direction, towards_item)) < 0, 'Candidate must be behind the camera'
    inventory = game.call('tm-mcp-pack-epp.FindInventory', {'query': 'TMIS_MatRemap_Candidate', 'limit': 20})
    save(out / 'inventory.json', inventory)
    result = game.call('tm-mcp-pack-epp.PlaceItem', {
        'itemPath': LIVE.name + '/' + manifest['candidate'].removesuffix('.Item.Gbx'),
        'x': position[0], 'y': position[1], 'z': position[2],
        'yaw': 1.57079632679, 'autofocus': False,
        'tag': 'tmis:material-remap-20261022',
    })
    save(out / 'placement.json', result)
    assert result['placed'] and result['afterItems'] == 1
    items = game.call('GetItems', {'limit': 8})
    save(out / 'items.json', items)
    assert items['total'] == 1 and items['items'][0]['idName'] == 'TMIS_MatRemap_Candidate'
    save(out / 'camera-after.json', game.call('GetEditorCamera'))
    current = game.call('ControlFids', {'action': 'preload', 'drive': 'user',
                                     'path': 'Items/' + LIVE.name + '/' + manifest['candidate']})
    item_ptr = current['nod']['ptr']
    summaries = []
    for i in range(4):
        time.sleep(.5)
        graph = effective_graph(item_ptr)
        save(out / ('effective-' + str(i) + '.json'), graph)
        cloth = [n for rec in graph['records'] for n in rec.get('nodes', []) if 'cloth' in n]
        summaries.append([{'ptr': n['ptr'], 'structured': n['cloth']['structured'],
                           'materials': n['earlyMaterials']} for n in cloth])
        if cloth and all(n['cloth']['structured'] == 5 for n in cloth):
            break
    save(out / 'summary.json', summaries)
    save(out / 'native-material.json', game.call('ControlFids', {
        'action': 'get', 'drive': 'game', 'path': 'GameData/Stadium/Media/Material/ItemFlag.Material.Gbx',
        'includeNod': True,
    }))
    print(json.dumps({'offscreenOnly': True, 'samples': summaries, 'out': str(out)}))


def refresh_inventory():
    out = BASE / ('inventory-' + GEN)
    out.mkdir(exist_ok=False)
    game.destination = out
    assert game.call('GetMapInfo')['nbItems'] == 0
    game.call('tm-mcp-pack-epp.RefreshInventory')
    time.sleep(1)
    initial = game.call('tm-mcp-pack-epp.FindInventory', {'query': 'TMIS_MatRemap', 'limit': 20})
    save(out / 'before.json', initial)
    # The article tree itself predates the new sibling files. Reopening this
    # verified empty disposable map rebuilds that tree; no user map is discarded.
    assert game.call('GetMapInfo')['name'] == 'Unnamed'
    result = game.call('BackToMainMenu')
    if result.get('blockedByDialog'):
        game.call('RespondDialog', {'action': 'no'})
    assert game.call('GetMode')['mode'] == 'Menu'
    game.call('EditNewMap', {'vista': 'stadium155-day'})
    for _ in range(80):
        if game.call('GetReadiness', {'want': 'editor'}).get('ready'):
            break
        time.sleep(.25)
    else:
        raise RuntimeError('Editor readiness timed out')
    game.call('tm-mcp-pack-epp.SetAgentTag', {'tag': 'tmis:material-remap-20261022'})
    time.sleep(1)
    found = game.call('tm-mcp-pack-epp.FindInventory', {'query': 'TMIS_MatRemap', 'limit': 20})
    save(out / 'after.json', found)
    print(json.dumps(found))


def helper_place(resume=False):
    assert GEN in {'v2', 'v3'}
    out = BASE / ('placement-helper-' + GEN + ('-retry' if resume else ''))
    out.mkdir(exist_ok=False)
    game.destination = out
    baseline = game.call('ListPlugins')
    save(out / 'plugins-before.json', baseline)
    assert 'TMISMaterialRemapPlacement' not in {p['id'] for p in baseline['plugins']}
    assert game.call('GetMapInfo')['name'] == 'Unnamed'
    assert game.call('GetMapInfo')['nbItems'] == 0
    game.call('SetEditorCamera', {'x': 128, 'y': 16, 'z': 128, 'distance': 16,
                                'hAngle': 180, 'vAngle': 0, 'animate': False})
    time.sleep(.5)
    save(out / 'camera-before.json', game.call('GetEditorCamera'))
    source = ROOT / 'Tests/Openplanet/TMISMaterialRemapPlacement'
    target = Path.home() / 'OpenplanetNext/Plugins/TMISMaterialRemapPlacement'
    if not resume:
        assert not target.exists()
        target.mkdir()
    staged = []
    for name in ['info.toml', 'Main.as']:
        if not resume:
            shutil.copy2(source / name, target / name)
        a = (source / name).read_bytes()
        assert (target / name).read_bytes() == a
        staged.append({'source': str(source / name), 'target': str(target / name),
                       'sha256': hashlib.sha256(a).hexdigest()})
    save(out / 'staged.json', staged)
    marker = LIVE / 'place.enable'
    if not resume:
        assert not marker.exists()
        marker.write_text('One fixed ordinary item placement; no render-state writes.\n')
    assert marker.read_text() == 'One fixed ordinary item placement; no render-state writes.\n'
    log = Path.home() / 'OpenplanetNext/Openplanet.log'
    offset = log.stat().st_size
    save(out / 'log-mark.json', {'path': str(log), 'offset': offset})
    save(out / 'load.json', game.call('ControlPlugin', {'action': 'load', 'path': target.as_posix() + '/'}))
    time.sleep(2)
    fresh = log.read_bytes()[offset:].decode('utf-8', errors='replace')
    (out / 'load.log').write_text(fresh)
    assert '[TMIS-MATREMAP-PLACE] RESULT placed=true items=1' in fresh, fresh
    assert 'Loaded' in fresh and 'TMISMaterialRemapPlacement' in fresh, fresh
    assert '[TMIS-MATREMAP-PLACE] FAIL' not in fresh
    marker.unlink()
    save(out / 'unload.json', game.call('ControlPlugin', {'action': 'unload', 'id': 'TMISMaterialRemapPlacement'}))
    time.sleep(.5)
    after = game.call('ListPlugins')
    save(out / 'plugins-after.json', after)
    assert 'TMISMaterialRemapPlacement' not in {p['id'] for p in after['plugins']}
    assert {p['id'] for p in after['plugins']} == {p['id'] for p in baseline['plugins']}
    items = game.call('GetItems', {'limit': 8})
    save(out / 'items.json', items)
    assert items['total'] == 1 and 'TMIS_MatRemap_Candidate' in items['items'][0]['idName']
    save(out / 'camera-after.json', game.call('GetEditorCamera'))
    current = game.call('ControlFids', {'action': 'preload', 'drive': 'user',
                                     'path': 'Items/' + LIVE.name + '/TMIS_MatRemap_Candidate.Item.Gbx'})
    graph = effective_graph(current['nod']['ptr'])
    save(out / 'effective.json', graph)
    save(out / 'native-material.json', game.call('ControlFids', {'action': 'get', 'drive': 'game',
        'path': 'GameData/Stadium/Media/Material/ItemFlag.Material.Gbx', 'includeNod': True}))
    cloth = [n for rec in graph['records'] for n in rec.get('nodes', []) if 'cloth' in n]
    print(json.dumps({'helperUnloaded': True, 'offscreenOnly': True,
                     'cloth': [{'ptr': n['ptr'], 'structured': n['cloth']['structured'],
                                'materials': n['earlyMaterials']} for n in cloth]}))


def cleanup_helper():
    out = BASE / ('helper-cleanup-' + GEN)
    out.mkdir(exist_ok=False)
    game.destination = out
    plugins = game.call('ListPlugins')
    save(out / 'before.json', plugins)
    if 'TMISMaterialRemapPlacement' in {p['id'] for p in plugins['plugins']}:
        save(out / 'unload.json', game.call('ControlPlugin', {'action': 'unload', 'id': 'TMISMaterialRemapPlacement'}))
        time.sleep(.5)
    marker = LIVE / 'place.enable'
    if marker.exists():
        marker.unlink()
    target = Path.home() / 'OpenplanetNext/Plugins/TMISMaterialRemapPlacement'
    assert target.resolve().parent == (Path.home() / 'OpenplanetNext/Plugins').resolve()
    assert {p.name for p in target.iterdir()} == {'Main.as', 'info.toml'}
    archive = out / 'source'
    archive.mkdir()
    for name in ['Main.as', 'info.toml']:
        shutil.copy2(target / name, archive / name)
        assert (target / name).read_bytes() == (archive / name).read_bytes()
        (target / name).unlink()
    target.rmdir()
    after = game.call('ListPlugins')
    save(out / 'after.json', after)
    assert 'TMISMaterialRemapPlacement' not in {p['id'] for p in after['plugins']}
    save(out / 'map.json', game.call('GetMapInfo'))
    print(json.dumps({'helperAbsent': True, 'mapItems': game.call('GetMapInfo')['nbItems']}))


def cold_prepare():
    out = BASE / 'cold1'
    out.mkdir(exist_ok=False)
    game.destination = out
    info = game.call('GetMapInfo')
    assert info['name'] == 'Unnamed' and info['nbItems'] == 0
    plugins = game.call('ListPlugins')
    assert 'TMISMaterialRemapPlacement' not in {p['id'] for p in plugins['plugins']}
    save(out / 'before-map.json', info)
    save(out / 'before-plugins.json', plugins)
    game.call('tm-mcp-pack-epp.SetAgentTag', {'tag': ''})
    response = game.call('BackToMainMenu')
    if response.get('blockedByDialog'):
        game.call('RespondDialog', {'action': 'no'})
    assert game.call('GetMode')['mode'] == 'Menu'
    receipts = []
    for version, folder in [('v1', 'TMIS_MatRemap_20261022'), ('v2', 'TMIS_MatRemap_20261022_v2')]:
        source = Path.home() / 'Documents/Trackmania/Items' / folder
        assert source.resolve().parent == (Path.home() / 'Documents/Trackmania/Items').resolve()
        manifest = json.loads((BASE / ('material-remap-' + version) / 'manifest.json').read_text())
        assert {p.name for p in source.iterdir()} == {r['file'] for r in manifest['files']}
        target = out / 'archived-user-controls' / version
        target.mkdir(parents=True)
        for r in manifest['files']:
            p = source / r['file']
            assert hashlib.sha256(p.read_bytes()).hexdigest().upper() == r['sha256']
            shutil.copy2(p, target / p.name)
            assert p.read_bytes() == (target / p.name).read_bytes()
            p.unlink()
        source.rmdir()
        receipts.append({'source': str(source), 'archive': str(target), 'files': manifest['files']})
    save(out / 'archived-controls.json', receipts)
    print(json.dumps({'mode': 'Menu', 'controlsArchived': ['v1', 'v2'], 'v3Retained': True}))


if __name__ == '__main__':
    {'preflight': preflight, 'prepare-editor': prepare_editor, 'stage-preload': stage_preload,
     'probe-preloads': probe_preloads, 'build-offscreen': build_offscreen,
     'refresh-inventory': refresh_inventory, 'helper-place': helper_place,
     'helper-resume': lambda: helper_place(resume=True), 'cleanup-helper': cleanup_helper,
     'cold-prepare': cold_prepare}[sys.argv[1]]()
