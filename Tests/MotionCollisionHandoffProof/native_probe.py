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
p.add_argument('--camera-mode', choices=['cam1','cam2','cam3'], help='Use a native per-frame vehicle camera instead of polling free-camera positions')
p.add_argument('--stop-on-launch', action='store_true', help='Capture five seconds after an upward impulse above 15 m/s, then finalize evidence')
p.add_argument('--fps', type=int, choices=[30,60], default=30)
p.add_argument('--item-offset', type=float, nargs=2, default=[0.,0.], metavar=('X','Z'), help='Shift the fixture relative to the fixed spawn for off-centre passenger tests (metres)')
p.add_argument('--cube-spawn', action='store_true', help='Spawn above an 8 m cube without requiring the complete coaster context')
p.add_argument('--wide-camera', action='store_true', help='Frame the larger two-line/two-arc fixture')
p.add_argument('--coaster-context', action='store_true', help='Place the known NewCoaster beside the normalized 128-point fixture')
p.add_argument('--gallery-file', type=Path, help='Verified showcase manifest: place every listed item in one new map')
p.add_argument('--shot-file', type=Path, help='JSON list of fixed camera views with time in seconds')
p.add_argument('--bird-item', default='TMIS_Coaster_BirdsFinal', help='Registered bird item identifier')
p.add_argument('--birds', action='store_true', help='Include generated animated bird flock')
p.add_argument('--empty-cart', action='store_true', help='Spawn observer vehicle away from coaster; no passenger test')
p.add_argument('--cart-shots', action='store_true', help='Three fixed close views of the initial coaster turns; observation only')
p.add_argument('--follow-car-camera', action='store_true', help='Move only the observation camera to follow the passive test car')
p.add_argument('--helper', type=Path, default=Path('C:/Users/PC/OpenplanetPlugins/tm-control-mcp/tools/call.py'))
a = p.parse_args()
if not 20 <= a.seconds <= 900 or not a.label.replace('_', '').isalnum():
    p.error('Use 20-900 seconds and an alphanumeric/underscore label')
if any(not -3 <= v <= 3 for v in a.item_offset):
    p.error('Item offsets must be finite and within three metres')
if a.camera_mode and a.follow_car_camera:
    p.error('Choose native camera or polling camera, not both')
if a.cart_shots and (a.camera_mode or a.follow_car_camera or not a.coaster_context):
    p.error('Cart shots require coaster context and no other camera mode')
if a.birds and not a.coaster_context:
    p.error('Bird scene requires coaster context')
if a.shot_file and (a.cart_shots or a.camera_mode or a.follow_car_camera):
    p.error('Shot file cannot be combined with another camera mode')
a.output.mkdir(parents=True, exist_ok=True)
video_path = a.output / (a.label + '.mp4')
if video_path.exists() or (a.output / (a.label + '-calls.jsonl')).exists():
    p.error('Run label already has evidence; choose a new label')
(a.output / (a.label+'-request.json')).write_text(json.dumps(dict(item=a.item,label=a.label,seconds=a.seconds,itemOffset=a.item_offset,cubeSpawn=a.cube_spawn,coasterContext=a.coaster_context,cameraMode=a.camera_mode,pollingFollowCamera=a.follow_car_camera,cartShots=a.cart_shots,emptyCart=a.empty_cart,birds=a.birds,birdItem=a.bird_item,shotFile=str(a.shot_file) if a.shot_file else None,fps=a.fps,stopOnLaunch=a.stop_on_launch),indent=2),encoding='utf-8')

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
    if call('GetMode')['mode'] == 'MapEditor_Test':
        call('ControlValidation', dict(action='requestLeavePlayground'))
        for _ in range(30):
            if call('GetMode')['mode'] != 'MapEditor_Test':
                break
            time.sleep(.2)
    for _ in range(5):
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
    try:
        ready=call('GetReadiness', dict(want='editor'))['ready']
    except RuntimeError as error:
        if 'Could not reach TM Control MCP' not in str(error): raise
        time.sleep(.5)
        continue
    if ready:
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
if a.coaster_context:
    for _ in range(30):
        inventory = call('tm-mcp-pack-epp.FindInventory', dict(query='NewCoaster', limit=20))
        if any(entry.get('path') == 'NewCoaster.Item.Gbx' for entry in inventory.get('results', [])):
            break
        time.sleep(.5)
    else:
        raise RuntimeError('Coaster inventory is not ready; no items placed')
call('tm-mcp-pack-epp.SetAgentTag', dict(tag='tmis:handoff'))
if a.gallery_file:
    gallery=json.loads(a.gallery_file.read_text(encoding='utf-8'))
    for entry in gallery:
        call('tm-mcp-pack-epp.PlaceItem',dict(itemPath=entry['item'],x=entry['x'],y=entry['y'],z=entry['z'],tag='tmis:handoff'))
    items=call('GetItems')
    if items['total']!=len(gallery) or sorted(i['idName'] for i in items['items'])!=sorted(g['item'] for g in gallery):
        raise RuntimeError('Gallery placement mismatch')
else:
    call('tm-mcp-pack-epp.PlaceItem', dict(itemPath=a.item,x=400+a.item_offset[0],y=15,z=400+a.item_offset[1],tag='tmis:handoff'))
    items = call('GetItems')
    if items['total'] != 1 or items['items'][0]['idName'] != a.item:
        raise RuntimeError('Fixture must contain exactly the requested item')
if a.coaster_context:
    call('tm-mcp-pack-epp.PlaceItem',dict(itemPath='NewCoaster.Item.Gbx',x=534.4675+a.item_offset[0],y=10.782,z=400+a.item_offset[1],tag='tmis:handoff'))
    items = call('GetItems')
    if items['total'] != 2 or sorted(i['idName'] for i in items['items']) != sorted([a.item,'NewCoaster.Item.Gbx']):
        raise RuntimeError('Coaster fixture must contain exactly the requested moving item and coaster')
if a.birds:
    call('tm-mcp-pack-epp.PlaceItem',dict(itemPath=a.bird_item,x=400,y=15,z=400,tag='tmis:handoff'))
    if call('GetItems')['total'] != 3: raise RuntimeError('Bird placement missing')
call('SaveMapAs', dict(fileName=a.label+'.Map.Gbx'))
if call('GetDialog').get('hasFrame'):
    raise RuntimeError('Save dialog remains; choose a new run label')

shots=json.loads(a.shot_file.read_text(encoding='utf-8')) if a.shot_file else []
samples = []
with (a.output / (a.label + '-record.log')).open('w') as log:
    recorder = subprocess.Popen(['ffmpeg','-hide_banner','-n','-f','gdigrab',
        '-framerate',str(a.fps),'-draw_mouse','0','-i','title=Trackmania','-t',str(a.seconds),
        '-vf','scale=1280:720','-fps_mode','cfr','-r',str(a.fps),'-c:v','libx264','-preset','veryfast','-crf','20',
        '-pix_fmt','yuv420p','-movflags','frag_keyframe+empty_moov',str(video_path)],stdin=subprocess.PIPE,stdout=log,stderr=log)
    try:
        # The coaster cube is 8 m tall (top Y=19), unlike the thin deck (Y=16).
        # Spawn above it, not inside its collision volume.
        call('ControlValidation',dict(action='testFromCoord',x=2 if a.empty_cart else 12,y=10 if a.empty_cart else (11 if a.coaster_context or a.cube_spawn else 10),z=2 if a.empty_cart else 12,dir='North'))
        time.sleep(2)
        camera = (dict(x=416,y=70,z=420,target=dict(x=416,y=14,z=388),fov=50)
                  if a.wide_camera else dict(x=432,y=48,z=446,target=dict(x=393,y=14,z=400),fov=55))
        if a.coaster_context:
            camera = dict(x=500,y=240,z=530,target=dict(x=500,y=40,z=310),fov=60)
        cart_views = [dict(x=405,y=38,z=383,target=dict(x=375,y=12,z=380),fov=65),
                      dict(x=385,y=65,z=295,target=dict(x=357,y=24,z=281),fov=65),
                      dict(x=430,y=65,z=250,target=dict(x=391,y=33,z=230),fov=65)]
        if shots:
            camera={k:v for k,v in shots[0].items() if k!='time'}
        elif a.cart_shots:
            camera = cart_views[0]
        if a.camera_mode:
            camera_result=call('ControlPlayCamera',dict(action='set_mode',mode=a.camera_mode,spectatorCamera='follow'))
            if camera_result.get('after', {}).get('camType', {}).get('name') != a.camera_mode:
                raise RuntimeError('Native camera did not switch to the requested mode')
        else:
            call('ControlPlayCamera',dict(action='set_position',**camera))
        start = time.monotonic()
        last_camera = -1.
        stopped_early = False
        launch_at = None
        cart_view = 0
        while time.monotonic()-start < a.seconds-5:
            if (a.output / (a.label + '.stop')).exists():
                stopped_early = True
                break
            s = call('GetVehicleState')['state']
            view = min(2, int((time.monotonic()-start)/30))
            if shots:
                shot_index=max(i for i,s in enumerate(shots) if s['time']<=time.monotonic()-start)
                if shot_index!=cart_view:
                    call('ControlPlayCamera',dict(action='set_position',**{k:v for k,v in shots[shot_index].items() if k!='time'}))
                    cart_view=shot_index
            elif a.cart_shots and view != cart_view:
                call('ControlPlayCamera',dict(action='set_position',**cart_views[view]))
                cart_view = view
            samples.append(dict(t=time.monotonic()-start,position=s['position'],
                velocity=s['worldVel'],gas=s['inputGasPedal'],steer=s['inputSteer'],
                brake=s['inputBrakePedal'],up=s['up'],direction=s['dir'],wheels=s['wheels']))
            if a.follow_car_camera and time.monotonic()-start-last_camera >= 1:
                x,y,z = s['position']
                call('ControlPlayCamera',dict(action='set_position',x=x+22,y=y+17,z=z+22,target=dict(x=x,y=y,z=z),fov=60))
                last_camera = time.monotonic()-start
            if a.stop_on_launch and s['worldVel'][1] > 15 and launch_at is None:
                launch_at = time.monotonic()
            if launch_at is not None and time.monotonic()-launch_at >= 5:
                stopped_early = True
                break
            time.sleep(.1)
        if stopped_early:
            recorder.communicate(input=b'q', timeout=20)
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
print(json.dumps(dict(stoppedEarly=stopped_early,observedSeconds=samples[-1]['t']-samples[0]['t'],samples=len(samples),lastPosition=samples[-1]['position'],
    yRange=[min(s['position'][1] for s in samples),max(s['position'][1] for s in samples)],
    allWheelsAirSamples=sum(all(w['falling']=='FallingAir' for w in s['wheels']) for s in samples))))
