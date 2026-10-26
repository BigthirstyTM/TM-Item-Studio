"""Correct camera-left labels from world positions; preserve native raw frames."""
import json
from pathlib import Path
import subprocess
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[2] / 'artifacts/flag-vanilla-steps-20261021'
CASES = {
    'native-vanilla-copy': (
        'Links: eigen native kopie | Rechts: vanilla',
        'Normaal ogend doek bewijst geen juiste eigen GPU-binding; zie het onderzoeksverslag'),
    'vanilla-shape-probes': (
        'Links: zwaluwstaart | Midden: 2,5x lang | Rechts: +2,5% breed',
        'Zonder correctie: de bedoelde eigen vormen worden niet correct getekend'),
    'diagnostic-srv-binding': (
        'TIJDELIJKE GPU-CORRECTIE | links zwaluwstaart | midden 2,5x lang | rechts +2,5%',
        'Echte ingame animatie; dit is nog GEEN zelfstandig werkende Item.Gbx'),
    'engine-structured-flag': (
        'TIJDELIJKE ENGINEFLAG | links zwaluwstaart | rechts 2,5x lang',
        'Eigen buffers via de normale renderer; nog GEEN zelfstandig werkende Item.Gbx'),
}

for name, (title, note) in CASES.items():
    folder = ROOT / 'motion' / name
    records = json.loads((folder / 'frames.json').read_text())
    for record in records:
        pic = Image.open(folder / record['raw']).convert('RGB')
        d = ImageDraw.Draw(pic)
        d.rectangle((0, 0, 1920, 104), fill=(17, 22, 28))
        d.text((960, 25), title, font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf', 26), anchor='mm', fill='white')
        d.text((960, 62), note, font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf', 20), anchor='mm', fill=(255, 196, 103))
        d.text((1900, 90), f"MapEditor | t={record['elapsed']:.2f}s | circa 3 bronbeelden/s",
               font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf', 20), anchor='rm', fill=(191, 205, 217))
        pic.save(folder / f"frame-{record['index']:03}.jpg", quality=93)
    (folder / 'corrected-caption.json').write_text(json.dumps({'title': title, 'note': note}, indent=2))
    subprocess.run(['ffmpeg', '-y', '-v', 'error', '-f', 'concat', '-safe', '0', '-i', str(folder / 'frames.ffconcat'),
                    '-r', '12', '-c:v', 'libx264', '-crf', '19', '-pix_fmt', 'yuv420p', '-movflags', '+faststart', str(ROOT / f'{name}.mp4')], check=True)
    print(name, len(records), records[-1]['elapsed'], flush=True)
