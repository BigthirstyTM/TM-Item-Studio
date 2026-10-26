"""Keep Fogger's header/body Id definition slots but make their text empty.

This controlled native hypothesis changes no name, author, mesh, point, bitmap
or unknown FX bytes. It deliberately does not alter Studio's export policy.
"""
from pathlib import Path
import argparse
import hashlib
import json
import struct
from gbx_container import GbxContainer

PINNED='DA6C92A686303BC7D2C502E8BA05D736546FA0E303535CD08314777298B6C211'
IDENT=b'TMIS_Standalone_VolumeFogger_20261025'
def sha(data):return hashlib.sha256(data).hexdigest().upper()

def empty_definition(data,tag_offset):
    if struct.unpack_from('<II',data,tag_offset)!=(0x40000000,len(IDENT)):raise ValueError('expected existing native Id definition')
    text_offset=tag_offset+8
    if data[text_offset:text_offset+len(IDENT)]!=IDENT:raise ValueError('unexpected Id text')
    return data[:tag_offset+4]+struct.pack('<I',0)+data[text_offset+len(IDENT):]

def generate(source,output):
    original=source.read_bytes()
    if sha(original)!=PINNED:raise ValueError('source must be the original non-float Studio Fogger export')
    container=GbxContainer.read(original);prefix=container.prefix
    header_size,count=struct.unpack_from('<II',prefix,13)
    if not 1<=count<=32:raise ValueError('unsupported header chunk count')
    first_id,first_size=struct.unpack_from('<II',prefix,21);payload=21+count*8
    if first_id!=0x2E001003 or struct.unpack_from('<I',prefix,payload)[0]!=3:raise ValueError('unsupported collector header')
    header_tag=payload+4
    new_prefix=bytearray(empty_definition(prefix,header_tag))
    struct.pack_into('<I',new_prefix,13,header_size-len(IDENT))
    struct.pack_into('<I',new_prefix,25,(first_size&0x80000000)|((first_size&0x7FFFFFFF)-len(IDENT)))
    chunk=container.body.index(struct.pack('<I',0x2E00100B),0,350);body_tag=chunk+4
    body=empty_definition(container.body,body_tag)
    # Reverse the exact initial metadata splice before emitting a candidate.
    restored=body[:body_tag+4]+struct.pack('<I',len(IDENT))+IDENT+body[body_tag+8:]
    if restored!=container.body:raise ValueError('all original non-Id body bytes were not preserved')
    result=GbxContainer(bytes(new_prefix),body,container.compression,container.node_count,container.references).write(body)
    reopened=GbxContainer.read(result)
    if reopened.body!=body or reopened.node_count!=container.node_count:raise ValueError('container rewrite differs')
    output.parent.mkdir(parents=True,exist_ok=True);output.write_bytes(result)
    report={'source':str(source.resolve()),'sourceSha256':sha(original),'output':str(output.resolve()),'bytes':len(result),'sha256':sha(result),
            'headerIdTag':'0x40000000','bodyIdTag':'0x40000000','newHeaderIdTextLength':0,'newBodyIdTextLength':0,
            'lookbackDefinitionSlotsPreserved':True,'allOtherBodyBytesExactlyRestorable':True,
            'nameAuthorPointMeshFxPixelsPreserved':True,'originalBitmapConfigurationPreserved':True,
            'headerIdTagOffset':header_tag,'bodyIdTagOffset':body_tag,
            'recommendedStageAt':'Items/TMIS_FX_SequentialProof/TMIS_Standalone_VolumeFogger_20261025.Item.Gbx',
            'nativeVerdict':'pending isolated native preload/catalog/placement; not a proven export policy'}
    output.with_suffix('.zero-id-manifest.json').write_text(json.dumps(report,indent=2),encoding='utf-8');return report

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--source',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args()
    print(json.dumps(generate(a.source,a.output),indent=2))
