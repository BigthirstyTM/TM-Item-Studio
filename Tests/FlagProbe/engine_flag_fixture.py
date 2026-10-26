"""Angle/distance and editor-playground checks with the bounded native flag probe."""
import json
import time
from pathlib import Path
import vanilla_fixture as f

gate = Path.home() / 'Documents/Trackmania/Items/TMIS_VanillaSteps/gpu-placement.enable'
log = Path.home() / 'OpenplanetNext/Openplanet.log'
assert f.call('GetMode')['mode'] == 'MapEditor'
f.call('tm-mcp-pack-epp.RemoveByTag', {'tag': 'tmis:gpu-warmup-20261021'})
assert f.call('GetMapInfo')['nbItems'] == 0
offset = log.stat().st_size
(f.ROOT / 'engine-matrix-log-boundary.json').write_text(json.dumps({'offset': offset}))
gate.write_text('Fixed, disposable native-flag fixture only')
views = []
try:
    f.call('ControlPlugin', {'id': 'TMISFlagGpuPlacement', 'action': 'reload'})
    time.sleep(.4)
    fresh = log.read_bytes()[offset:].decode('utf-8', errors='replace')
    assert "Loaded plugin 'TMISFlagGpuPlacement'" in fresh
    assert 'PREPARED TMIS_Step07_Swallowtail.Item.Gbx' in fresh
    assert '[TMIS-GPU-FLAG] FAIL' not in fresh
    f.call('tm-mcp-pack-epp.SetAgentTag', {'tag': 'tmis:engine-matrix-20261021'})
    for s, x in [('06_Long250', 1460), ('07_Swallowtail', 1480)]:
        p = f.call('tm-mcp-pack-epp.PlaceItem', {
            'itemPath': rf'TMIS_VanillaSteps\TMIS_Step{s}.Item.Gbx',
            'x': x, 'y': 8, 'z': 1360, 'yaw': 1.57079632679, 'autofocus': False})
        assert p['afterItems'] == p['beforeItems'] + 1
    f.call('ControlOverlay', {'action': 'hide'})
    f.call('ControlCamera', {'action': 'setVStep', 'step': 'Low', 'smooth': False})
    for direction, distance in [('North', 40), ('North', 80), ('North', 180), ('North', 550), ('South', 40)]:
        f.call('ControlCamera', {'action': 'look', 'direction': direction, 'smooth': False})
        camera = f.call('SetEditorCamera', {'x': 1470, 'y': 20, 'z': 1360, 'distance': distance})
        shot = f.capture.shot(f'evidence/engine-matrix-{direction}-{distance}')
        views.append({'direction': direction, 'distance': distance, 'camera': camera, 'shot': str(shot)})
    f.call('ControlValidation', {'action': 'requestEnterPlayground'})
    for _ in range(30):
        mode = f.call('GetMode')['mode']
        if mode == 'MapEditor_Playground':
            break
        time.sleep(.25)
    else:
        raise RuntimeError(f'Expected playground, got {mode}')
    camera = f.call('ControlPlayCamera', {'action': 'set_position', 'x': 1470, 'y': 22, 'z': 1320,
                    'target': {'x': 1470, 'y': 19, 'z': 1360}, 'fov': 60})
    views.append({'mode': mode, 'camera': camera})
    print(f.capture.shot('evidence/engine-playground'), flush=True)
    f.capture.record('engine-playground', 'Lang doek en zwaluwstaart | editor playground | TIJDELIJKE ENGINEFLAG', 22,
                     'Echte gameframes; correcte eigen vorm met runtimehulp, nog geen zelfstandig werkende Item.Gbx')
finally:
    gate.unlink(missing_ok=True)
    (f.ROOT / 'engine-view-matrix.json').write_text(json.dumps(views, indent=2))
    time.sleep(.1)
    (f.ROOT / 'engine-matrix-fresh.log').write_bytes(log.read_bytes()[offset:])
