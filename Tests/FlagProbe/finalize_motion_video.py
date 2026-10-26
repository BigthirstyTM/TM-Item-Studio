"""Label the observed scope and preserve every directly captured video frame."""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess

import numpy as np
from PIL import Image, ImageDraw

OUT = Path(__file__).resolve().parents[2] / 'artifacts/flag-motion-embedded-20261022/kinematic/video-slow'
RAW = OUT / 'kinematic-waving-flags-raw.mp4'
FINAL = OUT / 'kinematic-rotation-cloth-frozen.mp4'


def probe(path):
    return json.loads(subprocess.check_output([shutil.which('ffprobe'), '-v', 'error',
        '-show_entries', 'stream=width,height,avg_frame_rate,nb_frames,duration',
        '-show_entries', 'format=size,duration', '-of', 'json', str(path)]))


def main():
    assert not FINAL.exists(), 'Preserve prior evidence'
    raw = probe(RAW)
    stream = raw['streams'][0]
    n, d = map(int, stream['avg_frame_rate'].split('/'))
    (OUT / 'title.txt').write_text(f'NATIVE KINEMATIC FLAG TEST  |  1080p / {n/d:.1f} fps')
    (OUT / 'scope.txt').write_text('Rigid motion works; cloth animation is frozen in this fixture. Requires installed files.')
    timeline = json.loads((OUT / 'timeline.json').read_text())
    filters = ['drawbox=x=0:y=0:w=iw:h=66:color=black@0.75:t=fill',
        'drawbox=x=0:y=985:w=iw:h=95:color=black@0.75:t=fill',
        r"drawtext=fontfile='C\:/Windows/Fonts/arial.ttf':textfile='title.txt':x=28:y=19:fontsize=27:fontcolor=white",
        r"drawtext=fontfile='C\:/Windows/Fonts/arial.ttf':textfile='scope.txt':x=28:y=1040:fontsize=21:fontcolor=white"]
    labels = ['Overview - stationary control and moving flag',
              'Stationary kinematic control - the cloth silhouette stays frozen',
              'Circular motion and rotation work - simultaneous waving is NOT proven']
    for i, (entry, label) in enumerate(zip(timeline, labels)):
        (OUT / f'label-{i}.txt').write_text(label)
        end = timeline[i+1]['seconds'] if i+1 < len(timeline) else float(raw['format']['duration'])
        filters.append(f"drawtext=fontfile='C\\:/Windows/Fonts/arial.ttf':textfile='label-{i}.txt':x=28:y=1001:fontsize=25:fontcolor=white:enable='gte(t,{entry['seconds']})*lt(t,{end})'")
    (OUT / 'filters.txt').write_text(','.join(filters))
    with (OUT / 'encode.log').open('w') as log:
        subprocess.run([shutil.which('ffmpeg'), '-hide_banner', '-n', '-i', RAW.name,
            '-filter_script:v', 'filters.txt', '-c:v', 'libx264', '-preset', 'medium', '-crf', '19',
            '-pix_fmt', 'yuv420p', '-fps_mode', 'passthrough', '-movflags', '+faststart', FINAL.name],
            cwd=OUT, stdout=log, stderr=log, check=True)
    final = probe(FINAL)
    assert final['streams'][0]['nb_frames'] == stream['nb_frames'], 'No added or dropped evidence frames'
    # Fixed camera and stationary body. This crop includes cloth and grass,
    # excluding timer, HUD, background flags and the rotating control.
    data = subprocess.check_output([shutil.which('ffmpeg'), '-v', 'error', '-ss', '6', '-i', str(RAW),
        '-t', '3', '-vf', 'crop=390:340:1050:500,scale=195:170,format=gray',
        '-fps_mode', 'passthrough', '-f', 'rawvideo', '-'])
    frames = np.frombuffer(data, np.uint8).reshape((-1, 170, 195))
    differences = np.abs(np.diff(frames.astype(np.float32), axis=0)).mean(axis=(1, 2))
    validation = {'raw': raw, 'final': final, 'sha256': hashlib.sha256(FINAL.read_bytes()).hexdigest(),
        'capture': 'Direct game-window capture requested at 60 Hz. No motion interpolation, speed change or frame generation.',
        'stationaryClothCrop': {'startSeconds': 6, 'durationSeconds': 3,
            'crop': [1050, 500, 390, 340], 'decodedFrames': len(frames),
            'distinctFrames': len({hashlib.sha256(f.tobytes()).digest() for f in frames}),
            'meanAbsoluteConsecutiveDifferenceMedian': float(np.median(differences))}}
    validation['silhouetteGate'] = json.loads((OUT / 'stationary-cloth-verification.json').read_text())
    validation['conclusion'] = 'Rigid rotation observed; cloth silhouette is frozen. Distinct encoded frames are insufficient to prove waving.'
    (OUT / 'validation-frozen-control.json').write_text(json.dumps(validation, indent=2))
    # Same nominal rigid pose one 12-second rotation later. This comparison
    # does not establish independent cloth animation.
    pair = Image.new('RGB', (1500, 480))
    for i, second in enumerate([11, 23]):
        image = Image.open(OUT / f'frame-{second:02}.png').crop((450, 460, 1200, 900))
        pair.paste(image, (i*750, 40))
        ImageDraw.Draw(pair).text((20+i*750, 12), f'{second}s - one kinematic cycle apart', fill='white')
    pair.save(OUT / 'same-rigid-pose-comparison.jpg')
    subprocess.run([shutil.which('ffmpeg'), '-v', 'error', '-ss', '17', '-i', str(FINAL),
        '-frames:v', '1', '-n', str(OUT / 'poster-frozen-control.jpg')], check=True)
    print(json.dumps(validation, indent=2))


if __name__ == '__main__':
    main()
