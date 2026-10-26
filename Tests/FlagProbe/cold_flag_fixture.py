"""Reopen the same saved map after a process restart, with all helpers absent."""
import json
import time
from pathlib import Path
import vanilla_fixture as f

assert f.call('GetMode')['mode'] == 'Menu'
plugins = f.call('ListPlugins')
assert not [p for p in plugins['plugins'] if p['id'] in ['TMISFlagGpuPlacement', 'TMISVanillaFlagCopy']]
for name in ['TMIS_VanillaSteps', 'TMIS_GpuMaterials', 'TMIS_GpuWarmup']:
    assert not (Path.home() / 'Documents/Trackmania/Items' / name).exists()
print(f.call('OpenMapInEditor', {'path': 'My Maps/TMIS_EngineFlag_RuntimeOnly_20261021.Map.Gbx'}), flush=True)
for _ in range(30):
    if f.call('GetMode')['mode'] == 'MapEditor':
        break
    time.sleep(.5)
else:
    raise RuntimeError('Map editor unavailable')
items = f.call('GetItems', {'limit': 10})
assert items['total'] == 2
f.call('ControlOverlay', {'action': 'hide'})
f.call('ControlCamera', {'action': 'look', 'direction': 'North', 'smooth': False})
f.call('ControlCamera', {'action': 'setVStep', 'step': 'Low', 'smooth': False})
camera = f.call('SetEditorCamera', {'x': 1470, 'y': 20, 'z': 1360, 'distance': 40})
(f.ROOT / 'cold-map-state.json').write_text(json.dumps({'items': items, 'camera': camera, 'plugins': plugins}, indent=2))
print(f.capture.shot('evidence/cold-embedded-no-helper'), flush=True)
f.capture.record('cold-embedded-no-helper', 'SCHONE HERSTART | Dezelfde kaart zonder tijdelijke enginecorrectie', 8,
                 'Geen losse testmodellen of diagnoseplugins; de custom doeken worden weer onjuist getekend')
