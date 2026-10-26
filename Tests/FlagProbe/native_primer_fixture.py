"""Native-flag primer tests. Never sets material, visual, or GPU flags.

Use separate invocations for native render, late staging, and custom render so
the native cloth has genuinely drawn before any custom candidate is registered.
"""
import hashlib
import json
import shutil
import sys
import time
from pathlib import Path

import game

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'artifacts/flag-native-primer-20261021'
LIVE = Path.home() / 'Documents/Trackmania/Items/TMIS_NativePrimer'
game.destination = OUT
call = game.call
SHAPES = [('Long', '06_Long250', 1480), ('Swallowtail', '07_Swallowtail', 1500), ('Round', '08_Round', 1520)]
HELPERS = {'TMISFlagGpuPlacement', 'TMISFlagMaterialProbe', 'TMISVanillaFlagCopy'}


def save(name, value):
    path = OUT / (name + '.json')
    assert not path.exists(), 'Preserve prior receipt: ' + str(path)
    path.write_text(json.dumps(value, indent=2) + '\n')


def no_helpers():
    plugins = call('ListPlugins')
    assert not HELPERS & {p['id'] for p in plugins['plugins']}
    return plugins


def wait_mode(mode):
    for _ in range(60):
        if call('GetMode')['mode'] == mode:
            return
        time.sleep(.25)
    raise RuntimeError('Expected mode ' + mode)


def play():
    assert call('GetMode')['mode'] == 'MapEditor'
    call('ControlOverlay', {'action': 'hide'})
    call('ControlValidation', {'action': 'requestEnterPlayground'})
    wait_mode('MapEditor_Playground')
    call('ControlPlayCamera', {'action': 'set_position', 'x':1493, 'y':24, 'z':1305,
        'target':{'x':1493,'y':19,'z':1360},'fov':65})


def leave_play():
    call('ControlValidation', {'action':'requestLeavePlayground'})
    wait_mode('MapEditor')


def shot(label):
    result = call('TakeScreenshot', {'currentView':True, 'hideOverlay':True,
        'forceRes':True,'width':1920,'height':1080,'format':'webp'})
    path = OUT / (label + '.webp')
    assert not path.exists()
    shutil.copy2(result['fullName'], path)
    save(label, {'capture':result,'items':call('GetItems',{'limit':20}),
        'mode':call('GetMode'),'plugins':no_helpers()})
    print(path, flush=True)


def native():
    no_helpers()
    assert not LIVE.exists(), 'Native must draw before custom files are staged'
    if call('GetMode')['mode'] == 'Menu':
        call('EditNewMap', {'vista':'stadium155-day'})
    wait_mode('MapEditor')
    assert call('GetMapInfo')['nbItems'] == 0
    for _ in range(40):
        found=call('tm-mcp-pack-epp.FindInventory', {'query':'Flag16m','limit':10})
        if found['count']>0: break
        time.sleep(.5)
    else: raise RuntimeError(found)
    call('tm-mcp-pack-epp.SetAgentTag', {'tag':'tmis:native-primer-20261021'})
    result=call('tm-mcp-pack-epp.PlaceItem', {'itemPath':'Flag16m','x':1460,'y':8,'z':1360,
        'yaw':1.57079632679,'autofocus':False,'tag':'tmis:native-primer-native'})
    assert result['beforeItems']==0 and result['afterItems']==1
    call('ControlOverlay', {'action':'hide'})
    call('ControlCamera', {'action':'look','direction':'North','smooth':False})
    call('ControlCamera', {'action':'setVStep','step':'Low','smooth':False})
    call('SetEditorCamera', {'x':1490,'y':20,'z':1360,'distance':95})
    time.sleep(1)
    shot('04-native-editor-rendered-before-staging')


def stage():
    assert call('GetMode')['mode']=='MapEditor'
    assert call('GetItems',{'limit':20})['total']==1
    LIVE.mkdir(exist_ok=False)
    manifest=[]
    for shape,seed,_ in SHAPES:
        source=ROOT/'artifacts/flag-round-video-20261021/models'/('TMIS_Step'+seed+'.Item.Gbx')
        dest=LIVE/('TMIS_Primer_'+shape+'_A.Item.Gbx')
        shutil.copy2(source,dest)
        manifest.append({'source':str(source),'dest':str(dest),'sha256':hashlib.sha256(dest.read_bytes()).hexdigest()})
    save('05-late-stage',manifest)
    print('Staged only after native cloth rendered',flush=True)


def custom():
    no_helpers()
    assert call('GetMode')['mode']=='MapEditor'
    for _ in range(40):
        found=call('tm-mcp-pack-epp.FindInventory',{'query':'TMIS_Primer_','limit':20})
        if found['count']==3: break
        time.sleep(.5)
    else: raise RuntimeError(found)
    for shape,_,x in SHAPES:
        result=call('tm-mcp-pack-epp.PlaceItem',{'itemPath':'TMIS_NativePrimer\\TMIS_Primer_'+shape+'_A.Item.Gbx',
            'x':x,'y':8,'z':1360,'yaw':1.57079632679,'autofocus':False})
        assert result['afterItems']==result['beforeItems']+1,result
    assert call('GetItems',{'limit':20})['total']==4
    saved=call('SaveMapAs',{'fileName':'My Maps/TMIS_NativePrimer_NativeFirst_20261021.Map.Gbx'})
    save('06-native-first-save',saved)
    play()
    time.sleep(1)
    shot('07-native-first-three-custom')


def reopen_native():
    no_helpers()
    assert call('GetMode')['mode']=='Menu'
    call('OpenMapInEditor',{'path':'My Maps/TMIS_NativePrimer_NativeOnly_20261021.Map.Gbx'})
    wait_mode('MapEditor')
    assert call('GetItems',{'limit':20})['total']==1
    call('tm-mcp-pack-epp.SetAgentTag',{'tag':'tmis:native-primer-20261021'})
    call('ControlOverlay', {'action':'hide'})
    call('ControlCamera', {'action':'look','direction':'North','smooth':False})
    call('ControlCamera', {'action':'setVStep','step':'Low','smooth':False})
    call('SetEditorCamera', {'x':1490,'y':20,'z':1360,'distance':95})
    time.sleep(.5)
    shot('06b-native-after-inventory-restart')


if __name__ == '__main__':
    {'native':native,'stage':stage,'custom':custom,'reopen-native':reopen_native}[sys.argv[1]]()
