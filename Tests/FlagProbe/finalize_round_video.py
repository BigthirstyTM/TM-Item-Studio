"""Label and validate the direct capture without interpolating/duplicating frames."""
import hashlib
import json
import shutil
import subprocess
from pathlib import Path
import numpy as np

ROOT = Path(__file__).resolve().parents[2] / 'artifacts/flag-round-video-20261021'
raw = ROOT / 'three-flags-60fps-raw.mp4'
final = ROOT / 'custom-flags-swallowtail-long-round.mp4'
ffmpeg = shutil.which('ffmpeg')
timeline = json.loads((ROOT / 'video-timeline.json').read_text())
probe = json.loads(subprocess.check_output([shutil.which('ffprobe'), '-v', 'error', '-select_streams', 'v:0',
    '-show_entries', 'stream=width,height,r_frame_rate,avg_frame_rate,nb_frames,duration',
    '-show_entries', 'format=size,duration', '-of', 'json', str(raw)]))
stats = []
for label, start in [('swallowtail', 11), ('long', 24), ('round', 37)]:
    # Only the flag region during a stationary close-up; HUD and timer excluded.
    data = subprocess.check_output([ffmpeg, '-v', 'error', '-ss', str(start), '-i', str(raw), '-t', '3',
        '-vf', 'crop=900:600:500:280,scale=180:120,format=gray', '-fps_mode', 'passthrough', '-f', 'rawvideo', '-'])
    frames = np.frombuffer(data, dtype=np.uint8).reshape((-1,120,180))
    differences = np.abs(np.diff(frames.astype(np.float32),axis=0)).mean(axis=(1,2))
    stats.append({'shape':label, 'seconds':3, 'frames':len(frames), 'distinct_frame_hashes':len({hashlib.sha256(f.tobytes()).digest() for f in frames}),
        'consecutive_mean_absolute_difference': {'min':float(differences.min()), 'median':float(np.median(differences)), 'max':float(differences.max())},
        'transitions_above_0_1_gray_levels':int((differences>.1).sum())})
(ROOT / 'capture-validation.json').write_text(json.dumps({'raw':probe, 'cloth_motion_samples':stats,
    'method':'Direct window capture at requested 60 Hz. VFR encoding. No synthetic motion, speed change or frame duplication.'},indent=2))

header = ROOT / 'video-header.txt'
note = ROOT / 'video-note.txt'
header.write_text('CUSTOM FLAG SHAPES  |  TRACKMANIA  |  1080p / ~58 fps', encoding='utf-8')
note.write_text('Runtime-assisted proof. Standalone restart persistence is still under investigation.', encoding='utf-8')
filters = [
    'drawbox=x=0:y=0:w=iw:h=68:color=black@0.72:t=fill',
    'drawbox=x=0:y=960:w=iw:h=120:color=black@0.72:t=fill',
    r"drawtext=fontfile='C\:/Windows/Fonts/arial.ttf':textfile='video-header.txt':x=34:y=20:fontsize=28:fontcolor=white",
    r"drawtext=fontfile='C\:/Windows/Fonts/arial.ttf':textfile='video-note.txt':x=34:y=1030:fontsize=22:fontcolor=white",
]
chapters = [(0,'Overview - swallowtail / long / round'), (7,'Swallowtail - zoom and close-up'),
    (19.75,'Long cloth - 2.5x width - zoom and close-up'), (32.516,'Round cloth - zoom and close-up'),
    (45.266,'All three animated custom shapes')]
metadata = [';FFMETADATA1']
for i,(start,title) in enumerate(chapters):
    end = chapters[i+1][0] if i+1<len(chapters) else float(probe['format']['duration'])
    (ROOT / f'chapter-{i}.txt').write_text(title,encoding='utf-8')
    filters.append(f"drawtext=fontfile='C\\:/Windows/Fonts/arial.ttf':textfile='chapter-{i}.txt':x=34:y=983:fontsize=30:fontcolor=white:enable='gte(t,{start})*lt(t,{end})'")
    metadata += ['[CHAPTER]','TIMEBASE=1/1000',f'START={round(start*1000)}',f'END={round(end*1000)}',f'title={title}']
(ROOT / 'video-filter.txt').write_text(','.join(filters))
(ROOT / 'chapters.ffmetadata').write_text('\n'.join(metadata)+'\n')
with (ROOT / 'final-encode.log').open('w') as output:
    subprocess.run([ffmpeg,'-hide_banner','-y','-i',raw.name,'-i','chapters.ffmetadata','-map_metadata','1',
        '-filter_script:v','video-filter.txt','-c:v','libx264','-preset','medium','-crf','19','-pix_fmt','yuv420p',
        '-fps_mode','passthrough','-movflags','+faststart',final.name],cwd=ROOT,stdout=output,stderr=output,check=True)
final_probe = json.loads(subprocess.check_output([shutil.which('ffprobe'),'-v','error','-select_streams','v:0',
    '-show_entries','stream=width,height,r_frame_rate,avg_frame_rate,nb_frames,duration','-show_entries','format=size,duration',
    '-of','json',str(final)]))
assert final_probe['streams'][0]['nb_frames'] == probe['streams'][0]['nb_frames'], 'Do not generate or discard evidence frames'
(ROOT / 'final-video-metadata.json').write_text(json.dumps(final_probe,indent=2))
print(json.dumps({'final':str(final),'metadata':final_probe,'motion':stats},indent=2))
