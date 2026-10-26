"""Three-shape live fixture and real 60 Hz window capture (no motion interpolation)."""
import datetime
import json
import shutil
import subprocess
import sys
import time
from pathlib import Path
import game
import shapes_fixture as capture

ROOT = Path(__file__).resolve().parents[2] / 'artifacts/flag-round-video-20261021'
ROOT.mkdir(parents=True, exist_ok=True)
game.destination = ROOT
capture.ROOT = ROOT
call = game.call
gate = Path.home() / 'Documents/Trackmania/Items/TMIS_VanillaSteps/gpu-placement.enable'
log = Path.home() / 'OpenplanetNext/Openplanet.log'
TAG = 'tmis:round-video-20261021'
OVERVIEW = (1483, 23, 1310, 1483, 18.5, 1360, 54)
VIEWS = [
    ('Swallowtail', (1503.5, 21, 1346, 1503.5, 19.7, 1360, 50)),
    ('Long cloth - 2.5x', (1485, 21, 1343, 1485, 19.7, 1360, 50)),
    ('Round cloth', (1462.5, 21, 1349, 1462.5, 19.7, 1360, 50)),
]

def camera(p):
    return call('ControlPlayCamera', {'action': 'set_position', 'x': p[0], 'y': p[1], 'z': p[2],
        'target': {'x': p[3], 'y': p[4], 'z': p[5]}, 'fov': p[6]})

def setup():
    assert call('GetMode')['mode'] == 'Menu'
    call('EditNewMap', {'vista': 'stadium155-day'})
    for _ in range(60):
        if call('GetMode')['mode'] == 'MapEditor': break
        time.sleep(.5)
    else: raise RuntimeError('Editor did not become ready')
    for _ in range(45):
        found = call('tm-mcp-pack-epp.FindInventory', {'query': 'TMIS_Step0', 'limit': 20})
        if found['count'] == 3: break
        time.sleep(1)
    else: raise RuntimeError(found)
    assert call('GetMapInfo')['nbItems'] == 0
    offset = log.stat().st_size
    (ROOT / 'log-boundary.json').write_text(json.dumps({'offset': offset, 'at': datetime.datetime.now().isoformat()}))
    gate.write_text('Fixed three-shape video fixture; expires after 240 seconds')
    try:
        call('ControlPlugin', {'id': 'TMISFlagGpuPlacement', 'action': 'reload'})
        for _ in range(20):
            fresh = log.read_bytes()[offset:].decode('utf-8', errors='replace')
            if 'PREPARED TMIS_Step08_Round.Item.Gbx' in fresh: break
            time.sleep(.2)
        assert "Loaded plugin 'TMISFlagGpuPlacement'" in fresh and '[TMIS-GPU-FLAG] FAIL' not in fresh, fresh
        call('tm-mcp-pack-epp.SetAgentTag', {'tag': TAG})
        for shape, x in [('07_Swallowtail', 1500), ('06_Long250', 1480), ('08_Round', 1460)]:
            p = call('tm-mcp-pack-epp.PlaceItem', {'itemPath': rf'TMIS_VanillaSteps\TMIS_Step{shape}.Item.Gbx',
                'x': x, 'y': 8, 'z': 1360, 'yaw': 1.57079632679, 'autofocus': False})
            assert p['afterItems'] == p['beforeItems'] + 1
        items = call('GetItems', {'limit': 20})
        assert items['total'] == 3
        (ROOT / 'placed-items.json').write_text(json.dumps(items, indent=2))
        call('ControlOverlay', {'action': 'hide'})
        call('ControlValidation', {'action': 'requestEnterPlayground'})
        for _ in range(40):
            if call('GetMode')['mode'] == 'MapEditor_Playground': break
            time.sleep(.25)
        else: raise RuntimeError('Playground did not become ready')
        camera(OVERVIEW)
        time.sleep(1)
        print(capture.shot('evidence/overview'), flush=True)
        for label, p in VIEWS:
            camera(p)
            time.sleep(.4)
            print(capture.shot('evidence/' + label.split()[0].lower()), flush=True)
        camera(OVERVIEW)
    finally:
        (ROOT / 'setup-fresh.log').write_bytes(log.read_bytes()[offset:])

def record():
    assert call('GetMode')['mode'] == 'MapEditor_Playground'
    assert call('GetItems', {'limit': 20})['total'] == 3
    camera(OVERVIEW)
    raw = ROOT / 'three-flags-60fps-raw.mp4'
    assert not raw.exists(), 'Preserve previous recordings'
    timeline = []
    started = time.monotonic()
    with (ROOT / 'capture60.log').open('w') as output:
        proc = subprocess.Popen([shutil.which('ffmpeg'), '-hide_banner', '-y', '-f', 'gdigrab',
            '-framerate', '60', '-draw_mouse', '0', '-i', 'title=Trackmania', '-t', '52',
            '-vf', 'scale=1920:1080', '-c:v', 'libx264', '-preset', 'ultrafast', '-crf', '17',
            '-pix_fmt', 'yuv420p', '-fps_mode', 'vfr', str(raw)], stdin=subprocess.PIPE, stdout=output, stderr=output)
        time.sleep(1)
        def event(label):
            timeline.append({'time': time.monotonic() - started, 'label': label})
        def move(a, b, seconds):
            began = time.monotonic()
            while True:
                t = min(1, (time.monotonic() - began) / seconds)
                smooth = t*t*(3-2*t)
                camera(tuple(x+(y-x)*smooth for x,y in zip(a,b)))
                if t == 1: break
                time.sleep(.025)
        try:
            event('All three custom cloth shapes')
            time.sleep(6)
            for label, p in VIEWS:
                event(label)
                move(OVERVIEW, p, 3)
                event(label + ' close-up')
                time.sleep(6)
                move(p, OVERVIEW, 2.5)
                time.sleep(1)
            event('All three custom cloth shapes - final overview')
            time.sleep(3)
        finally:
            proc.communicate(b'q', timeout=15)
            gate.unlink(missing_ok=True)
            (ROOT / 'video-timeline.json').write_text(json.dumps(timeline, indent=2))
            (ROOT / 'recording-state.json').write_text(json.dumps({'mode': call('GetMode'), 'items': call('GetItems', {'limit': 20}),
                'capture': 'FFmpeg gdigrab game window; requested 60 Hz; no interpolated motion',
                'elapsed': time.monotonic()-started, 'returncode': proc.returncode}, indent=2))
    assert proc.returncode == 0
    print(raw, flush=True)

if __name__ == '__main__':
    if sys.argv[1] == 'setup': setup()
    elif sys.argv[1] == 'record': record()
