"""Reuse the proven 60 Hz capture; change only this fixture's views and scope."""
import json
from pathlib import Path
import game
import round_video_fixture as recorder

OUT=Path(__file__).resolve().parents[2]/'artifacts/flag-file-followup-20261022/native-package/video'
OUT.mkdir(exist_ok=False)
game.destination=OUT
plugins=game.call('ListPlugins')
assert not {'TMISNativeFlagPlacement','TMISFlagGpuPlacement','TMISFlagMaterialProbe','TMISVanillaFlagCopy'}&{p['id'] for p in plugins['plugins']}
(OUT/'recording-plugins.json').write_text(json.dumps(plugins,indent=2))
recorder.ROOT=OUT
recorder.gate=OUT/'unused-capture-marker'
recorder.OVERVIEW=(1483,24,1295,1483,19,1360,58)
recorder.VIEWS=[
    ('Long cloth - 2.5x',(1465,21,1343,1465,19.7,1360,50)),
    ('Swallowtail',(1482.5,21,1349,1482.5,19.7,1360,50)),
    ('Round cloth',(1502.5,21,1349,1502.5,19.7,1360,50)),
]
recorder.record()
