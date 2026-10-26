"""Bounded native-window recording. No frame generation or interpolation."""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys
import time

import game

variant = sys.argv[2] if len(sys.argv) > 2 else 'kinematic'
assert variant in {'kinematic', 'local-motion'}
OUT = Path(__file__).resolve().parents[2] / 'artifacts/flag-motion-embedded-20261022' / variant / (sys.argv[1] if len(sys.argv) > 1 else 'video')


def camera(x, y, z, tx, ty, tz, fov=48):
    game.call('ControlPlayCamera', {'action': 'set_position', 'x': x, 'y': y, 'z': z,
        'target': {'x': tx, 'y': ty, 'z': tz}, 'fov': fov})


def main():
    OUT.mkdir(exist_ok=False)
    game.destination = OUT
    assert game.call('GetMode')['mode'] == 'MapEditor_Playground'
    assert game.call('GetItems', {'limit': 10})['total'] == 2
    plugins = game.call('ListPlugins')
    assert not {'TMISFlagGpuPlacement', 'TMISFlagMaterialProbe', 'TMISNativeFlagPlacement',
                'TMISVanillaFlagCopy'} & {p['id'] for p in plugins['plugins']}
    (OUT / 'plugins.json').write_text(json.dumps(plugins, indent=2))
    camera(768, 149, 391, 768, 141, 450, 55)
    raw = OUT / 'kinematic-waving-flags-raw.mp4'
    with (OUT / 'capture.log').open('w') as log:
        proc = subprocess.Popen([shutil.which('ffmpeg'), '-hide_banner', '-n', '-f', 'gdigrab',
            '-framerate', '60', '-draw_mouse', '0', '-i', 'title=Trackmania', '-t', '32',
            '-vf', 'scale=1920:1080', '-c:v', 'libx264', '-preset', 'ultrafast', '-crf', '17',
            '-pix_fmt', 'yuv420p', '-fps_mode', 'vfr', str(raw)], stdin=subprocess.PIPE, stdout=log, stderr=log)
        started = time.monotonic()
        timeline = [{'seconds': 0, 'view': 'overview'}]
        try:
            time.sleep(5)
            camera(767, 147, 425, 748, 141, 450)
            timeline.append({'seconds': time.monotonic()-started, 'view': 'stationary control'})
            time.sleep(5)
            camera(807, 147, 425, 788, 141, 450)
            timeline.append({'seconds': time.monotonic()-started, 'view': 'orbit and waving cloth'})
            time.sleep(21)
        finally:
            proc.communicate(b'q', timeout=15)
            (OUT / 'timeline.json').write_text(json.dumps(timeline, indent=2))
        assert proc.returncode == 0
    metadata = json.loads(subprocess.check_output([shutil.which('ffprobe'), '-v', 'error',
        '-show_entries', 'stream=width,height,avg_frame_rate,nb_frames,duration',
        '-show_entries', 'format=duration,size', '-of', 'json', str(raw)]))
    (OUT / 'raw-metadata.json').write_text(json.dumps(metadata, indent=2))
    print(json.dumps({'video': str(raw), 'metadata': metadata, 'sha256': hashlib.sha256(raw.read_bytes()).hexdigest()}))


if __name__ == '__main__':
    main()
