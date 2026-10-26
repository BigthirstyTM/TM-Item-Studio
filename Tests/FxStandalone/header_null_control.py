"""Restore only a new Sparkler's header Id to its native null representation.

The compressed body bytes, reference table and all remaining header fields are
preserved. This is a native catalog hypothesis control, not a proven fix.
"""
from pathlib import Path
import argparse
import hashlib
import json
import struct
from gbx_container import GbxContainer

PINNED={
    '670D6B2F4449115F0F70B65376D235758B8023B7FB5112570EE52860EF1C8F1D',
    '9D17392F6CB9672125EBBE5D51A420128C21B1B8F0DCFE687A8BB44E4C76B66F',
}
def sha(data):return hashlib.sha256(data).hexdigest().upper()

def generate(source,output):
    data=source.read_bytes()
    if sha(data) not in PINNED:raise ValueError('source must be one of the pinned Studio Sparkler controls')
    container=GbxContainer.read(data);prefix=container.prefix
    header_size,count=struct.unpack_from('<II',prefix,13)
    if not 1<=count<=32:raise ValueError('unsupported header chunk count')
    first_id,first_size=struct.unpack_from('<II',prefix,21)
    payload=21+count*8
    if first_id!=0x2E001003 or struct.unpack_from('<II',prefix,payload)!=(3,0x40000000):raise ValueError('expected first header collector Id definition')
    length=struct.unpack_from('<I',prefix,payload+8)[0]
    old_id=prefix[payload+12:payload+12+length]
    if old_id!=b'TMIS_Standalone_SparklerFlare_20261025':raise ValueError('unexpected control header Id')
    # Both subsequent Id-valued fields in this v8 header are independently
    # decoded by metadata_audit.py: numeric collection and a new author
    # definition. The optional later Id is null. Thus there are no lookback
    # aliases to reindex when this first definition is removed.
    author_start=payload+12+length
    if struct.unpack_from('<II',prefix,author_start)!=(26,0x40000000):raise ValueError('unsupported collection/author representation')
    author_length=struct.unpack_from('<I',prefix,author_start+8)[0]
    version_at=author_start+12+author_length
    if struct.unpack_from('<I',prefix,version_at)[0]!=8:raise ValueError('expected native collector header version8')
    page_length=struct.unpack_from('<I',prefix,version_at+4)[0]
    if struct.unpack_from('<I',prefix,version_at+8+page_length)[0]!=0xFFFFFFFF:raise ValueError('later header Id is not null; aliases unsupported')
    removed=4+length
    new_prefix=bytearray(prefix[:payload+4]+struct.pack('<I',0xFFFFFFFF)+prefix[payload+12+length:])
    struct.pack_into('<I',new_prefix,13,header_size-removed)
    size=(first_size&0x7FFFFFFF)-removed
    struct.pack_into('<I',new_prefix,25,(first_size&0x80000000)|size)
    result=bytes(new_prefix)+data[len(prefix):]
    reread=GbxContainer.read(result)
    if reread.body!=container.body or result[len(new_prefix):]!=data[len(prefix):]:raise ValueError('body or compressed body bytes changed')
    if reread.node_count!=container.node_count or reread.references!=container.references:raise ValueError('node/reference table changed')
    output.parent.mkdir(parents=True,exist_ok=True);output.write_bytes(result)
    report={'source':str(source.resolve()),'sourceSha256':sha(data),'output':str(output.resolve()),'bytes':len(result),'sha256':sha(result),
            'oldHeaderId':old_id.decode(),'newHeaderId':None,'bodyIdRemainsNull':True,'compressedBodyBytesExactlyPreserved':True,
            'referenceTableExactlyPreserved':True,'otherHeaderFieldsPreserved':True,
            'headerIdDefinitionOffset':payload+4,'headerSizeWordsAdjusted':True,
            'recommendedStageAt':'Items/TMIS_FX_SequentialProof/TMIS_Standalone_SparklerFlare_20261025.Item.Gbx',
            'nativeVerdict':'pending single-change cold preload/catalog/placement control'}
    output.with_suffix('.header-null-manifest.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    return report

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--source',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args()
    print(json.dumps(generate(a.source,a.output),indent=2))
