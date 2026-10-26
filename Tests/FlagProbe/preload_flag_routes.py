"""Read-only, preload-only gate for address-free standalone candidates.

Requires actual staged bytes. Unresolved candidates must never be placed.
"""
import hashlib
import json
import sys
from pathlib import Path

import game
from read_flag_visuals import item_mesh, mesh_flags, read

out=Path(sys.argv[1])
assert not out.exists(), 'Preserve prior measurements'
game.destination=out.parent
live=Path.home()/'Documents/Trackmania/Items'/ (sys.argv[2] if len(sys.argv)>2 else 'TMIS_StandaloneRoutes')
assert live.parent==Path.home()/'Documents/Trackmania/Items', 'A single dedicated Items folder is required'
files=sorted(live.glob('*.Item.Gbx'))
assert files, 'No actual staged candidates'
assert game.call('GetMode')['mode']=='Menu', 'Preload only, never draw invalid candidates'
rows=[]
for path in files:
    print('PRELOAD',path.name,flush=True)
    result={'file':path.name,'sha256':hashlib.sha256(path.read_bytes()).hexdigest()}
    try:
        mesh=item_mesh('Items/'+live.name+'/'+path.name)
        result.update(mesh_flags(mesh))
        arrays=[]
        for off in [0xB8,0xC8,0x1F8]:
            count=read(mesh,off+8,'u32')
            assert count<8,(off,count)
            table=read(mesh,off)
            mats=[]
            for i in range(count):
                mat=read(table,i*8)
                devices=read(mat,0x40,'u32')
                assert devices<=4
                devs=read(mat,0x38)
                mats.append({'ptr':mat,'devices':[
                    {'fid1':read(devs,d*0x38+8),
                     'fid2':read(devs,d*0x38+0x10),
                     'fid3':read(devs,d*0x38+0x28),
                     'cachedShader':read(devs,d*0x38+0x30)} for d in range(devices)]})
            arrays.append({'offset':hex(off),'materials':mats})
        result['materialArrays']=arrays
    except Exception as error:
        result['error']=str(error)
    rows.append(result)
    out.write_text(json.dumps(rows,indent=2)+'\n')
    print(json.dumps({k:v for k,v in result.items() if k!='visuals'}),flush=True)
