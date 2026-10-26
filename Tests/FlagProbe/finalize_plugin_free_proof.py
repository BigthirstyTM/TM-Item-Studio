"""Validate the cold, injection-free capture; captions do not add motion."""
import hashlib
import json
from pathlib import Path
import subprocess
import sys

import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[2]
BASE = ROOT / 'artifacts/flag-file-followup-20261022/plugin-free'
CATALOG_FREE = sys.argv[1:] == ['--catalog-free']
if sys.argv[1:] and not CATALOG_FREE:
    raise SystemExit('Only --catalog-free is supported')
OUT = BASE.parent / 'catalog-free/plugin-free' if CATALOG_FREE else BASE / 'cold2'
RAW = OUT / 'plugin-free-raw.mp4'
FINAL = OUT / 'custom-flags-without-openplanet.mp4'


def probe(path):
    return json.loads(subprocess.check_output(['ffprobe', '-v', 'error', '-select_streams', 'v:0',
        '-show_entries', 'stream=width,height,avg_frame_rate,nb_frames,duration',
        '-show_entries', 'format=size,duration', '-of', 'json', str(path)]))


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    before = json.loads((OUT / 'modules-before.json').read_text(encoding='utf-8-sig'))
    after = json.loads((OUT / 'modules-after.json').read_text(encoding='utf-8-sig'))
    for modules in [before, after]:
        assert not any('openplanet' in m['FileName'].lower() for m in modules)
        proxy = [m for m in modules if m['ModuleName'].lower() == 'dinput8.dll']
        assert len(proxy) == 1 and proxy[0]['FileName'].lower() == r'c:\windows\system32\dinput8.dll'
    staging = json.loads((BASE / 'staging.json').read_text())
    for row in staging['assets']:
        if CATALOG_FREE and row['path'].endswith('.json'):
            assert not Path(row['path']).exists()
        else:
            assert digest(Path(row['path'])) == row['sha256'].lower()
    assert not Path(staging['proxy']['path']).exists()
    assert digest(Path(staging['proxy']['held'])) == staging['proxy']['sha256']
    boundary_path = BASE.parent / 'catalog-free/plugin-free-boundary.json' if CATALOG_FREE else BASE / 'second-plugin-free-boundary.json'
    boundary = json.loads(boundary_path.read_text())
    log = Path.home() / 'OpenplanetNext/Openplanet.log'
    assert log.stat().st_size == boundary['size']
    assert abs(log.stat().st_mtime - boundary['mtime']) < .01
    map_receipt = json.loads((BASE / 'proof-map.json').read_text(encoding='utf-16'))
    assert digest(Path(map_receipt['output'])) == map_receipt['sha256'].lower()
    raw = probe(RAW)
    top, bottom = OUT / 'top.txt', OUT / 'bottom.txt'
    rate = raw['streams'][0]['avg_frame_rate']
    a, b = map(int, rate.split('/'))
    top.write_text(f'COLD GAME | NO OPENPLANET | THREE INSTALLED CUSTOM FLAGS | {a/b:.2f} captured fps')
    bottom.write_text('Round (left) / swallowtail (middle) / long (right). Requires 3 game-directory items' +
        ('. No inventory overlay or native primer.' if CATALOG_FREE else ' + inventory file. No native primer.'))
    filters = [
        'drawbox=x=0:y=0:w=iw:h=65:color=black@0.8:t=fill',
        'drawbox=x=0:y=1015:w=iw:h=65:color=black@0.8:t=fill',
        r"drawtext=fontfile='C\:/Windows/Fonts/arial.ttf':textfile='top.txt':x=22:y=20:fontsize=25:fontcolor=white",
        r"drawtext=fontfile='C\:/Windows/Fonts/arial.ttf':textfile='bottom.txt':x=22:y=1037:fontsize=20:fontcolor=white",
    ]
    if not FINAL.exists():
        with (OUT / 'caption-encode.log').open('w') as output:
            subprocess.run(['ffmpeg', '-hide_banner', '-i', RAW.name, '-vf', ','.join(filters),
                '-c:v', 'libx264', '-preset', 'medium', '-crf', '18', '-pix_fmt', 'yuv420p',
                '-fps_mode', 'passthrough', '-movflags', '+faststart', FINAL.name], cwd=OUT,
                stdout=output, stderr=output, check=True)
    final = probe(FINAL)
    assert final['streams'][0]['nb_frames'] == raw['streams'][0]['nb_frames']
    motion = []
    # Fixed camera. Crops contain cloth only, excluding poles, UI and cursor.
    for shape, x in [('Round', 545), ('Swallowtail', 865), ('Long', 1180)]:
        data = subprocess.check_output(['ffmpeg', '-v', 'error', '-ss', '2', '-i', str(RAW),
            '-t', '12', '-vf', f'crop=130:110:{x}:480,format=gray', '-fps_mode', 'passthrough',
            '-f', 'rawvideo', '-'])
        frames = np.frombuffer(data, dtype=np.uint8).reshape((-1, 110, 130))
        diff = np.abs(np.diff(frames.astype(np.float32), axis=0)).mean(axis=(1, 2))
        motion.append({'shape': shape, 'clothCrop': [x, 480, 130, 110], 'frames': len(frames),
            'distinctFrames': len({hashlib.sha256(f.tobytes()).digest() for f in frames}),
            'medianFrameDifference': float(np.median(diff))})
    assert all(m['distinctFrames'] > 100 and m['medianFrameDifference'] > .05 for m in motion)
    sheet = Image.new('RGB', (1440, 850), 'white')
    draw = ImageDraw.Draw(sheet)
    for i, sec in enumerate([1, 5, 9, 13]):
        frame = OUT / f'frame-{sec:02}.png'
        if not frame.exists():
            subprocess.run(['ffmpeg', '-v', 'error', '-ss', str(sec), '-i', str(RAW),
                '-frames:v', '1', str(frame)], check=True)
        picture = Image.open(frame).crop((480, 450, 1410, 740)).resize((1380, 430))
        if i < 2:
            sheet.paste(picture.resize((690, 215)), ((i % 2)*720, 30))
        else:
            sheet.paste(picture.resize((690, 215)), ((i % 2)*720, 285))
        draw.text(((i % 2)*720+10, (0 if i < 2 else 255)+10), f'Real game frame t={sec}s', fill='black')
    # Keep an unclipped overview below the cloth comparisons.
    sheet.paste(Image.open(OUT / 'frame-01.png').resize((600, 337)), (420, 510))
    sheet.save(OUT / 'motion-contact-sheet.jpg', quality=94)
    result = {'raw': raw, 'final': final, 'capturedFps': a/b, 'clothMotion': motion,
        'sha256': digest(FINAL), 'openplanetAbsentBeforeAndAfter': True,
        'openplanetLogUnchanged': True, 'acceptedInstallationHashesUnchanged': True,
        'catalogAbsent': CATALOG_FREE,
        'map': map_receipt,
        'method': 'Direct game-window capture, requested 60 Hz; actual rate reported. No interpolation or speed change.',
        'scope': 'Game-directory installation only; not portable map embedding or full vanilla parity.'}
    (OUT / 'validation.json').write_text(json.dumps(result, indent=2))
    print(json.dumps(result, indent=2))


if __name__ == '__main__':
    main()
