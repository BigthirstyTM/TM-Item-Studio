"""Summarize native samples without promoting contact telemetry to a pass."""
import argparse
import json
from pathlib import Path

p = argparse.ArgumentParser()
p.add_argument('directory', type=Path)
a = p.parse_args()
summary = {}
for file in sorted(a.directory.glob('*-samples.json')):
    samples = json.loads(file.read_text(encoding='utf-8'))
    if not samples:
        continue
    # Earlier runs did not copy attitude/brake into compact samples. Read the
    # full captured responses rather than treating absent fields as zero.
    calls = file.with_name(file.name.replace('-samples.json', '-calls.jsonl'))
    states = []
    if calls.exists():
        for line in calls.read_text(encoding='utf-8').splitlines():
            call = json.loads(line)
            if call['tool'] == 'GetVehicleState':
                state = call['response'].get('data', {}).get('result', {}).get('output', {}).get('state')
                if state:
                    states.append(state)
    summary[file.stem.removesuffix('-samples')] = dict(
        samples=len(samples), durationSeconds=samples[-1]['t']-samples[0]['t'],
        positionRanges=[[min(s['position'][i] for s in samples), max(s['position'][i] for s in samples)] for i in range(3)],
        maxVerticalSpeed=max(abs(s['velocity'][1]) for s in samples),
        allWheelsAirSamples=sum(all(w['falling']=='FallingAir' for w in s['wheels']) for s in samples),
        minimumUpY=min((s['up'][1] for s in states), default=None),
        zeroInputs=(all(s['inputGasPedal']==s['inputBrakePedal']==s['inputSteer']==0 for s in states)
                    if len(states)==len(samples) else None),
        firstPosition=samples[0]['position'], lastPosition=samples[-1]['position'])
print(json.dumps(summary, indent=2))
