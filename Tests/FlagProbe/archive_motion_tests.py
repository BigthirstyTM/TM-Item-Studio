"""Hash-guarded restoration for the owned motion and split-graph fixtures."""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess

import game

ROOT = Path(__file__).resolve().parents[2]
BASE = ROOT / 'artifacts/flag-motion-embedded-20261022'
OUT = BASE / 'cleanup'
GAME = Path('C:/Program Files (x86)/Ubisoft/Ubisoft Game Launcher/games/Trackmania').resolve()
MAPS = (Path.home() / 'Documents/Trackmania/Maps').resolve()
VARIANTS = ['phase-range', 'local-motion', 'local-motion-v3', 'local-motion-v3-func1', 'nonkinematic-constraints']


def read_json(path):
    data = path.read_bytes()
    return json.loads(data.decode('utf-16' if data.startswith(b'\xff\xfe') else 'utf-8-sig'))


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def prepare(rows, root, category):
    for row in rows:
        source = Path(row['path']).resolve()
        assert source.is_relative_to(root), source
        assert source.is_file() and digest(source) == row['sha256'].lower(), source
        target = (OUT / category / source.relative_to(root)).resolve()
        assert target.is_relative_to(OUT.resolve()) and not target.exists(), target
        row['archived'] = str(target)


def main():
    OUT.mkdir(exist_ok=False)
    assets = read_json(BASE / 'kinematic/slow-staging/staging.json')
    maps = [read_json(BASE / 'kinematic/map-staging.json'), read_json(BASE / 'external-graph/map-staging.json')]
    for variant in VARIANTS:
        assets += read_json(BASE / variant / 'staging/files.json')
        maps.append(read_json(BASE / variant / 'staging/map.json'))
    assert len(assets) == 12 and len(maps) == 7
    assert len({row['path'] for row in assets + maps}) == 19
    prepare(assets, GAME, 'game-assets')
    prepare(maps, MAPS, 'maps')
    protected = read_json(ROOT / 'artifacts/flag-file-followup-20261022/protected-final-hashes.json')
    protected += [{'Path': str(GAME / 'Trackmania.exe'), 'Hash': '3FC7D8CDA542BEDA131C44306B123F4004D07D7E22F512B46B762AFC29F6EDDA'}]
    proof = read_json(ROOT / 'artifacts/flag-file-followup-20261022/plugin-free/proof-map.json')
    protected += [{'Path': proof['output'], 'Hash': proof['sha256']}]
    assert all(digest(Path(r['Path'])) == r['Hash'].lower() for r in protected)
    assert not (Path.home() / 'Documents/Trackmania/Items/TMIS_ExternalGraph').exists()
    game.destination = OUT
    assert game.call('GetMapInfo')['name'] == 'TMIS Nonkinematic Constraint Controls'
    result = game.call('BackToMainMenu')
    if result.get('blockedByDialog'):
        game.call('RespondDialog', {'action': 'no'})
    assert game.call('GetMode')['mode'] == 'Menu'
    plugins = game.call('ListPlugins')
    helpers = {'TMISFlagGpuPlacement', 'TMISFlagMaterialProbe', 'TMISNativeFlagPlacement', 'TMISVanillaFlagCopy'}
    assert not helpers & {p['id'] for p in plugins['plugins']}
    (OUT / 'plugins-before-stop.json').write_text(json.dumps(plugins, indent=2))
    stop = subprocess.run(['powershell', '-NoProfile', '-Command',
        "$flagMotionCleanupProcess=Get-Process Trackmania; if (@($flagMotionCleanupProcess).Count -ne 1) { throw 'Expected one process' }; $flagMotionCleanupProcess | Select-Object Id,StartTime | ConvertTo-Json; Stop-Process -Id $flagMotionCleanupProcess.Id"],
        capture_output=True, text=True, check=True)
    (OUT / 'stopped-process.json').write_text(stop.stdout)
    for row in assets + maps:
        source = Path(row['path']).resolve()
        assert digest(source) == row['sha256'].lower()
        target = Path(row['archived'])
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.move(source, target)
        assert not source.exists() and digest(target) == row['sha256'].lower()
    # Quarantine old labels together with the already rejected video.
    evidence = BASE / 'kinematic/video-slow'
    moved_labels = []
    for name in ['validation.json', 'poster.jpg', 'same-rigid-pose-different-cloth.jpg']:
        source = (evidence / name).resolve()
        assert source.is_relative_to(BASE.resolve()) and source.is_file()
        target = evidence / 'rejected-premature-label' / name
        assert not target.exists()
        sha = digest(source)
        shutil.move(source, target)
        assert digest(target) == sha
        moved_labels.append({'source': str(source), 'archived': str(target), 'sha256': sha})
    assert all(digest(Path(r['Path'])) == r['Hash'].lower() for r in protected)
    result = {'archivedAssets': assets, 'archivedMaps': maps, 'protectedHashes': protected,
        'quarantinedPrematureLabels': moved_labels, 'flagHelpersAbsent': True,
        'looseSplitGraphAbsent': True, 'gameStoppedDuringArchive': True,
        'note': 'Retained proof map still needs the separate accepted installation package. No experiment is a portable success.'}
    (OUT / 'restoration.json').write_text(json.dumps(result, indent=2))
    shutil.copy2(Path.home() / 'OpenplanetNext/Openplanet.log', OUT / 'Openplanet-before-relaunch.log')
    print(json.dumps({'archivedAssets': len(assets), 'archivedMaps': len(maps),
        'protectedHashesMatched': len(protected), 'relaunchRequired': True}))


if __name__ == '__main__':
    main()
