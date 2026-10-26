"""Three independent material-order probes on a verified disposable map."""
import json
import time
import vanilla_fixture as f

assert f.call('GetMode')['mode'] == 'MapEditor'
assert f.call('GetMapInfo')['nbItems'] == 0
for _ in range(30):
    found = f.call('tm-mcp-pack-epp.FindInventory', {'query': 'TMIS_Warm_', 'limit': 10})
    if found['count'] == 3:
        break
    time.sleep(.5)
else:
    raise RuntimeError(found)
f.call('tm-mcp-pack-epp.SetAgentTag', {'tag': 'tmis:gpu-warmup-20261021'})
for i, name in enumerate(['SharedInst', 'SeparateInst', 'EmptyOwner']):
    print(f.call('tm-mcp-pack-epp.PlaceItem', {
        'itemPath': rf'TMIS_GpuWarmup\TMIS_Warm_{name}.Item.Gbx',
        'x': 1440 + 20 * i, 'y': 8, 'z': 1360, 'yaw': 1.57079632679, 'autofocus': False}), flush=True)
f.call('ControlOverlay', {'action': 'hide'})
f.call('ControlCamera', {'action': 'look', 'direction': 'North', 'smooth': False})
f.call('ControlCamera', {'action': 'setVStep', 'step': 'Low', 'smooth': False})
f.call('SetEditorCamera', {'x': 1460, 'y': 20, 'z': 1360, 'distance': 60})
(f.ROOT / 'warmup-placed.json').write_text(json.dumps(f.call('GetItems', {'limit': 10}), indent=2))
print(f.capture.shot('evidence/warmup-clean-process'), flush=True)
