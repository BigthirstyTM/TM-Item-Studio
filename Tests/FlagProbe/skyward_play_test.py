"""Bounded editor-playground comparison for the owned Skyward flight fixture."""
import json
from pathlib import Path
import sys
import time
import skyward_runtime as runtime

out = runtime.BASE / sys.argv[2]
out.mkdir(exist_ok=False)
runtime.game.destination = out
mode = runtime.game.call('GetMode')['mode']
info = runtime.game.call('GetMapInfo')
items = runtime.game.call('GetItems', {'limit': 4})
assert info['name'].startswith('TMIS ') and items['total'] == 2
assert info['fileName'].endswith('TMIS_Skyward_NativeWarpFlagCircleWave.Map.Gbx')
(out / 'before.json').write_text(json.dumps({'mode':mode,'map':info,'items':items},indent=2))
if sys.argv[1] == 'enter':
    assert mode == 'MapEditor', mode
    result = runtime.game.call('ControlValidation', {'action':'testFromCoord','x':24,'y':9,'z':14,'dir':'N'})
    (out / 'enter.json').write_text(json.dumps(result,indent=2))
    for _ in range(80):
        mode = runtime.game.call('GetMode')['mode']
        if mode.startswith('MapEditor_'):
            break
        time.sleep(.25)
    else:
        raise RuntimeError('No editor playground entered')
    camera = runtime.game.call('ControlPlayCamera', {'action':'set_position',
        'x':724.104,'y':175.103,'z':486.89,'target':{'x':788,'y':129,'z':450},'settleFrames':4})
    (out / 'camera.json').write_text(json.dumps(camera,indent=2))
    runtime.screenshot(out,'playground')
elif sys.argv[1] == 'leave':
    assert mode.startswith('MapEditor_'), mode
    result = runtime.game.call('ControlValidation', {'action':'requestLeavePlayground'})
    (out / 'leave.json').write_text(json.dumps(result,indent=2))
    for _ in range(80):
        mode = runtime.game.call('GetMode')['mode']
        if mode == 'MapEditor':
            break
        time.sleep(.25)
    else:
        raise RuntimeError('Editor playground did not leave')
else:
    raise ValueError(sys.argv[1])
(out / 'after.json').write_text(json.dumps({'mode':mode,'readiness':runtime.game.call('GetReadiness')},indent=2))
print(json.dumps({'mode':mode,'out':str(out)}))
