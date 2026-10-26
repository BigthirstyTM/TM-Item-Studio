"""Preload-only shader and FID gates for a uniquely packaged empty map.

Never places or draws an item, writes process memory, or loads a helper plugin.
Each invocation requires a new evidence directory. Uses the existing DEV read
bridge for the fixed Trackmania build, not an authoring/runtime dependency.
"""
import hashlib
import json
from pathlib import Path
import shutil
import sys
import time

import game
import material_remap_fixture as probe


def candidate_item_name(manifest):
    legacy_name = 'TMIS_MatRemap_Candidate.Item.Gbx'
    if 'expectedEmbeddedItemIds' in manifest:
        paths = manifest['expectedEmbeddedItemIds']
        assert isinstance(paths, list), 'Expected embedded Item identities must be a list'
    elif 'embeddedFiles' in manifest:
        paths = [entry['zipPath'] for entry in manifest['embeddedFiles']
                 if entry['zipPath'].lower().endswith('.item.gbx')]
    else:
        return legacy_name
    assert paths and all(isinstance(path, str) and path.lower().endswith('.item.gbx')
                         for path in paths), 'Expected at least one embedded Item path'
    names = [path.replace('\\', '/').rsplit('/', 1)[-1] for path in paths]
    if len(names) == 1:
        return names[0]
    # Original v4-v6 fixtures also contain a control Item beside this candidate.
    assert names.count(legacy_name) == 1, 'Expected one Item or the unique legacy candidate'
    return legacy_name


def main():
    source = Path(sys.argv[1]).resolve()
    out = Path(sys.argv[2]).resolve()
    out.mkdir(exist_ok=False)
    game.destination = out
    adjacent = Path(str(source) + '.manifest.json')
    if adjacent.exists():
        manifest = json.loads(adjacent.read_text())
    else:
        package = json.loads((source.parent / 'manifest.json').read_text())
        assert package['operation'] == 'material-remap-v6-external-mesh-only'
        rows = [m for m in package['maps'] if Path(m['path']) == source]
        assert len(rows) == 1
        row = rows[0]
        manifest = {'anchoredObjectCount': row['anchorCount'], 'outputSha256': row['sha256'],
                    'MapName': row['MapName'], 'embeddedFolder': package['embeddedFolder'],
                    'expectedEmbeddedItemIds': [row['expectedEmbeddedItemId']]}
    candidate_name = candidate_item_name(manifest)
    assert manifest['anchoredObjectCount'] == 0
    assert hashlib.sha256(source.read_bytes()).hexdigest().upper() == manifest['outputSha256']
    baseline = {tool: game.call(tool) for tool in [
        'ListToolPacks', 'tm-mcp-pack-epp.Ping', 'GetMode', 'ListPlugins']}
    probe.save(out / 'baseline.json', baseline)
    assert baseline['GetMode']['mode'] == 'Menu'
    assert not any('TMIS' in p['id'] for p in baseline['ListPlugins']['plugins'])
    target = Path.home() / 'Documents/Trackmania/Maps/TMIS_MatRemap_Research' / source.name
    assert not target.exists()
    target.parent.mkdir(exist_ok=True)
    shutil.copy2(source, target)
    assert source.read_bytes() == target.read_bytes()
    probe.save(out / 'staged-map.json', {'path': str(target), 'sha256': manifest['outputSha256']})
    probe.save(out / 'open.json', game.call('OpenMapInEditor', {'path': str(target), 'waitMs': 1500}))
    for _ in range(80):
        if game.call('GetReadiness', {'want': 'editor'}).get('ready'):
            break
        time.sleep(.25)
    else:
        raise RuntimeError('Empty editor did not become ready')
    info = game.call('GetMapInfo')
    probe.save(out / 'map.json', info)
    assert info['nbItems'] == 0 and info['name'] == manifest['MapName']
    prefix = 'MemoryTemp/CurrentMap_EmbeddedFiles/ContentLoaded/Items/' + manifest['embeddedFolder'] + '/'
    probe.save(out / 'embedded-files.json', game.call('ControlFids', {
        'action': 'list', 'drive': 'fake', 'path': prefix}))
    loaded = game.call('ControlFids', {'action': 'preload', 'drive': 'fake',
        'path': prefix + candidate_name})
    probe.save(out / 'candidate.json', loaded)
    item = loaded['nod']['ptr']
    mesh = probe.model_mesh(item)
    material = probe.read(probe.read(mesh, 0xC8), 0)
    shader_count = probe.read(material, 0x40, 'u32')
    slots = probe.read(material, 0x38)
    assert 0 < shader_count <= 8, 'Do not build a source with zero shader slots'
    rows = []
    for i in range(shader_count):
        row = {'index': i, 'fidFields': [probe.read(slots, i * 0x38 + o) for o in [8, 0x10, 0x18]],
               'selected': probe.read(slots, i * 0x38 + 0x28),
               'cached': probe.read(slots, i * 0x38 + 0x30)}
        rows.append(row)
        # Native inline mode puts the actual shader node in cached +0x30.
        # The earlier fields are FIDs and must never be treated as CMwNod.
        assert probe.ptr_int(row['cached']), 'Source shader must be nonnull before build'
        row['shaderClass'] = probe.describe_node(row['cached'])
    modifier = probe.read(item, 0x148)
    skin = probe.read(modifier, 0x18)
    fid = probe.read(probe.read(skin, 0x58), 0)
    result = {'sourceMesh': probe.mesh_flags(mesh), 'material': material,
              'materialFid': probe.read(material, 8), 'gameSkinSourceFid': fid,
              'replacementFolder': probe.read(modifier, 0x28), 'shaderSlots': rows,
              'scope': 'Embedded preload only; no candidate instance built or drawn'}
    probe.save(out / 'preload-gates.json', result)
    assert fid == result['materialFid'] and probe.ptr_int(fid)
    assert probe.ptr_int(result['replacementFolder'])
    assert game.call('GetItems', {'limit': 8})['total'] == 0
    print(json.dumps(result))


if __name__ == '__main__':
    main()
