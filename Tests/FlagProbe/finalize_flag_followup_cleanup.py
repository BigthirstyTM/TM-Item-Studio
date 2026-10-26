"""Archive exact disposable camera/empty-map fixtures and verify final state."""
import hashlib
import json
from pathlib import Path
import shutil

import game

ROOT = Path(__file__).resolve().parents[2]
BASE = ROOT / 'artifacts/flag-file-followup-20261022'
OUT = BASE / 'final-recovery'
MAPS = Path.home() / 'Documents/Trackmania/Maps'


def read_json(path):
    data = path.read_bytes()
    return json.loads(data.decode('utf-16' if data.startswith(b'\xff\xfe') else 'utf-8-sig'))


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    game.destination = OUT
    assert game.call('GetMode')['mode'] == 'Menu'
    rows = []
    for label in ['centered', 'near', 'view', 'close']:
        receipt = read_json(BASE / f'plugin-free/{label}-map.json')
        rows.append({'path': receipt['output'], 'sha256': receipt['sha256']})
    rows.append(read_json(BASE / 'embedded-context/staging.json'))
    rows.append({'path': str(MAPS / '000_TMISTest/TMIS_MinimalFlags_20261022.Map.Gbx'),
        'sha256': '42A6CABBD5B3569621A4540E33C24837012F0D309ACD68B54475633839A22E22'})
    for row in rows:
        source = Path(row['path']).resolve()
        assert source.is_relative_to(MAPS.resolve())
        assert digest(source) == row['sha256'].lower()
        target = OUT / 'archived-disposable-maps' / source.relative_to(MAPS.resolve())
        assert not target.exists()
        row['archived'] = str(target)
    for row in rows:
        target = Path(row['archived'])
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.move(row['path'], target)
        assert digest(target) == row['sha256'].lower()
    staging = read_json(BASE / 'plugin-free/staging.json')
    assert all(not Path(row['path']).exists() for row in staging['assets'])
    assert digest(Path(staging['proxy']['path'])) == staging['proxy']['sha256']
    assert not Path(staging['proxy']['held']).exists()
    plugins = game.call('ListPlugins')
    ids = {p['id'] for p in plugins['plugins']}
    helpers = {'TMISFlagGpuPlacement', 'TMISFlagMaterialProbe', 'TMISVanillaFlagCopy', 'TMISNativeFlagPlacement'}
    assert not ids & helpers
    game.call('tm-mcp-pack-epp.SetAgentTag', {'tag': ''})
    result = {'archivedMaps': rows, 'customGameAssetsAbsent': True,
        'openplanetProxyRestored': True, 'heldProxyAbsent': True, 'flagHelpersAbsent': True,
        'agentTag': '', 'mode': game.call('GetMode'), 'readiness': game.call('GetReadiness'),
        'retainedLiveMap': str(MAPS / '000_TMISTest/TMIS_PluginFreeProof_20261022.Map.Gbx'),
        'note': 'Proof/example maps require installing the accepted models before opening.'}
    (OUT / 'cleanup.json').write_text(json.dumps(result, indent=2))
    shutil.copy2(Path.home() / 'OpenplanetNext/Openplanet.log', OUT / 'Openplanet-final.log')
    print(json.dumps(result, indent=2))


if __name__ == '__main__':
    main()
