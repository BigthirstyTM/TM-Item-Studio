"""Summarize only proven 86-frame fixture draws, keeping their raw buffers."""
import json
import struct
import sys
from pathlib import Path

root = Path(sys.argv[1])
rows = [json.loads(x) for x in (root / 'readback.jsonl').read_text().splitlines()]
assert rows[-1]['event'] == 'restored', 'Probe teardown is required'
candidates = {r['draw']: r for r in rows if r.get('event') == 'candidate'}
summary = []
for row in rows:
    if row.get('args', [0])[0] != 726 or row.get('cb', [0, 0, 0])[2] != 86:
        continue
    draw = row['draw']
    data = (root / f'draw-{draw:02d}-t0.bin').read_bytes()
    assert row['stride'] == 28 and len(data) == 144 * 86 * 28
    summary.append({'draw': draw, 'instance': row['cb'][0], 'stride': row['stride'],
                    'bytes': row['bytes'], 'iaVertexBuffer': candidates[draw]['vb'],
                    'minX': min(struct.unpack_from('<f', data, o)[0] for o in range(0, len(data), 28)),
                    'maxX': max(struct.unpack_from('<f', data, o)[0] for o in range(0, len(data), 28))})
(root / 'structured-summary.json').write_text(json.dumps(summary, indent=2))
print(json.dumps(summary, indent=2))
