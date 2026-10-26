"""Cold place/reopen tests for three single-file installation controls."""
import json
from pathlib import Path
import sys
import time
import game
import native_package_fixture as views
from read_flag_visuals import read, model_mesh, mesh_flags

BASE=Path(__file__).resolve().parents[2]/'artifacts/flag-file-followup-20261022/minimal-installed'


def run(action, label):
    out=BASE/label
    out.mkdir(exist_ok=True)
    assert not (out/'items.json').exists(), 'Preserve completed run'
    game.destination=out;views.OUT=out
    assert game.call('GetMode')['mode']=='Menu'
    plugins=game.call('ListPlugins')
    assert not {'TMISFlagGpuPlacement','TMISFlagMaterialProbe','TMISVanillaFlagCopy','TMISNativeFlagPlacement'} & {p['id'] for p in plugins['plugins']}
    views.save('plugins',plugins)
    if action=='setup':
        game.call('EditNewMap',{'vista':'stadium155-day'});views.wait('MapEditor')
        assert game.call('GetMapInfo')['nbItems']==0
        for _ in range(60):
            found=game.call('tm-mcp-pack-epp.FindInventory',{'query':'TMIS_Adjacent','limit':20})
            if found['inventory']['nbItems']>0:break
            time.sleep(.5)
        views.save('inventory',found)
        print(json.dumps(found),flush=True)
        game.call('tm-mcp-pack-epp.SetAgentTag',{'tag':'tmis:minimal-installed-20261022'})
        for i,shape in enumerate(views.SHAPES):
            result=game.call('tm-mcp-pack-epp.PlaceItem',{'itemPath':'TMIS_Adjacent_'+shape,
                'x':1460+20*i,'y':8,'z':1360,'yaw':1.57079632679,'autofocus':False})
            print(shape,json.dumps(result),flush=True)
            assert result['placed'] and result['afterItems']==i+1
    elif action=='reopen':
        game.call('OpenMapInEditor',{'path':'My Maps/TMIS_MinimalFlags_20261022.Map.Gbx'});views.wait('MapEditor')
    else:raise ValueError(action)
    items=game.call('GetItems',{'limit':20})
    assert items['total']==3
    assert {i['idName'] for i in items['items']}=={'TMIS_Adjacent_'+s for s in views.SHAPES}
    views.save('items',items)
    anchors=game.call('DevGetPointers',{'listAnchoredObjects':True,'anchoredObjectsLimit':10})
    values=[dict(name=item['idName'],**mesh_flags(model_mesh(read(anchor['ptr'],0x158))))
        for item,anchor in zip(items['items'],anchors['anchoredObjects'])]
    views.save('visuals',values)
    assert all(v['structured']==5 for v in values)
    print(json.dumps([{'name':v['name'],'structured':v['structured']} for v in values]),flush=True)
    game.call('ControlOverlay',{'action':'hide'})
    views.views(label)


if __name__=='__main__':run(*sys.argv[1:])
