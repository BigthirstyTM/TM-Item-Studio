"""Label the observed non-kinematic constraint result; preserve captured frames."""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess

OUT = Path(__file__).resolve().parents[2] / 'artifacts/flag-motion-embedded-20261022/nonkinematic-constraints/cold1'
RAW = OUT / 'orbit-control.mp4'
FINAL = OUT / 'nonkinematic-cloth-waves-motion-ignored.mp4'


def probe(path):
    return json.loads(subprocess.check_output([shutil.which('ffprobe'), '-v', 'error',
        '-show_entries', 'stream=width,height,avg_frame_rate,nb_frames,duration',
        '-show_entries', 'format=size,duration', '-of', 'json', str(path)]))


def main():
    assert not FINAL.exists(), 'Preserve prior evidence'
    raw = probe(RAW)
    n, d = map(int, raw['streams'][0]['avg_frame_rate'].split('/'))
    (OUT / 'control-title.txt').write_text(f'NON-KINEMATIC CONSTRAINT CONTROL | 1080p / {n/d:.1f} fps')
    (OUT / 'control-result.txt').write_text('Cloth waves; requested circular motion is ignored. Installed test assets required.')
    filters = ['drawbox=x=0:y=0:w=iw:h=66:color=black@0.75:t=fill',
        'drawbox=x=0:y=1000:w=iw:h=80:color=black@0.75:t=fill',
        r"drawtext=fontfile='C\:/Windows/Fonts/arial.ttf':textfile='control-title.txt':x=28:y=19:fontsize=27:fontcolor=white",
        r"drawtext=fontfile='C\:/Windows/Fonts/arial.ttf':textfile='control-result.txt':x=28:y=1020:fontsize=25:fontcolor=white"]
    (OUT / 'control-filters.txt').write_text(','.join(filters))
    with (OUT / 'control-encode.log').open('w') as log:
        subprocess.run([shutil.which('ffmpeg'), '-hide_banner', '-n', '-i', RAW.name,
            '-filter_script:v', 'control-filters.txt', '-c:v', 'libx264', '-preset', 'medium',
            '-crf', '19', '-pix_fmt', 'yuv420p', '-fps_mode', 'passthrough',
            '-movflags', '+faststart', FINAL.name], cwd=OUT, stdout=log, stderr=log, check=True)
    final = probe(FINAL)
    assert raw['streams'][0]['nb_frames'] == final['streams'][0]['nb_frames']
    result = {'raw': raw, 'final': final,
        'sha256': hashlib.sha256(FINAL.read_bytes()).hexdigest(),
        'result': 'Independent cloth deformation observed; the pole remains stationary despite the retained orbit constraint.',
        'capture': 'Direct window capture requested at 60 Hz. No generated frames, interpolation or speed changes.'}
    (OUT / 'control-video-validation.json').write_text(json.dumps(result, indent=2))
    subprocess.run([shutil.which('ffmpeg'), '-v', 'error', '-ss', '4', '-i', str(FINAL),
        '-frames:v', '1', '-n', str(OUT / 'control-poster.jpg')], check=True)
    print(json.dumps(result, indent=2))


if __name__ == '__main__':
    main()
