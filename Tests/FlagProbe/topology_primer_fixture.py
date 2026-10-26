"""Disposable-map topology/primer controls, with no flag or material writes."""
import json
from pathlib import Path
import shutil
import sys
import time
import game
from read_flag_visuals import read, model_mesh, mesh_flags

OUT=Path(__file__).resolve().parents[2]/'artifacts/flag-file-followup-20261022/topology-primer'
PREFIX='TMIS_Topology_'
FOLDER='TMIS_TopologyPrimer'
if '--native-geometry' in sys.argv:
    OUT=OUT/'native-geometry-fixed-run'
    PREFIX='TMIS_NativeTopology_'
    FOLDER='TMIS_NativeTopologyFixed'
game.destination=OUT
MODES=['Rectangle','Half','Swallowtail','Round','NarrowRectangle']
HELPERS={'TMISFlagGpuPlacement','TMISFlagMaterialProbe','TMISVanillaFlagCopy'}
TAG='tmis:topology-primer-20261022'


def save(name,value):
    p=OUT/(name+'.json');assert not p.exists();p.write_text(json.dumps(value,indent=2)+'\n')


def wait(mode):
    for _ in range(60):
        if game.call('GetMode')['mode']==mode:return
        time.sleep(.25)
    raise RuntimeError('Expected '+mode)


def shot(name):
    began=time.time()
    r=game.call('TakeScreenshot',{'currentView':True,'hideOverlay':True,'forceRes':True,'width':1920,'height':1080,'format':'webp'})
    p=Path(r['fullName']);assert r['detected'] and p.stat().st_mtime>=began-.1
    dest=OUT/(name+'.webp');assert not dest.exists();shutil.copy2(p,dest)
    save(name,{'capture':r,'mode':game.call('GetMode'),'items':game.call('GetItems',{'limit':20})})
    print(dest,flush=True)


def prepare():
    plugins=game.call('ListPlugins');assert not HELPERS&{p['id'] for p in plugins['plugins']}
    assert game.call('GetMode')['mode']=='Menu'
    save('initial-plugins',plugins)
    game.call('EditNewMap',{'vista':'stadium155-day'});wait('MapEditor')
    assert game.call('GetMapInfo')['nbItems']==0
    for _ in range(40):
        found=game.call('tm-mcp-pack-epp.FindInventory',{'query':PREFIX,'limit':20})
        if found['count']==5:break
        time.sleep(.25)
    else:raise RuntimeError(found)
    game.call('tm-mcp-pack-epp.SetAgentTag',{'tag':TAG})
    for i,name in enumerate(MODES):
        p=game.call('tm-mcp-pack-epp.PlaceItem',{'itemPath':FOLDER+'\\'+PREFIX+name+'.Item.Gbx',
            'x':1460+14*i,'y':8,'z':1360,'yaw':1.57079632679,'autofocus':False})
        assert p['placed'] and p['afterItems']==i+1
    game.call('ControlOverlay',{'action':'hide'})
    game.call('ControlCamera',{'action':'look','direction':'North','smooth':False})
    game.call('ControlCamera',{'action':'setVStep','step':'Low','smooth':False})
    game.call('SetEditorCamera',{'x':1480,'y':20,'z':1360,'distance':110})
    time.sleep(.5);shot('01-custom-only-editor')


def primer():
    assert game.call('GetMode')['mode']=='MapEditor'
    assert game.call('GetItems',{'limit':20})['total']==5
    p=game.call('tm-mcp-pack-epp.PlaceItem',{'itemPath':'Flag16m','x':1446,'y':8,'z':1360,'yaw':1.57079632679,'autofocus':False})
    assert p['afterItems']==6
    time.sleep(.5);shot('02-native-primer-editor')
    game.call('ControlValidation',{'action':'requestEnterPlayground'});wait('MapEditor_Playground')
    game.call('ControlPlayCamera',{'action':'set_position','x':1483,'y':24,'z':1275,'target':{'x':1483,'y':19,'z':1360},'fov':66})
    time.sleep(.5);shot('03-native-primer-overview')
    for i,name in enumerate(MODES):
        x=1460+14*i+2
        game.call('ControlPlayCamera',{'action':'set_position','x':x,'y':21,'z':1347,'target':{'x':x,'y':19.7,'z':1360},'fov':48})
        time.sleep(.3);shot('04-'+name.lower())
    game.call('ControlPlayCamera',{'action':'set_position','x':1483,'y':24,'z':1275,'target':{'x':1483,'y':19,'z':1360},'fov':66})


if __name__=='__main__':{'prepare':prepare,'primer':primer}[sys.argv[1]]()
