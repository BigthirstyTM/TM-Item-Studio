"""Pinned offline evidence for repairing a native SaveAnyway empty embed chunk.

This is not a production repair API or a playable-map generator. The source
has no native start. It adds only the selected exact Item and its metadata to
the existing independently scoped 03043054 chunk, preserving every other byte.
"""
from pathlib import Path
import argparse
import hashlib
import io
import json
import struct
import sys
import zipfile

ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'Tests/FxStandalone'))
sys.path.insert(0,r'C:/Users/PC/Downloads/Blender Addons/blendermania-addon/blendermania-addon/NICE')
from gbx_container import GbxContainer
from src.gbx_structs import body_chunks

MAP_SHA='633F22C8DB7376EE859D523CFC2F6B82C3BA265BBF3C5EAB84181929E66812F0'
ITEM_SHA='3C1F8FA89642AD33E918BAD8AC323CA8CE0A3EB2A40204B767C8AC8541863B44'
IDENT=r'TMIS_FX_SequentialProof\TMIS_Sparkler_FidStadiumBackslash_20261025.Item.Gbx'
AUTHOR='5X7FYPqoTyWnW8fpyvCv5Q'
SIGNATURE=struct.pack('<I',0x03043054)+b'PIKS'
EMPTY=bytes.fromhex('01000000000000000c000000000000000000000000000000')

def sha(data):return hashlib.sha256(data).hexdigest().upper()
def u32(value):return struct.pack('<I',value)
def text(value):
    data=value.encode('utf-8');return u32(len(data))+data

def generate(source,item,output):
    if output.exists():raise ValueError('choose unused output directory')
    original=source.read_bytes();item_bytes=item.read_bytes()
    if sha(original)!=MAP_SHA or sha(item_bytes)!=ITEM_SHA:raise ValueError('pinned source evidence differs')
    archive=GbxContainer.read(original)
    if archive.body.count(SIGNATURE)!=1:raise ValueError('embed chunk is not unique')
    at=archive.body.index(SIGNATURE);old_size=struct.unpack_from('<I',archive.body,at+8)[0]
    if old_size!=len(EMPTY) or archive.body[at+12:at+12+old_size]!=EMPTY:raise ValueError('not the pinned native empty chunk')
    memory=io.BytesIO()
    zip_name='Items/'+IDENT.replace('\\','/')
    with zipfile.ZipFile(memory,'w',compression=zipfile.ZIP_DEFLATED)as z:
        entry=zipfile.ZipInfo(zip_name,date_time=(2026,10,25,0,0,0));entry.compress_type=zipfile.ZIP_DEFLATED
        z.writestr(entry,item_bytes)
    zip_bytes=memory.getvalue()
    # Inner lookback context is independent of every outer map string table.
    # count1, first-lookback version3, fresh Id definition, collection26,
    # fresh author definition, exact normal ZIP, empty external-texture list.
    metadata=u32(1)+u32(3)+u32(0x40000000)+text(IDENT)+u32(26)+u32(0x40000000)+text(AUTHOR)
    enclosed=metadata+u32(len(zip_bytes))+zip_bytes+u32(0)
    payload=u32(1)+u32(0)+u32(len(enclosed))+enclosed
    parsed=body_chunks[0x03043054].parse(payload,gbx_data={},nodes=[None]*archive.node_count,load_external_file=lambda _:None)
    metas=parsed.embeddedData.filesMeta
    if len(metas)!=1 or metas[0].id!=IDENT or metas[0].author!=AUTHOR:raise ValueError('independent metadata parse differs')
    z=parsed.embeddedData.zip
    if z.namelist()!=[zip_name]or z.read(zip_name)!=item_bytes:raise ValueError('independent exact ZIP parse differs')
    new_chunk=SIGNATURE+u32(len(payload))+payload
    body=archive.body[:at]+new_chunk+archive.body[at+12+old_size:]
    restored=body[:at]+SIGNATURE+u32(old_size)+EMPTY+body[at+len(new_chunk):]
    if restored!=archive.body:raise ValueError('complete map body not recoverable')
    result=archive.write(body)
    check=GbxContainer.read(result)
    if check.prefix!=archive.prefix or check.body!=body:raise ValueError('raw map container differs')
    output.mkdir(parents=True)
    destination=output/'TMIS_Sparkler_EmbedChunkProof_NoStart_20261025.Map.Gbx'
    destination.write_bytes(result)
    report={'operation':'pinned-native-empty-embedded-chunk-file-proof','sourceMap':str(source.resolve()),'sourceMapSha256':sha(original),
            'sourceItem':str(item.resolve()),'sourceItemSha256':sha(item_bytes),'outputMap':str(destination.resolve()),'outputSha256':sha(result),
            'embeddedChunkOffset':at,'oldPayloadBytes':old_size,'newPayloadBytes':len(payload),
            'embeddedIdentity':IDENT,'embeddedAuthor':AUTHOR,'zipPath':zip_name,'zipBytes':len(zip_bytes),
            'independentNiceMetadataAndExactZipReparsePassed':True,'headerAndRefTableByteExact':True,
            'allUncompressedMapBytesOutsideChunk054Exact':True,'originalCompleteUncompressedBodyExactlyRestorable':True,
            'allAnchorsStartBlocksValidationGhostAndUnknownOtherChunksPreserved':True,'mapOrItemNodeReserializationPerformed':False,
            'inputsUnchanged':source.read_bytes()==original and item.read_bytes()==item_bytes,
            'scope':'Offline packaging-mechanism evidence only. Source has NO native start. This does not establish gameplay, native editor SaveMapAs support or rendered particles. No staging/runtime actions.'}
    destination.with_suffix(destination.suffix+'.manifest.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    return report

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('source',type=Path);p.add_argument('item',type=Path);p.add_argument('output',type=Path);a=p.parse_args()
    print(json.dumps(generate(a.source,a.item,a.output),indent=2))
