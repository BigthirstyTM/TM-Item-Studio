"""Owned, append-only Skyward banner game experiment commands.

Uses the existing localhost semantic bridge; never patches game memory.
"""
import json
import hashlib
from pathlib import Path
import shutil
import subprocess
import sys
import time

import game

ROOT = Path(__file__).resolve().parents[2]
BASE = ROOT / 'artifacts/skyward-banner-20261023'
BASE.mkdir(exist_ok=True)
game.destination = BASE


def save(name, data):
    path = BASE / name
    assert not path.exists(), 'Preserve previous evidence: ' + str(path)
    path.write_text(json.dumps(data, indent=2) + '\n', encoding='utf-8')


def menu_from_owned_map():
    mode = game.call('GetMode')['mode']
    if mode.startswith('MapEditor'):
        info = game.call('GetMapInfo')
        items = game.call('GetItems', {'limit': 10})
        assert info['name'] == 'Unnamed' or info['name'].startswith('TMIS '), info
        assert items['total'] <= 4
        assert all(i['idName'].startswith(('TMIS_', 'XertrovItems\\PromoPlane_Skyward')) or i['idName']=='Flag16m' for i in items['items']), items
        response = game.call('BackToMainMenu')
        if response.get('blockedByDialog'):
            game.call('RespondDialog', {'action': 'no'})
    elif mode != 'Menu':
        raise RuntimeError('Unexpected game mode: ' + mode)
    for _ in range(60):
        if game.call('GetMode')['mode'] == 'Menu':
            return
        time.sleep(.25)
    raise RuntimeError('Menu transition timed out')


def screenshot(out, name):
    for attempt in range(3):
        result = game.call('TakeScreenshot', {'currentView':True, 'hideOverlay':True,
            'format':'jpg','forceRes':True,'width':1920,'height':1080})
        if result.get('detected'):
            break
    assert result.get('detected')
    target = out / (name + '.jpg')
    assert not target.exists()
    shutil.copy2(result['fullName'], target)
    (out / (name + '.json')).write_text(json.dumps(result, indent=2))
    return str(target)


def main():
    command = sys.argv[1]
    if command == 'preflight':
        values = {name: game.call(name) for name in [
            'GetMode', 'GetReadiness', 'ListToolPacks',
            'tm-mcp-pack-epp.Ping', 'ListPlugins']}
        save('startup-preflight.json', values)
        request = {'route': 'tools'}
        response = game.bridge.send_request('127.0.0.1', 30006, 45, request)
        save('tool-catalog.json', response)
        print(json.dumps(values, indent=2))
    elif command == 'call':
        print(json.dumps(game.call(sys.argv[2], json.loads(sys.argv[3]) if len(sys.argv)>3 else {}), indent=2))
    elif command == 'open':
        source = Path(sys.argv[2]).resolve()
        out = BASE / sys.argv[3]
        out.mkdir(exist_ok=False)
        game.destination = out
        menu_from_owned_map()
        folder = Path.home() / 'Documents/Trackmania/Maps/TMIS_Skyward_Research'
        folder.mkdir(exist_ok=True)
        target = folder / source.name
        if not target.exists():
            shutil.copy2(source, target)
        assert target.read_bytes() == source.read_bytes()
        record = {'source':str(source),'staged':str(target),
            'sha256':hashlib.sha256(target.read_bytes()).hexdigest().upper()}
        (out/'map-file.json').write_text(json.dumps(record,indent=2))
        game.call('OpenMapInEditor', {'path':str(target),'waitMs':1500})
        for _ in range(120):
            try:
                if game.call('GetReadiness',{'want':'editor'}).get('ready'):
                    break
            except (ConnectionResetError,TimeoutError):
                pass  # Native map transition may close a bridge connection.
            time.sleep(.25)
        else:
            raise RuntimeError('Editor readiness timed out')
        info=game.call('GetMapInfo')
        items=game.call('GetItems',{'limit':10})
        assert info['name'].startswith('TMIS ') and items['total']==2
        assert items['items'][0]['idName']=='Flag16m'
        (out/'map-state.json').write_text(json.dumps({'map':info,'items':items},indent=2))
        game.call('ControlOverlay',{'action':'hide'})
        game.call('SetEditorCamera',{'x':768,'y':129,'z':438,'distance':43,
            'hAngle':90,'vAngle':8,'animate':False})
        record['screenshot']=screenshot(out,'overview')
        print(json.dumps(record))
    elif command == 'shot':
        out = BASE / sys.argv[2]
        out.mkdir(exist_ok=True)
        print(screenshot(out,sys.argv[3]))
    elif command in ('record', 'record-center'):
        out = BASE / sys.argv[2]
        out.mkdir(exist_ok=False)
        game.destination = out
        seconds = int(sys.argv[3]) if len(sys.argv)>3 else 24
        assert 5<=seconds<=55
        mode=game.call('GetMode')
        assert mode['mode'].startswith('MapEditor')
        state={'mode':mode,'items':game.call('GetItems',{'limit':10}),
            'plugins':game.call('ListPlugins')}
        (out/'state.json').write_text(json.dumps(state,indent=2))
        raw=out/'raw.mp4'
        # The prior full-window capture log measured a 3840x2160 client.
        # A native 1920x1080 central crop avoids a 4K GDI readback/downscale.
        # This is an evidence crop, not frame interpolation or camera motion.
        crop = command == 'record-center'
        capture_options = ['-offset_x','960','-offset_y','540','-video_size','1920x1080'] if crop else []
        started=time.time()
        with (out/'capture.log').open('w') as log:
            result=subprocess.run([shutil.which('ffmpeg'),'-hide_banner','-n','-f','gdigrab',
                '-framerate','60','-draw_mouse','0',*capture_options,'-i','title=Trackmania',
                '-t',str(seconds),'-vf','scale=1920:1080','-c:v','libx264',
                '-preset','ultrafast','-crf','17','-pix_fmt','yuv420p','-fps_mode','vfr',str(raw)],
                stdin=subprocess.DEVNULL,stdout=log,stderr=log,timeout=seconds+30)
        assert result.returncode==0
        metadata=json.loads(subprocess.check_output([shutil.which('ffprobe'),'-v','error',
            '-count_frames','-show_entries','stream=width,height,avg_frame_rate,nb_frames,nb_read_frames,duration',
            '-show_entries','format=duration,size','-of','json',str(raw)]))
        record={'video':str(raw),'sha256':hashlib.sha256(raw.read_bytes()).hexdigest().upper(),
            'captureCrop':{'x':960,'y':540,'width':1920,'height':1080,'measuredWindowWidth':3840,'measuredWindowHeight':2160} if crop else None,
            'startedUnix':started,'elapsed':time.time()-started,'metadata':metadata}
        (out/'recording.json').write_text(json.dumps(record,indent=2))
        print(json.dumps(record))
    elif command == 'original':
        source = Path.home() / 'Documents/Trackmania/Items/BF2_ASSETS/Skyward_DoubleDecker_Plane/PromoPlane_Skyward.Item.Gbx'
        expected = '7EEDD88D3417A59F08724F6755EE665ADC35CAF7CE2E46AEFC554C5443EE2FB6'
        assert hashlib.sha256(source.read_bytes()).hexdigest().upper() == expected
        folder = Path.home() / 'Documents/Trackmania/Items/TMIS_Skyward_20261023'
        folder.mkdir(exist_ok=True)
        copy = folder / 'TMIS_Skyward_Original_Copy.Item.Gbx'
        if not copy.exists():
            shutil.copy2(source, copy)
        assert copy.read_bytes() == source.read_bytes()
        assert game.call('GetMode')['mode'] == 'MapEditor'
        before = game.call('GetMapInfo')
        assert before['name'] == 'Unnamed' and before['nbItems'] == 0
        if not (BASE / 'original-map-before.json').exists():
            save('original-map-before.json', before)
        game.call('tm-mcp-pack-epp.SetAgentTag', {'tag': 'tmis:skyward-20261023'})
        game.call('tm-mcp-pack-epp.RefreshInventory')
        result = game.call('tm-mcp-pack-epp.PlaceItem', {
            'itemPath': 'TMIS_Skyward_20261023/TMIS_Skyward_Original_Copy.Item.Gbx',
            'x': 768, 'y': 80, 'z': 768, 'tag': 'tmis:skyward-20261023', 'autofocus': False})
        save('original-placement.json', result)
        save('original-items.json', game.call('GetItems', {'limit': 8}))
        assert game.call('GetMapInfo')['nbItems'] == 1
        game.call('SetEditorCamera', {'x': 633, 'y': 81, 'z': 755,
            'distance': 65, 'hAngle': 90, 'vAngle': 10, 'animate': False})
        print(json.dumps({'copy': str(copy), 'originalSha256': expected, 'items': 1}))
    else:
        raise ValueError(command)


if __name__ == '__main__':
    main()
