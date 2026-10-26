"""Recorded game-drive controls; never promote preload to visible success."""
import argparse
import json
from pathlib import Path
import shutil

import game
from read_flag_visuals import read

ROOT = Path(__file__).resolve().parents[2]
GAME = Path('C:/Program Files (x86)/Ubisoft/Ubisoft Game Launcher/games/Trackmania')


def run(out):
    game.destination = out
    manifest = json.loads((out/'files/manifest.json').read_text())
    receipt = out/'staging.json'
    if not receipt.exists():
        assert all(not (GAME/r['relative']).exists() for r in manifest)
        staged = []
        for row in manifest:
            target = GAME/row['relative']
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(Path(row['path']) if 'path' in row else out/'files'/row['relative'], target)
            staged.append({'path':str(target),'fidPath':row['relative'],'sha256':row['sha256']})
        receipt.write_text(json.dumps(staged,indent=2))
    results = {}
    for folder in dict.fromkeys(str(Path(r['relative']).parent).replace('\\','/') for r in manifest):
        game.call('ControlFids',{'action':'updateTree','drive':'game','path':folder,'withFiles':True})
    for row in manifest:
        path = row['relative']
        try:
            result = game.call('ControlFids',{'action':'preload','drive':'game','path':path})
            if path.endswith('.Mesh.Gbx'):
                ptr = result['nod']['ptr']
                assert read(ptr,0xB0,'u32') == 5
                table = read(ptr,0xA8)
                visuals = []
                for i in range(5):
                    visual = read(table,i*8)
                    flags = read(visual,0x24,'u32')
                    visuals.append({'ptr':visual,'frames':read(visual,0x118,'u32'),
                                    'flags':hex(flags),'structured':bool(flags&0x08000000)})
                result['visuals'] = visuals
            results[path] = result
        except Exception as error:
            results[path] = {'error':str(error)}
    name = 'read-results.json'
    assert not (out/name).exists()
    (out/name).write_text(json.dumps(results,indent=2))
    print(json.dumps({p:{'class':r.get('nod',{}).get('className'),
                         'flags':[v['structured'] for v in r.get('visuals',[])],
                         'error':r.get('error')} for p,r in results.items()},indent=2))


if __name__ == '__main__':
    p=argparse.ArgumentParser()
    p.add_argument('output',type=Path)
    run(p.parse_args().output)
