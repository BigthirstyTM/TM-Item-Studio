"""Reversible no-Openplanet boundary for one embedded-map experiment.

Game navigation and visual observation remain native UI actions. This script
only stages a byte-verified map, controls the known process, and audits files.
"""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
GAME = Path('C:/Program Files (x86)/Ubisoft/Ubisoft Game Launcher/games/Trackmania')
PROXY = GAME / 'dinput8.dll'
LOG = Path.home() / 'OpenplanetNext/Openplanet.log'


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def save(path, value):
    assert not path.exists(), str(path)
    path.write_text(json.dumps(value, indent=2) + '\n')


def asset_names(manifest):
    if 'embeddedFiles' in manifest:
        return {Path(f['zipPath']).name for f in manifest['embeddedFiles']}
    return ({'TMIS_MatRemap_Placeholder.Material.Gbx', 'TMIS_MatRemap.GameSkin.Gbx',
             'TMIS_MatRemap.TerrainModifier.Gbx'} |
            {a['itemId'].split('\\')[-1] for a in manifest['anchors']})


def ps(code):
    return subprocess.check_output(['powershell', '-NoProfile', '-Command', code], text=True)


phase, destination = sys.argv[1:3]
out = Path(destination).resolve()
assert out.is_relative_to(ROOT / 'artifacts')
held = out / 'held-dinput8.dll'
if phase == 'prepare':
    import game
    source = Path(sys.argv[3]).resolve()
    out.mkdir(exist_ok=False)
    game.destination = out
    assert game.call('GetMode')['mode'] == 'Menu'
    manifest = json.loads(Path(str(source) + '.manifest.json').read_text())
    assert sha(source) == manifest['outputSha256']
    names = asset_names(manifest)
    roots = [Path.home() / 'Documents/Trackmania/Items', GAME / 'GameData/Stadium/Items']
    loose = [str(p) for root in roots for p in root.rglob('*.Gbx') if p.name in names]
    save(out / 'loose-isolation.json', {'roots': list(map(str, roots)), 'matchingLooseFiles': loose})
    assert not loose
    target = Path.home() / 'Documents/Trackmania/Maps/TMIS_MatRemap_Research' / source.name
    if not target.exists():
        shutil.copy2(source, target)
    assert sha(target) == sha(source)
    save(out / 'map.json', {'source': str(source), 'staged': str(target), 'sha256': sha(target)})
    protected = json.loads((ROOT / 'artifacts/flag-file-followup-20261022/protected-final-hashes.json').read_text())
    assert all(sha(Path(r['Path'])) == r['Hash'] for r in protected)
    save(out / 'protected-before.json', protected)
    (out / 'modules-original.json').write_text(ps("$flagProofProcess=Get-Process Trackmania; if (@($flagProofProcess).Count -ne 1) { throw 'Expected one process' }; $flagProofProcess.Modules | Select-Object ModuleName,FileName | ConvertTo-Json"))
    (out / 'process-original.json').write_text(ps("$flagProofProcess=Get-Process Trackmania; $flagProofProcess | Select-Object Id,StartTime,Path | ConvertTo-Json; Stop-Process -Id $flagProofProcess.Id; if (!$flagProofProcess.WaitForExit(10000)) { throw 'Exit timed out' }"))
    assert ps("Write-Output (@(Get-Process Trackmania -ErrorAction SilentlyContinue).Count)").strip() == '0'
    assert sha(PROXY) == protected[0]['Hash'] and not held.exists()
    shutil.move(str(PROXY), str(held))
    save(out / 'boundary.json', {'proxy': str(PROXY), 'held': str(held), 'proxySha256': sha(held),
                                'logSize': LOG.stat().st_size, 'logMtimeNs': LOG.stat().st_mtime_ns})
    if '--manual-launch' not in sys.argv:
        ps("Start-Process 'uplay://launch/5595/0' -WindowStyle Hidden")
    print('Prepared without the Openplanet proxy; use native UI to launch/open', target)
elif phase == 'launch':
    # Resume only the launch boundary after an interrupted pre-move check.
    assert (out / 'process-original.json').exists() and not (out / 'boundary.json').exists()
    assert ps("Write-Output (@(Get-Process Trackmania -ErrorAction SilentlyContinue).Count)").strip() == '0'
    protected = json.loads((out / 'protected-before.json').read_text())
    assert sha(PROXY) == protected[0]['Hash'] and not held.exists()
    shutil.move(str(PROXY), str(held))
    save(out / 'boundary.json', {'proxy': str(PROXY), 'held': str(held), 'proxySha256': sha(held),
                                'logSize': LOG.stat().st_size, 'logMtimeNs': LOG.stat().st_mtime_ns})
    ps("Start-Process 'uplay://launch/5595/0' -WindowStyle Hidden")
    print('Resumed no-Openplanet launch')
elif phase == 'cold':
    # New evidence directory, same deliberately absent proxy. Keep the
    # original ownership boundary so restoration remains deterministic.
    original = Path(sys.argv[3]).resolve()
    source = Path(sys.argv[4]).resolve()
    boundary = json.loads((original / 'boundary.json').read_text())
    assert not PROXY.exists() and sha(Path(boundary['held'])) == boundary['proxySha256']
    assert LOG.stat().st_size == boundary['logSize'] and LOG.stat().st_mtime_ns == boundary['logMtimeNs']
    out.mkdir(exist_ok=False)
    save(out / 'boundary.json', boundary)
    save(out / 'protected-before.json', json.loads((original / 'protected-before.json').read_text()))
    manifest = json.loads(Path(str(source) + '.manifest.json').read_text())
    assert sha(source) == manifest['outputSha256']
    names = asset_names(manifest)
    roots = [Path.home() / 'Documents/Trackmania/Items', GAME / 'GameData/Stadium/Items']
    loose = [str(p) for root in roots for p in root.rglob('*.Gbx') if p.name in names]
    save(out / 'loose-isolation.json', {'roots': list(map(str, roots)), 'matchingLooseFiles': loose})
    assert not loose
    target = Path.home() / 'Documents/Trackmania/Maps/TMIS_MatRemap_Research' / source.name
    if not target.exists():
        shutil.copy2(source, target)
    assert sha(target) == sha(source)
    save(out / 'map.json', {'source': str(source), 'staged': str(target), 'sha256': sha(source)})
    (out / 'previous-process.json').write_text(ps("$flagProofProcess=Get-Process Trackmania; if (@($flagProofProcess).Count -ne 1) { throw 'Expected one process' }; $flagProofProcess | Select-Object Id,StartTime,Path | ConvertTo-Json; Stop-Process -Id $flagProofProcess.Id; if (!$flagProofProcess.WaitForExit(10000)) { throw 'Exit timed out' }"))
    assert ps("Write-Output (@(Get-Process Trackmania -ErrorAction SilentlyContinue).Count)").strip() == '0'
    if '--manual-launch' not in sys.argv:
        ps("Start-Process 'uplay://launch/5595/0' -WindowStyle Hidden")
    print('Prepared new no-Openplanet process boundary for', target)
elif phase == 'cold-launch':
    assert (out / 'previous-process.json').exists()
    boundary = json.loads((out / 'boundary.json').read_text())
    assert not PROXY.exists() and sha(Path(boundary['held'])) == boundary['proxySha256']
    assert LOG.stat().st_size == boundary['logSize'] and LOG.stat().st_mtime_ns == boundary['logMtimeNs']
    assert ps("Write-Output (@(Get-Process Trackmania -ErrorAction SilentlyContinue).Count)").strip() == '0'
    ps("Start-Process 'uplay://launch/5595/0' -WindowStyle Hidden")
    print('Resumed cold no-Openplanet launch')
elif phase in {'before', 'after', 'final'}:
    boundary = json.loads((out / 'boundary.json').read_text())
    assert not PROXY.exists() and sha(Path(boundary['held'])) == boundary['proxySha256']
    assert LOG.stat().st_size == boundary['logSize'] and LOG.stat().st_mtime_ns == boundary['logMtimeNs']
    modules = json.loads(ps("$flagProofProcess=Get-Process Trackmania; if (@($flagProofProcess).Count -ne 1) { throw 'Expected one process' }; $flagProofProcess.Modules | Select-Object ModuleName,FileName | ConvertTo-Json"))
    assert not any('openplanet' in r['FileName'].lower() for r in modules)
    proxy = [r for r in modules if r['ModuleName'].lower() == 'dinput8.dll']
    assert len(proxy) == 1 and proxy[0]['FileName'].lower() == 'c:\\windows\\system32\\dinput8.dll'
    save(out / f'modules-{phase}.json', modules)
    (out / f'process-{phase}.json').write_text(ps("Get-Process Trackmania | Select-Object Id,StartTime,Path | ConvertTo-Json"))
    save(out / f'absence-{phase}.json', {'openplanetAbsent': True, 'systemDinput8': True, 'logUnchanged': True})
    print('No Openplanet; system dinput8; log unchanged:', phase)
elif phase == 'restore':
    assert not PROXY.exists()
    boundary = json.loads((out / 'boundary.json').read_text())
    assert sha(held) == boundary['proxySha256']
    ps("$flagProofProcesses=@(Get-Process Trackmania -ErrorAction SilentlyContinue); if ($flagProofProcesses.Count -gt 1) { throw 'Multiple processes' }; foreach ($flagProofProcess in $flagProofProcesses) { Stop-Process -Id $flagProofProcess.Id; if (!$flagProofProcess.WaitForExit(10000)) { throw 'Exit timed out' } }")
    shutil.move(str(held), str(PROXY))
    protected = json.loads((out / 'protected-before.json').read_text())
    assert all(sha(Path(r['Path'])) == r['Hash'] for r in protected)
    save(out / 'restoration.json', {'proxyRestoredExactly': True, 'protectedHashesMatched': len(protected)})
    ps("Start-Process 'uplay://launch/5595/0' -WindowStyle Hidden")
    print('Exact proxy restored and normal game relaunched')
else:
    raise ValueError(phase)
