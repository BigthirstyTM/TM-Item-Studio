"""Compile and natively preload an authored HLSL source, never a flag fixture.

This determines whether user-file HLSL can supply a native CPlugFileVHlsl node.
The deliberately minimal smoke shader is never rendered or sold as flag parity.
"""
import ctypes as c
import hashlib
import json
from pathlib import Path
import shutil

import game

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'artifacts/flag-native-primer-20261021/authored-hlsl-probe'
LIVE=Path.home()/'Documents/Trackmania/Items/TMIS_AuthoredHlslProbe'
SOURCE=b'''// Authored loader smoke test, not a finished Trackmania flag shader.
StructuredBuffer<float3> FlagPositions : register(t0);
float4 main(uint vertexId : SV_VertexID) : SV_Position
{
    return float4(FlagPositions[vertexId], 1.0);
}
'''


def blob_bytes(ptr):
    vt=c.cast(ptr,c.POINTER(c.POINTER(c.c_void_p))).contents
    getp=c.WINFUNCTYPE(c.c_void_p,c.c_void_p)(vt[3])
    gets=c.WINFUNCTYPE(c.c_size_t,c.c_void_p)(vt[4])
    return c.string_at(getp(ptr),gets(ptr))


def main():
    assert not OUT.exists() and not LIVE.exists()
    OUT.mkdir();LIVE.mkdir();game.destination=OUT
    assert game.call('GetMode')['mode']=='Menu'
    d=c.WinDLL('d3dcompiler_47.dll')
    compile=d.D3DCompile
    compile.argtypes=[c.c_void_p,c.c_size_t,c.c_char_p,c.c_void_p,c.c_void_p,
        c.c_char_p,c.c_char_p,c.c_uint,c.c_uint,c.POINTER(c.c_void_p),c.POINTER(c.c_void_p)]
    compile.restype=c.c_long
    code=c.c_void_p();errors=c.c_void_p()
    hr=compile(SOURCE,len(SOURCE),b'TMIS_Authored_v.hlsl',None,None,b'main',b'vs_5_0',0,0,c.byref(code),c.byref(errors))
    diagnostics=blob_bytes(errors).decode('utf-8',errors='replace') if errors else ''
    assert hr==0,(hr,diagnostics)
    dxbc=blob_bytes(code);assert dxbc[:4]==b'DXBC'
    (OUT/'TMIS_Authored_v.dxbc').write_bytes(dxbc)
    result={'sourceSha256':hashlib.sha256(SOURCE).hexdigest(),'D3DCompile':{
        'hresult':hr,'profile':'vs_5_0','entry':'main','bytes':len(dxbc),'diagnostics':diagnostics}}
    for name in ['TMIS_Authored_v.hlsl','TMIS_Authored.VHlsl.Txt']:
        (LIVE/name).write_bytes(SOURCE);(OUT/name).write_bytes(SOURCE)
    (OUT/'compile-and-staging.json').write_text(json.dumps(result,indent=2)+'\n')
    result['update']=game.call('ControlFids',{'action':'updateTree','drive':'user',
        'path':'Items','withFiles':True})
    for name in ['TMIS_Authored_v.hlsl','TMIS_Authored.VHlsl.Txt']:
        try:result[name]=game.call('ControlFids',{'action':'preload','drive':'user',
            'path':'Items/TMIS_AuthoredHlslProbe/'+name})
        except Exception as e:result[name]={'error':str(e)}
        print(name,json.dumps(result[name]),flush=True)
    (OUT/'results.json').write_text(json.dumps(result,indent=2)+'\n')


if __name__=='__main__':main()
