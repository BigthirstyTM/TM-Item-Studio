"""Fixture telemetry diagnostics; no automatic native success classification."""
import argparse
import json
import math
from pathlib import Path


def centre_at(schedule, seconds):
    ms = seconds*1000 % schedule['cycleMs']
    p = list(schedule.get('placement', [400.,15.,400.]))
    p[1] += 4  # cube top centre, not coaster rail height
    for leg in schedule['legs']:
        fraction = min(1.,max(0.,(ms-leg['startMs'])/leg['moveMs']))
        for i in range(3):
            p[i] += fraction*leg['delta'][i]
    return p


def analyze(samples, schedule):
    # Fit startup clock offset only to the early departure. It is an estimated
    # alignment, not a measurement of native animation phase.
    initial = [s for s in samples if s['t'] < 9]
    if not initial:
        raise ValueError('No early telemetry')
    phase = min((i/100 for i in range(801)),
                key=lambda phase: sum(math.dist(s['position'],centre_at(schedule,s['t']+phase))**2 for s in initial))
    error = [math.dist(s['position'],centre_at(schedule,s['t']+phase)) for s in samples]
    off = next((s['t'] for s,e in zip(samples,error) if e>8),None)
    return dict(samples=len(samples),durationSeconds=samples[-1]['t']-samples[0]['t'],
        yRange=[min(s['position'][1] for s in samples),max(s['position'][1] for s in samples)],
        maxUpwardVelocity=max(s['velocity'][1] for s in samples),
        allWheelsAirSamples=sum(all(w['falling']=='FallingAir' for w in s['wheels']) for s in samples),
        minimumUpY=min(s['up'][1] for s in samples),
        zeroInputs=all(s['gas']==s['steer']==s['brake']==0 for s in samples),
        estimatedClockOffsetSeconds=phase,
        firstDistanceOver8mSeconds=off,maxDistanceToScheduledCubeTop=max(error),
        clockFitRmsMetres=math.sqrt(sum(math.dist(s['position'],centre_at(schedule,s['t']+phase))**2 for s in initial)/len(initial)),
        note='Distance is a diagnostic against the scheduled cube top; phase is fitted, not native telemetry. Inspect video. Rails also have collision.')


if __name__=='__main__':
    p=argparse.ArgumentParser()
    p.add_argument('samples',type=Path)
    p.add_argument('schedule',type=Path)
    a=p.parse_args()
    print(json.dumps(analyze(json.loads(a.samples.read_text()),json.loads(a.schedule.read_text())),indent=2))
