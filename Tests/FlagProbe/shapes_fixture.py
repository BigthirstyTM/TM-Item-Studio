"""Reproducible ingame test and native-frame recording of the shape experiments.

Uses only the installed localhost bridge. Raw frames and actual elapsed times
are retained; encoding never generates/interpolates cloth motion.
"""
import datetime
import json
from pathlib import Path
import shutil
import subprocess
import sys
import time

from PIL import Image, ImageDraw, ImageFont
import game

ROOT = Path(__file__).resolve().parents[2] / 'artifacts/flag-shapes-20261021'
game.destination = ROOT
call = game.call
TAG = 'tmis:flag-shapes-20261021'
MODELS = ['Flag16m'] + [rf'TMIS_FlagShapes\TMIS_Shape11_{s}.Item.Gbx'
                                for s in ['Control', 'Long250', 'Swallowtail']]


def shot(label):
    began = time.time()
    result = call('TakeScreenshot', {'currentView': True, 'hideOverlay': True,
                  'format': 'webp', 'forceRes': True, 'width': 1920, 'height': 1080})
    assert result.get('detected'), result
    source = Path(result['fullName'])
    deadline = time.monotonic() + 3
    while source.stat().st_mtime < began - 0.1 and time.monotonic() < deadline:
        time.sleep(0.025)
    assert source.stat().st_mtime >= began - 0.1, 'Reject stale native screenshot'
    target = ROOT / f'{label}.webp'
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(source, target)
    with (ROOT / 'captures.jsonl').open('a', encoding='utf-8') as f:
        f.write(json.dumps({'utc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
                           'source': str(source), 'target': str(target), 'capture': result}) + '\n')
    return target


def setup():
    assert call('GetMode')['mode'] == 'MapEditor'
    info = call('GetMapInfo')
    assert info['name'] == 'Unnamed' and info['nbItems'] == 0
    for _ in range(20):
        inventory = call('tm-mcp-pack-epp.FindInventory', {'query': 'TMIS_Shape11', 'limit': 10})
        if inventory.get('count', 0) >= 3:
            break
        time.sleep(1)
    else:
        raise RuntimeError(f'Inventory not populated: {inventory}')
    call('tm-mcp-pack-epp.SetAgentTag', {'tag': TAG})
    for i, model in enumerate(MODELS):
        p = call('tm-mcp-pack-epp.PlaceItem', {'itemPath': model, 'x': 1360 + 20 * i,
                 'y': 8, 'z': 1360, 'yaw': 1.57079632679, 'autofocus': False})
        assert p['placed'] and p['afterItems'] == p['beforeItems'] + 1
    items = call('GetItems', {'limit': 20})
    assert items['total'] == 4
    (ROOT / 'placed-items.json').write_text(json.dumps(items, indent=2), encoding='utf-8')
    for name, z in [('RoadTechStart', 14), ('RoadTechStraight', 15), ('RoadTechStraight', 16),
                    ('RoadTechStraight', 17), ('RoadTechFinish', 18)]:
        assert call('tm-mcp-pack-epp.PlaceGridBlock', {'blockName': name, 'coord': [10, 9, z],
                    'dir': 'North', 'onGround': True, 'autofocus': False})['placed']
    call('ControlOverlay', {'action': 'hide'})
    call('ControlCamera', {'action': 'look', 'direction': 'North', 'smooth': False})
    call('ControlCamera', {'action': 'setVStep', 'step': 'Low', 'smooth': False})
    call('SetEditorCamera', {'x': 1390, 'y': 19, 'z': 1360, 'distance': 65})
    time.sleep(1)
    print(shot('evidence/editor-comparison'), flush=True)


def record(name, title, seconds=22, note='Echte native gameframes; afwijkende doekvorm wordt onderzocht'):
    folder = ROOT / 'motion' / name
    assert not folder.exists(), 'Do not overwrite previous evidence'
    folder.mkdir(parents=True)
    mode = call('GetMode')['mode']
    scene = {'mode': mode, 'title': title, 'items': call('GetItems', {'limit': 20}),
             'camera': call('ControlPlayCamera', {'action': 'get'}) if mode == 'Race'
                       else call('ControlCamera', {'action': 'status'})}
    (folder / 'scene.json').write_text(json.dumps(scene, indent=2), encoding='utf-8')
    font = ImageFont.truetype('C:/Windows/Fonts/arial.ttf', 26)
    small = ImageFont.truetype('C:/Windows/Fonts/arial.ttf', 20)
    records = []
    start = time.monotonic()
    index = 0
    while index == 0 or time.monotonic() - start < seconds:
        time.sleep(max(0, start + index / 3 - time.monotonic()))
        elapsed = time.monotonic() - start
        raw = shot(f'motion/{name}/raw-{index:03}')
        picture = Image.open(raw).convert('RGB')
        draw = ImageDraw.Draw(picture)
        draw.rectangle((0, 0, 1920, 104), fill=(17, 22, 28))
        draw.text((960, 25), title, font=font, anchor='mm', fill='white')
        draw.text((960, 62), note,
                  font=small, anchor='mm', fill=(255, 196, 103))
        draw.text((1900, 90), f'{mode} | t={elapsed:.2f}s | circa 3 opgenomen beelden/s',
                  font=small, anchor='rm', fill=(191, 205, 217))
        picture.save(folder / f'frame-{index:03}.jpg', quality=93)
        records.append({'index': index, 'elapsed': elapsed, 'raw': raw.name})
        index += 1
    (folder / 'frames.json').write_text(json.dumps(records, indent=2), encoding='utf-8')
    concat = []
    for a, b in zip(records, records[1:]):
        concat.extend([f"file 'frame-{a['index']:03}.jpg'", f"duration {b['elapsed'] - a['elapsed']:.6f}"])
    concat.append(f"file 'frame-{records[-1]['index']:03}.jpg'")
    (folder / 'frames.ffconcat').write_text('\n'.join(concat) + '\n', encoding='utf-8')
    video = ROOT / f'{name}.mp4'
    subprocess.run(['ffmpeg', '-y', '-v', 'error', '-f', 'concat', '-safe', '0',
                    '-i', str(folder / 'frames.ffconcat'), '-r', '12', '-c:v', 'libx264',
                    '-crf', '19', '-pix_fmt', 'yuv420p', '-movflags', '+faststart', str(video)], check=True)
    print(json.dumps({'video': str(video), 'frames': len(records), 'seconds': records[-1]['elapsed']}), flush=True)


if __name__ == '__main__':
    if sys.argv[1] == 'setup':
        setup()
    elif sys.argv[1] == 'record':
        record(sys.argv[2], sys.argv[3])
