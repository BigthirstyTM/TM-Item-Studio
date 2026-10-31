"""Local TM/Openplanet experiment runner; deliberately excluded from CI.

Requires an idle game menu, registered fixture inventory, tm-control-mcp/E++,
and ffmpeg on PATH. Never opens or changes an existing user map.
"""
import argparse
import json
import os
from pathlib import Path
import subprocess
import sys
import time

p = argparse.ArgumentParser()
p.add_argument('item', help='Registered inventory model, e.g. TMIS_CH_CornerOverlap')
p.add_argument('label', help='Unique run/map basename; use a new label for repeats')
p.add_argument('--output', type=Path, required=True)
p.add_argument('--seconds', type=int, default=32)
p.add_argument('--wide-camera', action='store_true', help='Frame the larger two-line/two-arc fixture')
p.add_argument('--coaster-context', action='store_true', help='Place the known NewCoaster beside the normalized 128-point fixture')
p.add_argument('--follow-car-camera', action='store_true', help='Move only the observation camera to follow the passive test car')
p.add_argument('--helper', type=Path, default=Path('C:/Users/PC/OpenplanetPlugins/tm-control-mcp/tools/call.py'))
a = p.parse_args()
if not 20 <= a.seconds <= 300 or not a.label.replace('_', '').isalnum():
    p.error('Use 20-300 seconds and an alphanumeric/underscore label')
a.output.mkdir(parents=True, exist_ok=True)
video_path = a.output / (a.label + '.mp4')
if video_path.exists():
    p.error('Run label already has a recording; choose a new label')

def call(name, data=None):
    result = subprocess.run([sys.executable, str(a.helper), '--skip-process-check',
                             '--timeout', '30', name, json.dumps(data or {})],
                            capture_output=True, text=True, encoding='utf-8',
                            env=dict(os.environ, PYTHONUTF8='1'), timeout=40)
    value = json.loads(result.stdout)
    with (a.output / (a.label + '-calls.jsonl')).open('a', encoding='utf-8') as log:
        log.write(json.dumps(dict(time=time.time(),tool=name,request=data,response=value))+'\n')
    if not value.get('ok') or value.get('data', {}).get('result', {}).get('success') is False:
        raise RuntimeError(result.stdout)
    return value['data']['result']['output']

def leave_fixture():
    for _ in range(3):
        result = call('BackToMainMenu')
        if result.get('blockedByDialog'):
            # This runner created and saved the disposable fixture, never a user map.
            call('RespondDialog', dict(action='no'))
        mode = call('GetMode')['mode']
        if mode == 'Menu':
            return
        if mode != 'MapEditor':
            raise RuntimeError('Unexpected leave state: ' + mode)
    raise RuntimeError('Fixture did not return to menu')

if call('GetMode')['mode'] != 'Menu':
    raise RuntimeError('Start from the game menu; existing map will not be modified')
call('tm-mcp-pack-epp.Ping')
call('EditNewMap', dict(vista='stadium155-day'))
for _ in range(20):
    if call('GetReadiness', dict(want='editor'))['ready']:
        break
    time.sleep(.5)
else:
    raise RuntimeError('Editor not ready')
if call('GetItems')['total'] != 0:
    raise RuntimeError('New fixture is not empty; refusing placement')
call('tm-mcp-pack-epp.RefreshInventory')
for _ in range(20):
    inventory = call('tm-mcp-pack-epp.FindInventory', dict(query=a.item, limit=20))
    if any(entry.get('path') == a.item for entry in inventory.get('results', [])):
        break
    time.sleep(.5)
else:
    raise RuntimeError('Requested fixture is not registered in the native inventory')
call('tm-mcp-pack-epp.SetAgentTag', dict(tag='tmis:handoff'))
call('tm-mcp-pack-epp.PlaceItem', dict(itemPath=a.item,x=400,y=15,z=400,tag='tmis:handoff'))
items = call('GetItems')
if items['total'] != 1 or items['items'][0]['idName'] != a.item:
    raise RuntimeError('Fixture must contain exactly the requested item')
if a.coaster_context:
    call('tm-mcp-pack-epp.PlaceItem',dict(itemPath='NewCoaster.Item.Gbx',x=534.4675,y=10.782,z=400,tag='tmis:handoff'))
    items = call('GetItems')
    if items['total'] != 2 or sorted(i['idName'] for i in items['items']) != sorted([a.item,'NewCoaster.Item.Gbx']):
        raise RuntimeError('Coaster fixture must contain exactly the requested moving item and coaster')
call('SaveMapAs', dict(fileName=a.label+'.Map.Gbx'))
if call('GetDialog').get('hasFrame'):
    raise RuntimeError('Save dialog remains; choose a new run label')

samples = []
with (a.output / (a.label + '-record.log')).open('w') as log:
    recorder = subprocess.Popen(['ffmpeg','-hide_banner','-n','-f','gdigrab',
        '-framerate','30','-draw_mouse','0','-i','title=Trackmania','-t',str(a.seconds),
        '-vf','scale=1280:720','-c:v','libx264','-preset','veryfast','-crf','20',
        '-pix_fmt','yuv420p',str(video_path)],stdout=log,stderr=log)
    try:
        # The coaster cube is 8 m tall (top Y=19), unlike the thin deck (Y=16).
        # Spawn above it, not inside its collision volume.
        call('ControlValidation',dict(action='testFromCoord',x=12,y=11 if a.coaster_context else 10,z=12,dir='North'))
        time.sleep(2)
        camera = (dict(x=416,y=70,z=420,target=dict(x=416,y=14,z=388),fov=50)
                  if a.wide_camera else dict(x=432,y=48,z=446,target=dict(x=393,y=14,z=400),fov=55))
        if a.coaster_context:
            camera = dict(x=500,y=240,z=530,target=dict(x=500,y=40,z=310),fov=60)
        call('ControlPlayCamera',dict(action='set_position',**camera))
        start = time.monotonic()
        last_camera = -1.
        while time.monotonic()-start < a.seconds-5:
            s = call('GetVehicleState')['state']
            samples.append(dict(t=time.monotonic()-start,position=s['position'],
                velocity=s['worldVel'],gas=s['inputGasPedal'],steer=s['inputSteer'],
                brake=s['inputBrakePedal'],up=s['up'],direction=s['dir'],wheels=s['wheels']))
            if a.follow_car_camera and time.monotonic()-start-last_camera >= 1:
                x,y,z = s['position']
                call('ControlPlayCamera',dict(action='set_position',x=x+22,y=y+17,z=z+22,target=dict(x=x,y=y,z=z),fov=60))
                last_camera = time.monotonic()-start
            time.sleep(.1)
        if recorder.wait(timeout=15) != 0:
            raise RuntimeError('Recording failed; inspect record log')
    finally:
        (a.output / (a.label+'-samples.json')).write_text(json.dumps(samples,indent=2),encoding='utf-8')
        if recorder.poll() is None:
            recorder.terminate()
            recorder.wait(timeout=10)
if not samples or any(s['gas'] != 0 or s['steer'] != 0 or s['brake'] != 0 for s in samples):
    raise RuntimeError('Missing telemetry or driver input invalidates passive-car test')
call('tm-mcp-pack-epp.SetAgentTag', dict(tag=''))
leave_fixture()
print(json.dumps(dict(samples=len(samples),lastPosition=samples[-1]['position'],
    yRange=[min(s['position'][1] for s in samples),max(s['position'][1] for s in samples)],
    allWheelsAirSamples=sum(all(w['falling']=='FallingAir' for w in s['wheels']) for s in samples))))
