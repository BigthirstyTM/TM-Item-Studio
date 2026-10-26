"""Offline isolated material/LOD visibility controls for embedded Fogger towers."""
from __future__ import annotations
import argparse
import hashlib
import json
from pathlib import Path
import struct
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "Tests/FxStandalone"))
from gbx_container import GbxContainer

BASE = ROOT / "artifacts/fx-clones/fx-sequential-bitmap-20261025"
DONOR = BASE / "meshes/TMIS_Sparkler_EmbeddedFlare.Item.Gbx"
SOURCES = {
    "volume": (BASE / "final/CustomFogger_VolumeTower1.Item.Gbx", "940D1EBBE7B45550A848F17F5296A5AC49AF0BC7E203303547AEB8FEA14F58AE"),
    "sprite": (BASE / "final/CustomFogger_SpriteTower1.Item.Gbx", "96CE1ED3842673BE2AB17E5A333563D535CBD92E4E30A86862B87AE4B024162E"),
}

def sha(data): return hashlib.sha256(data).hexdigest().upper()

def splice(body, patches, reverse=False):
    parts=[]; cursor=0; delta=0
    for patch in sorted(patches,key=lambda p:p['offset']):
        offset=patch['offset']+(delta if reverse else 0)
        old=patch['new'] if reverse else patch['old']; new=patch['old'] if reverse else patch['new']
        if offset<cursor or body[offset:offset+len(old)]!=old: raise ValueError('recorded splice signature differs')
        parts.extend((body[cursor:offset],new));cursor=offset+len(old)
        delta+=len(patch['new'])-len(patch['old'])
    return b''.join(parts+[body[cursor:]])

def generate(kind, mode, number, output_folder):
    path,pinned=SOURCES[kind]; original=path.read_bytes()
    if sha(original)!=pinned: raise ValueError('Fogger fixture hash differs')
    container=GbxContainer.read(original); donor_data=DONOR.read_bytes()
    if sha(donor_data)!='F799B92D732AB5A205F3AF329FF8B9A27CBC220D3E00E83D6A573A9119D08FD7': raise ValueError('visible donor hash differs')
    donor=GbxContainer.read(donor_data)
    old_id=(f'TMIS_FX_SequentialProof\\CustomFogger_{"Volume" if kind=="volume" else "Sprite"}Tower1.Item.Gbx').encode()
    new_id=old_id.replace(b'Tower1.',f'Tower{number}.'.encode())
    if len(old_id)!=58 or len(new_id)!=58 or container.prefix.count(old_id)!=1 or container.body.count(old_id)!=1: raise ValueError('identity control must retain native fixed-width slots')
    patches=[{'offset':container.body.index(old_id),'old':old_id,'new':new_id,'kind':'body identity'}]
    if struct.unpack_from('<II',container.body,6057)!=(6,0x090FD000) or struct.unpack_from('<II',donor.body,1794)!=(6,0x090FD000): raise ValueError('material NodeRef/class differs')
    if container.body[6235:6239]!=struct.pack('<I',0xFACADE01) or donor.body[1932:1936]!=struct.pack('<I',0xFACADE01): raise ValueError('typed material-node boundary differs')
    if struct.unpack_from('<iII',container.body,5962)!=(-1,0,1): raise ValueError('empty LOD array signature differs')
    if mode in ('material','combined'):
        patches.append({'offset':6057,'old':container.body[6057:6239],'new':donor.body[1794:1936],'kind':'exact visible PlatformTech material node'})
    if mode in ('lod','combined'):
        patches.append({'offset':5966,'old':struct.pack('<I',0),'new':struct.pack('<If',1,1000000.0),'kind':'one native LOD max distance'})
    body=splice(container.body,patches)
    if splice(body,patches,True)!=container.body: raise ValueError('source native body not exactly restorable')
    prefix=container.prefix.replace(old_id,new_id)
    result=GbxContainer(prefix,body,container.compression,container.node_count,container.references).write(body)
    output=output_folder/new_id.decode().split('\\')[-1];output.parent.mkdir(parents=True,exist_ok=True);output.write_bytes(result)
    manifest={'source':str(path),'sourceSha256':sha(original),'output':str(output),'bytes':len(result),'sha256':sha(result),
              'stageAt':'Items/'+new_id.decode().replace('\\','/'),'control':mode,'IsFogEmitter':kind=='volume',
              'material':('PlatformTech copied exactly from visible Sparkler' if mode in ('material','combined') else 'original AddSelfIllum'),
              'lodDistances':([1000000] if mode in ('lod','combined') else []),
              'recordedPatches':[{'sourceBodyOffset':p['offset'],'oldBytes':len(p['old']),'newBytes':len(p['new']),'kind':p['kind']}for p in patches],
              'opaqueFxAndPixelsUnchanged':True,'sourceBodyExactlyRestorable':True,'nativeVerdict':'pending isolated native visibility test'}
    output.with_suffix('.visibility-manifest.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
    return manifest

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--output-folder',type=Path,default=BASE/'visibility-controls');a=p.parse_args()
    print(json.dumps([generate('volume','material',2,a.output_folder),generate('volume','lod',3,a.output_folder),generate('volume','combined',4,a.output_folder),generate('sprite','combined',2,a.output_folder)],indent=2))
