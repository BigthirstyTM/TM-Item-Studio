"""Caption and check the file-only installation recording without new frames."""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import numpy as np

OUT=Path(__file__).resolve().parents[2]/'artifacts/flag-file-followup-20261022/native-package/video'
RAW=OUT/'three-flags-60fps-raw.mp4'
FINAL=OUT/'custom-flags-file-only-installation.mp4'
assert not FINAL.exists()
FFMPEG=shutil.which('ffmpeg')


def probe(path):
    return json.loads(subprocess.check_output([shutil.which('ffprobe'),'-v','error','-select_streams','v:0',
        '-show_entries','stream=width,height,r_frame_rate,avg_frame_rate,nb_frames,duration',
        '-show_entries','format=size,duration','-of','json',str(path)]))


raw=probe(RAW)
timeline=json.loads((OUT/'video-timeline.json').read_text())
motion=[]
for event in timeline:
    if not event['label'].endswith('close-up'):continue
    start=event['time']+.5
    data=subprocess.check_output([FFMPEG,'-v','error','-ss',str(start),'-i',str(RAW),'-t','3',
        '-vf','crop=900:600:500:280,scale=180:120,format=gray','-fps_mode','passthrough','-f','rawvideo','-'])
    frames=np.frombuffer(data,dtype=np.uint8).reshape((-1,120,180))
    diff=np.abs(np.diff(frames.astype(np.float32),axis=0)).mean(axis=(1,2))
    motion.append({'shape':event['label'],'start':start,'frames':len(frames),
        'distinctFrameHashes':len({hashlib.sha256(f.tobytes()).digest() for f in frames}),
        'consecutiveMeanAbsoluteDifference':{'min':float(diff.min()),'median':float(np.median(diff)),'max':float(diff.max())}})
rate=raw['streams'][0]['avg_frame_rate'];a,b=map(int,rate.split('/'));fps=a/b
(OUT/'video-header.txt').write_text(f'CUSTOM FLAG FILES | SAVED MAP AFTER RESTART | 1080p / {fps:.2f} fps')
(OUT/'video-note.txt').write_text('No flag companion. Requires custom game-drive files and a native inventory addition. Not a portable embedded item.')
filters=['drawbox=x=0:y=0:w=iw:h=68:color=black@0.72:t=fill',
    'drawbox=x=0:y=960:w=iw:h=120:color=black@0.72:t=fill',
    r"drawtext=fontfile='C\:/Windows/Fonts/arial.ttf':textfile='video-header.txt':x=30:y=20:fontsize=27:fontcolor=white",
    r"drawtext=fontfile='C\:/Windows/Fonts/arial.ttf':textfile='video-note.txt':x=30:y=1030:fontsize=22:fontcolor=white"]
chapters=[(0,'Overview - all three custom shapes')]+[(e['time'],e['label']) for e in timeline[1:] if not e['label'].endswith('close-up')]
metadata=[';FFMETADATA1']
for i,(start,title) in enumerate(chapters):
    end=chapters[i+1][0] if i+1<len(chapters) else float(raw['format']['duration'])
    (OUT/f'chapter-{i}.txt').write_text(title)
    filters.append(f"drawtext=fontfile='C\\:/Windows/Fonts/arial.ttf':textfile='chapter-{i}.txt':x=30:y=983:fontsize=30:fontcolor=white:enable='gte(t,{start})*lt(t,{end})'")
    metadata+=['[CHAPTER]','TIMEBASE=1/1000',f'START={round(start*1000)}',f'END={round(end*1000)}',f'title={title}']
(OUT/'video-filter.txt').write_text(','.join(filters))
(OUT/'chapters.ffmetadata').write_text('\n'.join(metadata)+'\n')
with (OUT/'final-encode.log').open('w') as log:
    subprocess.run([FFMPEG,'-hide_banner','-i',RAW.name,'-i','chapters.ffmetadata','-map_metadata','1',
        '-filter_script:v','video-filter.txt','-c:v','libx264','-preset','medium','-crf','19','-pix_fmt','yuv420p',
        '-fps_mode','passthrough','-movflags','+faststart',FINAL.name],cwd=OUT,stdout=log,stderr=log,check=True)
final=probe(FINAL)
assert final['streams'][0]['nb_frames']==raw['streams'][0]['nb_frames']
assert len(motion)==3 and all(m['distinctFrameHashes']>100 for m in motion)
result={'raw':raw,'final':final,'measuredFps':fps,'clothMotion':motion,
    'sha256':hashlib.sha256(FINAL.read_bytes()).hexdigest(),
    'method':'Direct game-window capture requested at 60 Hz. No interpolation, speed change or generated frames.'}
(OUT/'video-validation.json').write_text(json.dumps(result,indent=2))
print(json.dumps(result,indent=2))
