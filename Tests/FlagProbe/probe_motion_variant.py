"""Cold preload gate, real map observation and stationary cloth video."""
import json
from pathlib import Path
import shutil
import subprocess
import sys
import time

import game
from motion_flag_fixture import save
from read_flag_visuals import model_mesh, mesh_flags, read

ROOT = Path(__file__).resolve().parents[2]
variant = sys.argv[1]
assert variant in {'local-motion', 'local-motion-v3', 'local-motion-v3-func1', 'nonkinematic-constraints'}
BASE = ROOT / 'artifacts/flag-motion-embedded-20261022' / variant
OUT = BASE / 'cold1'
OUT.mkdir(exist_ok=False)
game.destination = OUT
for name in ['ListToolPacks', 'tm-mcp-pack-epp.Ping', 'GetMode', 'GetReadiness']:
    save(OUT, name.replace('.', '-'), game.call(name))
assert game.call('GetMode')['mode'] == 'Menu'
plugins = save(OUT, 'plugins', game.call('ListPlugins'))
assert not {'TMISFlagGpuPlacement', 'TMISFlagMaterialProbe', 'TMISNativeFlagPlacement',
            'TMISVanillaFlagCopy'} & {p['id'] for p in plugins['plugins']}
(OUT / 'process.json').write_text(subprocess.check_output(['powershell', '-NoProfile', '-Command',
    'Get-Process Trackmania | Select-Object Id,StartTime,Path | ConvertTo-Json'], text=True))
manifest = json.loads((BASE / 'files/manifest.json').read_text())
visuals = []
for row in manifest['models']:
    load = game.call('ControlFids', {'action': 'preload', 'drive': 'game',
        'path': 'GameData/Stadium/Items/' + row['name'] + '.Item.Gbx'})
    item = load['nod']['ptr']
    if variant == 'nonkinematic-constraints':
        variants = read(item, 0x288)
        assert read(variants, 0x48, 'u32') == 1
        prefab = read(read(variants, 0x40), 0x18)
        assert read(prefab, 0x48, 'u32') == 4
        mesh = read(read(read(prefab, 0x40), 0x58), 0x20)
    else:
        mesh = model_mesh(item)
    visuals.append(dict(name=row['name'], **mesh_flags(mesh)))
save(OUT, 'preload-visuals', visuals)
assert all(v['structured'] == 5 for v in visuals), 'Do not draw failed candidates'
game.call('OpenMapInEditor', {'path': '000_TMISTest/' + Path(manifest['map']).name})
for _ in range(100):
    if game.call('GetReadiness', {'want': 'editor'}).get('ready'): break
    time.sleep(.25)
assert game.call('GetReadiness', {'want': 'editor'}).get('ready')
save(OUT, 'items', game.call('GetItems', {'limit': 10}))
game.call('ControlOverlay', {'action': 'hide'})
game.call('ControlValidation', {'action': 'requestEnterPlayground'})
for _ in range(80):
    if game.call('GetMode')['mode'] == 'MapEditor_Playground': break
    time.sleep(.25)
assert game.call('GetMode')['mode'] == 'MapEditor_Playground'
game.call('ControlPlayCamera', {'action': 'set_position', 'x': 767, 'y': 147, 'z': 425,
    'target': {'x': 748, 'y': 141, 'z': 450}, 'fov': 48})
with (OUT / 'stationary-capture.log').open('w') as log:
    subprocess.run([shutil.which('ffmpeg'), '-hide_banner', '-n', '-f', 'gdigrab', '-framerate', '60',
        '-draw_mouse', '0', '-i', 'title=Trackmania', '-t', '12', '-vf', 'scale=1920:1080',
        '-c:v', 'libx264', '-preset', 'ultrafast', '-crf', '17', '-pix_fmt', 'yuv420p',
        '-fps_mode', 'vfr', str(OUT / 'stationary-control.mp4')], stdout=log, stderr=log, check=True)
print('Stationary cloth control captured:', OUT)
