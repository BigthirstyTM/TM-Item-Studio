"""Read-only measurements through the existing DevSafeRead bridge.

All raw layouts are guarded for this fixed one-variant/two-entity fixture and
Trackmania 2026.2.2.1751. Never writes process memory or visual/GPU flags.
"""
import json
import sys
from pathlib import Path

import game


def read(ptr, offset, kind='u64'):
    result=game.call('DevSafeRead',{'ptr':ptr,'offset':offset,'kind':kind})
    if result.get('readError') or not result.get('probe',{}).get('readable'):
        raise RuntimeError(result)
    return result['value']


def mesh_flags(mesh):
    count=read(mesh,0xB0,'u32')
    assert count==5,('Expected five native animated visuals',mesh,count)
    table=read(mesh,0xA8)
    visuals=[]
    for i in range(count):
        ptr=read(table,i*8)
        frames=read(ptr,0x118,'u32')
        assert frames==86,(i,frames)
        flags=read(ptr,0x24,'u32')
        visuals.append({'ptr':ptr,'frames':frames,'flags':hex(flags),'structured':bool(flags&0x08000000)})
    return {'mesh':mesh,'structured':sum(v['structured'] for v in visuals),'visuals':visuals}


def item_mesh(path):
    load=game.call('ControlFids',{'action':'preload','drive':'user','path':path})
    assert load['nod']['className']=='CGameItemModel',load
    return model_mesh(load['nod']['ptr'])


def model_mesh(item):
    variants=read(item,0x288)
    assert read(variants,0x48,'u32')==1
    table=read(variants,0x40)
    prefab=read(table,0x18)
    assert read(prefab,0x48,'u32')==2
    ents=read(prefab,0x40)
    dyna=read(ents,0x50+8)
    return read(dyna,0x20)


if __name__=='__main__':
    out=Path(sys.argv[1])
    assert not out.exists(),'Preserve previous measurement'
    game.destination=out.parent
    paths=sys.argv[2:]
    results={}
    # Cold custom-only controls must not load native resources while measuring.
    if '--no-native' in paths:
        paths.remove('--no-native')
    else:
        native=game.call('ControlFids',{'action':'preload','drive':'game',
            'path':'GameData/Stadium/Media/Dyna/Flag/Flag.Mesh.Gbx'})
        results['native']=mesh_flags(native['nod']['ptr'])
    if paths==['--placed']:
        pointers=game.call('DevGetPointers',{'listAnchoredObjects':True,'anchoredObjectsLimit':20})
        items=game.call('GetItems',{'limit':20})
        assert pointers['anchoredObjectsCount']==items['total']<=20
        for anchor,item in zip(pointers['anchoredObjects'],items['items']):
            if 'TMIS_' not in item['idName']:continue
            try:
                model=read(anchor['ptr'],0x158)
                results[item['idName']]=mesh_flags(model_mesh(model))
            except Exception as error:results[item['idName']]={'error':str(error)}
        paths=[]
    for path in paths:
        try: results[path]=mesh_flags(item_mesh(path))
        except Exception as error: results[path]={'error':str(error)}
    out.write_text(json.dumps(results,indent=2)+'\n')
    print(json.dumps({name:{k:v for k,v in value.items() if k!='visuals'} for name,value in results.items()},indent=2))
