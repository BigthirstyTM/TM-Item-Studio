"""Observe the saved three-flag map and record a bounded startup-companion proof.

Does not place or edit models. Requires the fixed, opt-in DEV startup probe.
"""
import json
import shutil
import subprocess
import sys
import time
import round_video_fixture as f


def open_map(label):
    assert f.call('GetMode')['mode'] == 'Menu'
    fresh = f.log.read_text(errors='replace')
    assert '[TMIS-GPU-FLAG] STARTUP' in fresh
    assert fresh.count('[TMIS-GPU-FLAG] PREPARED') == 3
    assert '[TMIS-GPU-FLAG] FAIL' not in fresh
    f.call('OpenMapInEditor', {'path': 'My Maps/TMIS_RoundFlags_Video_20261021.Map.Gbx'})
    for _ in range(50):
        if f.call('GetMode')['mode'] == 'MapEditor':
            break
        time.sleep(.3)
    else:
        raise RuntimeError('Editor unavailable')
    items = f.call('GetItems', {'limit': 10})
    assert items['total'] == 3
    (f.ROOT / 'persistence' / (label + '-map.json')).write_text(json.dumps(items, indent=2))
    f.call('ControlOverlay', {'action': 'hide'})
    f.call('ControlValidation', {'action': 'requestEnterPlayground'})
    for _ in range(40):
        if f.call('GetMode')['mode'] == 'MapEditor_Playground':
            break
        time.sleep(.25)
    else:
        raise RuntimeError('Playground unavailable')
    f.camera(f.OVERVIEW)
    time.sleep(.4)
    print(f.capture.shot('persistence/' + label + '-overview'), flush=True)
    for shape, p in f.VIEWS:
        f.camera(p)
        time.sleep(.3)
        print(f.capture.shot('persistence/' + label + '-' + shape.split()[0].lower()), flush=True)
    fresh = f.log.read_text(errors='replace')
    assert 'SAVED MAP visuals=15 enabled=15 identical-to-preload=15' in fresh
    (f.ROOT / 'persistence' / (label + '-Openplanet.log')).write_bytes(f.log.read_bytes())


def record():
    assert f.call('GetMode')['mode'] == 'MapEditor_Playground'
    assert f.call('GetItems', {'limit': 10})['total'] == 3
    target = f.ROOT / 'persistence/custom-flags-after-restart.mp4'
    assert not target.exists(), 'Preserve existing evidence'
    f.camera(f.OVERVIEW)
    started = time.monotonic()
    timeline = []
    with target.with_suffix('.capture.log').open('w') as output:
        proc = subprocess.Popen([shutil.which('ffmpeg'), '-hide_banner', '-n',
            '-f', 'gdigrab', '-framerate', '60', '-draw_mouse', '0', '-i', 'title=Trackmania',
            '-t', '24', '-vf', "scale=1920:1080,drawtext=fontfile='C\\:/Windows/Fonts/arial.ttf':"
            "text='AFTER FULL RESTART - STARTUP COMPANION REQUIRED':fontsize=28:fontcolor=white:"
            "box=1:boxcolor=black@0.7:boxborderw=10:x=30:y=42",
            '-c:v', 'libx264', '-preset', 'ultrafast', '-crf', '19', '-pix_fmt', 'yuv420p',
            '-fps_mode', 'vfr', str(target)], stdin=subprocess.PIPE, stdout=output, stderr=output)
        try:
            time.sleep(4)
            for label, p in f.VIEWS:
                f.camera(p)
                timeline.append({'seconds': time.monotonic() - started, 'view': label})
                time.sleep(4)
            f.camera(f.OVERVIEW)
            time.sleep(3)
        finally:
            proc.communicate(b'q', timeout=15)
    assert proc.returncode == 0
    probe = shutil.which('ffprobe')
    result = subprocess.run([probe, '-v', 'error', '-show_entries',
        'stream=width,height,nb_frames,avg_frame_rate,r_frame_rate,duration:format=duration,size',
        '-of', 'json', str(target)], capture_output=True, text=True, check=True)
    metadata = json.loads(result.stdout)
    metadata['timeline'] = timeline
    metadata['capture'] = '60 Hz requested; VFR direct game-window capture; no synthetic frames'
    target.with_suffix('.json').write_text(json.dumps(metadata, indent=2))
    print(json.dumps(metadata, indent=2), flush=True)


if __name__ == '__main__':
    if sys.argv[1] == 'open':
        open_map(sys.argv[2])
    elif sys.argv[1] == 'record':
        record()
