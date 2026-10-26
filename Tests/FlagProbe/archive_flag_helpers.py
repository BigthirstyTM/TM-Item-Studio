"""Unload and archive only this investigation's fixed, owned fixtures.

Run from the menu before a clean-process embedded-map check. No user content or
installed Editor package is moved; all moved paths are verified and preserved.
"""
import hashlib
import json
import shutil
import sys
import time
from pathlib import Path
import vanilla_fixture as f

assert f.call('GetMode')['mode'] == 'Menu'
home = Path.home()
log = home / 'OpenplanetNext/Openplanet.log'
if '--resume' in sys.argv:
    fresh = (f.ROOT / 'cleanup-fresh.log').read_bytes()
    assert not [p for p in f.call('ListPlugins')['plugins'] if p['id'] in ['TMISFlagGpuPlacement', 'TMISVanillaFlagCopy']]
else:
    offset = log.stat().st_size
    (f.ROOT / 'cleanup-boundary.json').write_text(json.dumps({'offset': offset}))
    (f.ROOT / 'plugins-before-cleanup.json').write_text(json.dumps(f.call('ListPlugins'), indent=2))
    for module in ['TMISFlagGpuPlacement', 'TMISVanillaFlagCopy']:
        f.call('ControlPlugin', {'id': module, 'action': 'unload'})
    time.sleep(.25)
    fresh = log.read_bytes()[offset:]
    (f.ROOT / 'cleanup-fresh.log').write_bytes(fresh)
for module in ['TMISFlagGpuPlacement', 'TMISVanillaFlagCopy']:
    assert f"Unloading plugin '{module}'".encode() in fresh
    assert f"Loaded plugin '{module}'".encode() not in fresh
print(fresh.decode('utf-8', errors='replace'), flush=True)

def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def preserve_folder(source, parent, expected_names=None):
    source = source.resolve()
    parent = parent.resolve(strict=True)
    assert source.parent == parent, 'Refuse unexpected source boundary'
    dest = (f.ROOT / 'held-live' / source.name).resolve()
    if not source.exists() and '--resume' in sys.argv:
        assert dest.exists() and {p.name for p in dest.iterdir()} == expected_names
        return {'from': str(source), 'to': str(dest), 'sha256': {
            str(p.relative_to(dest)): digest(p) for p in dest.rglob('*') if p.is_file()}, 'resumed': True}
    if expected_names is not None:
        assert {p.name for p in source.iterdir()} == expected_names, list(source.iterdir())
    assert dest.is_relative_to(f.ROOT.resolve()) and not dest.exists()
    dest.parent.mkdir(parents=True, exist_ok=True)
    hashes = {str(p.relative_to(source)): digest(p) for p in source.rglob('*') if p.is_file()}
    shutil.move(str(source), str(dest))
    assert {str(p.relative_to(dest)): digest(p) for p in dest.rglob('*') if p.is_file()} == hashes
    return {'from': str(source), 'to': str(dest), 'sha256': hashes}

receipt = []
items = home / 'Documents/Trackmania/Items'
receipt.append(preserve_folder(items / 'TMIS_VanillaSteps', items, {
    'TMIS_Step' + s + '.Item.Gbx' for s in [
        '01_ZeroFids', '02_Roundtrip', '03_Name', '04_Attachment025', '05_Width1025', '06_Long250', '07_Swallowtail']
} | {'TMIS_Gpu_MaterialIds.Item.Gbx'}))
receipt.append(preserve_folder(items / 'TMIS_GpuWarmup', items, {
    'TMIS_Warm_' + s + '.Item.Gbx' for s in ['SharedInst', 'SeparateInst', 'EmptyOwner']}))
receipt.append(preserve_folder(items / 'TMIS_GpuMaterials', items, {
    'TMIS_Gpu_' + s + '.Item.Gbx' for s in ['InlineMaterial', 'InlineParent', 'InlineShader']}))
plugins = home / 'OpenplanetNext/Plugins'
for module in ['TMISFlagGpuPlacement', 'TMISVanillaFlagCopy']:
    names = {'info.toml', 'Main.as'}
    if module == 'TMISVanillaFlagCopy':
        names.add('INITIALIZATION-BRIEF.md')
    receipt.append(preserve_folder(plugins / module, plugins, names))
for name in ['TMIS_VanillaSteps_20261021', 'TMIS_EngineFlag_RuntimeOnly_20261021']:
    source = home / 'Documents/Trackmania/Maps/My Maps' / (name + '.Map.Gbx')
    dest = f.ROOT / source.name
    if dest.exists():
        assert digest(dest) == digest(source)
    else:
        shutil.copyfile(source, dest)
    receipt.append({'map': str(source), 'archivedCopy': str(dest), 'sha256': digest(dest)})

captures = {Path(json.loads(line)['capture']['fullName'])
            for line in (f.ROOT / 'captures.jsonl').read_text().splitlines()}
archive = f.ROOT / 'native-webp-session5'
assert not archive.exists()
archive.mkdir()
for source in captures:
    assert source.parent == home / 'Documents/Trackmania'
    if source.exists():
        shutil.move(str(source), str(archive / source.name))
(f.ROOT / 'cleanup-receipt.json').write_text(json.dumps(receipt, indent=2))
shutil.copyfile(log, f.ROOT / 'Openplanet-before-embedded-cold.log')
f.call('tm-mcp-pack-epp.SetAgentTag', {'tag': ''})
print('Archived exact research helpers and files; map retained for cold check.', flush=True)
