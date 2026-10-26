"""Independent NICE-schema audit of actual header and collector body metadata."""
from pathlib import Path
import argparse
import hashlib
import io
import json
import struct
import sys

ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'Tests/FxStandalone'))
sys.path.insert(0,r'C:\Users\PC\Downloads\Blender Addons\blendermania-addon\blendermania-addon\NICE')
from gbx_container import GbxContainer
from src.gbx_structs import GbxStructWithoutBodyParsed,body_chunks

AUTHORS=('5X7FYPqoTyWnW8fpyvCv5Q','-oTBhm4_S1-UxnlnBizUDA','TM_Item_Studio')

def audit(path):
    raw=path.read_bytes();container=GbxContainer.read(raw)
    header=GbxStructWithoutBodyParsed.parse(raw,gbx_data={},nodes=[],load_external_file=lambda _: {},errors=[],warns=[])
    hm=header.header.data[0]
    chunk=container.body.index(struct.pack('<I',0x2E00100B),0,350)
    stream=io.BytesIO(container.body);stream.seek(chunk+4)
    params={'gbx_data':{'lookbackstring_table':{},'lookbackstring_index':0,'lookbackstring_version':True},'nodes':[None]*container.node_count}
    body=body_chunks[0x2E00100B].parse_stream(stream,**params)['author']
    if stream.read(4)!=struct.pack('<I',0x2E00100C):raise ValueError('body name not immediately after native collector ident')
    name=body_chunks[0x2E00100C].parse_stream(stream,**params)['name']
    if stream.read(4)!=struct.pack('<I',0x2E00100D):raise ValueError('body description not immediately after name')
    description=body_chunks[0x2E00100D].parse_stream(stream,**params)['description']
    pick=lambda meta:{key:meta[key]for key in ('id','collection','author')}
    return {'file':str(path.resolve()),'bytes':len(raw),'sha256':hashlib.sha256(raw).hexdigest().upper(),
            'schema':'independent NICE CGameCtnCollector header003/body00B GbxMeta; 00C name; 00D description',
            'header':pick(hm.meta),'body':pick(body),'headerName':hm.get('fileName'),
            'bodyName':name,'bodyDescription':description,'bodyIdentityTag':hex(struct.unpack_from('<I',container.body,chunk+4)[0]),
            'headerBodyAuthorMatch':hm.meta.author==body.author,
            'headerBodyIdMatchOrNativeNull':not body.id or hm.meta.id==body.id,
            'originalAndStudioAuthorOccurrences':{author:{'header':container.prefix.count(author.encode()),'body':container.body.count(author.encode())}for author in AUTHORS}}

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('files',type=Path,nargs='+');p.add_argument('--output',type=Path);args=p.parse_args()
    report={'audits':[audit(path)for path in args.files],
            'interpretation':'Metadata is decoded independently of the Studio FxIdentity helper. Matching header/body fields does not establish native catalog placement or visible particle rendering.'}
    text=json.dumps(report,indent=2)
    if args.output:args.output.parent.mkdir(parents=True,exist_ok=True);args.output.write_text(text,encoding='utf-8')
    print(text)
