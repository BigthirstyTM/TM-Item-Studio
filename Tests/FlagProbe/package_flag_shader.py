"""Stage actual native shader dependencies and compiled caches, without stubs.

The ordinary item and its relative references are address-free. This is a
preload-only experiment; a cache file is never substituted for a shader node.
"""
import hashlib
import json
import shutil
import struct
import sys
import zipfile
from pathlib import Path, PurePosixPath

import game

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'artifacts/flag-native-primer-20261021/local-cache-package'
EXTRACT=Path.home()/'OpenplanetNext/Extract'
LIVE=Path.home()/'Documents/Trackmania/Items/TMIS_LocalCachePackage'
CACHE=Path('C:/Program Files (x86)/Ubisoft/Ubisoft Game Launcher/games/Trackmania/Packs/GpuCache_D3D11_SM5.zip')


def references(data):
    assert data[:5]==b'GBX\x06\x00' and data[5:7]==b'BU'
    pos=17+struct.unpack_from('<I',data,13)[0]+4
    def integer():
        nonlocal pos
        v=struct.unpack_from('<i',data,pos)[0];pos+=4;return v
    def string():
        nonlocal pos
        n=integer();assert 0<=n<65536
        v=data[pos:pos+n].decode('utf-8');pos+=n;return v
    n=integer();assert 0<=n<500
    if not n:return 0,[]
    ancestor=integer();assert 0<=ancestor<100
    folders=['']
    def dirs(parent):
        for _ in range(integer()):
            s=parent+string()+'/'
            folders.append(s);dirs(s)
    dirs('')
    refs=[]
    for _ in range(n):
        flags=integer();assert not flags&4,'Resource-table references require separate inspection'
        name=string();node=integer();use=integer();folder=integer()
        assert 0<=folder<len(folders)
        refs.append({'path':folders[folder]+name,'flags':flags,'nodeIndex':node,'useFile':use})
    return ancestor,refs


def prepare():
    assert not OUT.exists() and not LIVE.exists(),'Preserve prior package'
    game.destination=OUT.parent
    assert game.call('GetMode')['mode']=='Menu'
    OUT.mkdir()
    package=OUT/'files';package.mkdir()
    queue=['GameData/Techno3/Media/Shader/Tech3_Warp_TDiffSpec_VertexTween.Shader.Gbx']
    rows=[];seen=set()
    while queue:
        path=queue.pop(0)
        if path.lower() in seen:continue
        seen.add(path.lower());assert len(seen)<128,'Bounded native dependency graph'
        source=EXTRACT/Path(path)
        row={'gamePath':path}
        # Virtual HLSL has zero extracted bytes. Preserve this as missing source,
        # and copy the real compiled cache separately; never invent source files.
        if path.lower().endswith('.hlsl'):
            row['virtualSource']=game.call('ControlFids',{'action':'get','drive':'game','path':path,'includeNod':True})
            row['copied']=False
        else:
            if not source.exists():
                row['extraction']=game.call('ControlFids',{'action':'extract','drive':'game','path':path})
            assert source.exists(),str(source)
            data=source.read_bytes();assert data
            dest=package/Path(path);dest.parent.mkdir(parents=True,exist_ok=True);dest.write_bytes(data)
            row.update(copied=True,bytes=len(data),sha256=hashlib.sha256(data).hexdigest())
            if data[:3]==b'GBX':
                ancestor,refs=references(data)
                parent=PurePosixPath(path).parent
                for _ in range(ancestor):parent=parent.parent
                row.update(ancestor=ancestor,references=refs)
                queue.extend(str(parent/PurePosixPath(r['path'].replace('\\','/'))) for r in refs)
        rows.append(row)
        (OUT/'dependency-manifest.json').write_text(json.dumps(rows,indent=2)+'\n')
        print(path,'copied',row['copied'],flush=True)
    with zipfile.ZipFile(CACHE) as z:
        names=[r['gamePath'][len('GameData/Shaders/'):]+'.GpuCache.Gbx' for r in rows if not r['copied']]
        assert len(names)==9 and all(n in z.namelist() for n in names),names
        cache_rows=[]
        for name in names:
            data=z.read(name);dest=package/'GameData/Shaders'/Path(name)
            dest.parent.mkdir(parents=True,exist_ok=True);dest.write_bytes(data)
            cache_rows.append({'zipEntry':name,'relativeFile':str(dest.relative_to(package)),
                'bytes':len(data),'sha256':hashlib.sha256(data).hexdigest()})
    (OUT/'compiled-cache-manifest.json').write_text(json.dumps(cache_rows,indent=2)+'\n')
    seed=ROOT/'artifacts/flag-standalone-20261021/shader-paths/TMIS_Path_Relative0.Item.Gbx'
    shutil.copy2(seed,package/'TMIS_LocalCache_Long.Item.Gbx')
    shutil.copytree(package,LIVE)
    print('STAGED',LIVE,flush=True)


if __name__=='__main__':prepare()
