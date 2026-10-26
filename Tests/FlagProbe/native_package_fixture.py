"""Visible tests of the separate game-drive flag package, no flag helper."""
import json
from pathlib import Path
import shutil
import sys
import time
import game
from read_flag_visuals import read,model_mesh

OUT=Path(__file__).resolve().parents[2]/'artifacts/flag-file-followup-20261022/native-package'
if len(sys.argv)>2 and sys.argv[1]=='setup':OUT=OUT/sys.argv[2]
game.destination=OUT
SHAPES=['Long','Swallowtail','Round']


def save(name,result):
    target=OUT/(name+'.json');assert not target.exists()
    target.write_text(json.dumps(result,indent=2))


def wait(mode):
    for _ in range(80):
        if game.call('GetMode')['mode']==mode:return
        time.sleep(.25)
    raise RuntimeError('Expected '+mode)


def shot(name):
    began=time.time()
    result=game.call('TakeScreenshot',{'currentView':True,'hideOverlay':True,'forceRes':True,'width':1920,'height':1080,'format':'webp'})
    source=Path(result['fullName']);assert result['detected'] and source.stat().st_mtime>=began-.1
    target=OUT/(name+'.webp');assert not target.exists();shutil.copy2(source,target)
    save(name,{'capture':result,'mode':game.call('GetMode'),'items':game.call('GetItems',{'limit':20})})
    print(target,flush=True)


def camera(x,y,z,tx,ty,tz,fov):
    game.call('ControlPlayCamera',{'action':'set_position','x':x,'y':y,'z':z,'target':{'x':tx,'y':ty,'z':tz},'fov':fov})


def setup():
    assert game.call('GetMode')['mode']=='Menu'
    plugins=game.call('ListPlugins')
    assert not {'TMISFlagGpuPlacement','TMISFlagMaterialProbe','TMISVanillaFlagCopy'}&{p['id'] for p in plugins['plugins']}
    save('initial-plugins',plugins)
    game.call('EditNewMap',{'vista':'stadium155-day'});wait('MapEditor')
    assert game.call('GetMapInfo')['nbItems']==0
    for _ in range(60):
        found=game.call('tm-mcp-pack-epp.FindInventory',{'query':'TMIS_NativePackage','limit':20})
        if found['inventory']['nbItems']>0:break
        time.sleep(.25)
    save('inventory',found)
    game.call('tm-mcp-pack-epp.SetAgentTag',{'tag':'tmis:native-package-20261022'})
    for i,shape in enumerate(SHAPES):
        result=game.call('tm-mcp-pack-epp.PlaceItem',{'itemPath':'TMIS_NativePackage_'+shape,
            'x':1460+20*i,'y':8,'z':1360,'yaw':1.57079632679,'autofocus':False})
        assert result['placed'] and result['afterItems']==i+1
    save('placed-items',game.call('GetItems',{'limit':20}))
    game.call('ControlOverlay',{'action':'hide'})
    game.call('ControlCamera',{'action':'look','direction':'North','smooth':False})
    game.call('ControlCamera',{'action':'setVStep','step':'Low','smooth':False})
    game.call('SetEditorCamera',{'x':1480,'y':20,'z':1360,'distance':90})
    time.sleep(.5);shot('hot-editor')
    views('hot')


def views(prefix):
    if game.call('GetMode')['mode']=='MapEditor':
        game.call('ControlValidation',{'action':'requestEnterPlayground'});wait('MapEditor_Playground')
    assert game.call('GetMode')['mode']=='MapEditor_Playground'
    camera(1483,24,1295,1483,19,1360,58)
    time.sleep(.5);shot(prefix+'-overview')
    for i,shape in enumerate(SHAPES):
        x=1460+20*i+(5 if shape=='Long' else 2.5)
        camera(x,21,1343 if shape=='Long' else 1349,x,19.7,1360,50)
        time.sleep(.3);shot(prefix+'-'+shape.lower())
    camera(1483,24,1295,1483,19,1360,58)


def reopen(prefix):
    global OUT
    OUT=OUT/prefix;OUT.mkdir(exist_ok=False);game.destination=OUT
    assert game.call('GetMode')['mode']=='Menu'
    plugins=game.call('ListPlugins')
    assert not {'TMISNativeFlagPlacement','TMISFlagGpuPlacement','TMISFlagMaterialProbe','TMISVanillaFlagCopy'}&{p['id'] for p in plugins['plugins']}
    save('initial-plugins',plugins)
    game.call('OpenMapInEditor',{'path':'My Maps/TMIS_NativePackage_20261022.Map.Gbx'});wait('MapEditor')
    items=game.call('GetItems',{'limit':20});assert items['total']==3
    assert {i['idName'] for i in items['items']}=={'TMIS_NativePackage_'+s for s in SHAPES}
    save('placed-items',items)
    ptrs=game.call('DevGetPointers',{'listAnchoredObjects':True,'anchoredObjectsLimit':10})
    results=[]
    for item,anchor in zip(items['items'],ptrs['anchoredObjects']):
        mesh=model_mesh(read(anchor['ptr'],0x158));assert read(mesh,0xB0,'u32')==5
        table=read(mesh,0xA8);visuals=[]
        for i in range(5):
            v=read(table,i*8);f=read(v,0x24,'u32');frames=read(v,0x118,'u32')
            assert frames==(86 if i<4 else 0)
            assert bool(f&0x08000000)==(i<4)
            visuals.append({'ptr':v,'frames':frames,'flags':hex(f),'structured':bool(f&0x08000000)})
        results.append({'name':item['idName'],'mesh':mesh,'visuals':visuals})
    save('placed-visuals',results)
    game.call('ControlOverlay',{'action':'hide'})
    views('saved-map')


if __name__=='__main__':
    if sys.argv[1]=='setup':setup()
    elif sys.argv[1]=='views':views(sys.argv[2])
    elif sys.argv[1]=='reopen':reopen(sys.argv[2])
