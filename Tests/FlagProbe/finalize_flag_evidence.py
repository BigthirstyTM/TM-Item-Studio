"""Preserve exact bytes and prove the fixed research helpers were removed."""
import hashlib
import json
import shutil
from pathlib import Path
import vanilla_fixture as f

def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

home = Path.home()
assert f.call('GetMode')['mode'] == 'Menu'
plugins = f.call('ListPlugins')
assert not [p for p in plugins['plugins'] if p['id'] in ['TMISFlagGpuPlacement', 'TMISVanillaFlagCopy']]
for name in ['TMIS_VanillaSteps', 'TMIS_GpuMaterials', 'TMIS_GpuWarmup']:
    assert not (home / 'Documents/Trackmania/Items' / name).exists()
probe = f.ROOT / 'gpu-tools/native-probe'
assert not (probe / 'gpu-readback.enable').exists()
assert not (probe / 'gpu-repair.enable').exists()
for module in ['TMISFlagGpuPlacement', 'TMISVanillaFlagCopy']:
    assert not (home / 'OpenplanetNext/Plugins' / module).exists()

for name in ['TMIS_VanillaSteps_20261021', 'TMIS_EngineFlag_RuntimeOnly_20261021']:
    source = (home / 'Documents/Trackmania/Maps/My Maps' / (name + '.Map.Gbx')).resolve(strict=True)
    assert source.parent == (home / 'Documents/Trackmania/Maps/My Maps').resolve(strict=True)
    assert digest(source) == digest(f.ROOT / source.name)
    dest = (f.ROOT / 'held-maps' / source.name).resolve()
    assert dest.is_relative_to(f.ROOT.resolve()) and not dest.exists()
    dest.parent.mkdir(exist_ok=True)
    shutil.move(str(source), str(dest))
    assert digest(dest) == digest(f.ROOT / source.name)

for source, expected in [
    (home / 'OpenplanetNext/Plugins/Editor.op', 'a07d1450ff06ca281bbfd5b1f44b7c08e647a2c13d472e1efd7ac4e4036fa644'),
    (home / 'Documents/Trackmania/Items/XertrovItems/ztest-flag3.Item.Gbx', '3b14e7e9d8ddf237e8ca2d885031cc26d9d08be1205e2ffbdf69f6c0df1223ff')]:
    assert digest(source) == expected

captures = {Path(json.loads(line)['capture']['fullName'])
            for line in (f.ROOT / 'captures.jsonl').read_text().splitlines()}
archive = f.ROOT / 'native-webp-session6'
assert not archive.exists()
archive.mkdir()
for source in captures:
    assert source.parent == home / 'Documents/Trackmania'
    if source.exists():
        shutil.move(str(source), str(archive / source.name))
shutil.copyfile(home / 'OpenplanetNext/Openplanet.log', f.ROOT / 'Openplanet-cold-final.log')
state = {'mode': 'Menu', 'diagnosisPluginsAbsent': True, 'liveResearchItemFoldersAbsent': True,
         'nativeProbeGatesAbsent': True, 'originalEditorAndItemHashesUnchanged': True,
         'tag': f.call('tm-mcp-pack-epp.SetAgentTag', {'tag': ''}),
         'gameVersion': '2026.2.2.1751', 'openplanetVersion': '1.29.14 Next/Public 6fd4200f'}
(f.ROOT / 'final-state.json').write_text(json.dumps(state, indent=2))

files = list((f.ROOT / 'models').glob('*')) + list(f.ROOT.glob('*.mp4'))
files += [f.ROOT / n for n in ['TMIS_VanillaSteps_20261021.Map.Gbx', 'TMIS_EngineFlag_RuntimeOnly_20261021.Map.Gbx',
          'gpu-flag-identity.json', 'gpu-flag-lsp.json', 'gpu-flag-fresh.log', 'engine-matrix-fresh.log',
          'engine-view-matrix.json', 'cold-map-state.json', 'cleanup-receipt.json', 'cleanup-fresh.log', 'final-state.json']]
files += [probe / 'GpuFlagReadback.dll', probe / 'GpuFlagInject.exe']
source_root = Path(__file__).resolve().parent
files += [source_root / n for n in ['GpuFlagReadback.cpp', 'GpuFlagInject.c', 'FlagVanillaSteps.cs',
          'FlagGpuMaterials.cs', 'FlagGpuWarmup.cs', 'engine_flag_fixture.py', 'cold_flag_fixture.py']]
manifest = {str(p.resolve()): {'bytes': p.stat().st_size, 'sha256': digest(p)} for p in files if p.is_file()}
(f.ROOT / 'artifact-manifest.json').write_text(json.dumps(manifest, indent=2))
print(json.dumps(state, indent=2))
