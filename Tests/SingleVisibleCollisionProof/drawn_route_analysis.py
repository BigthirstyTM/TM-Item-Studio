"""Research only: invert a planar drawn path into slider + offset rotation.

This does NOT emit GBX or establish native collision playback. The inverse
requires arbitrary scalar curves, which the tested native timeline cannot store.
Coordinates are local X/Z metres. Input: JSON array [[x,z], ...].
"""
import argparse
import json
import math


def analyze(points, radius=None, heading_degrees=0, subdivisions=16):
    if not 2 <= len(points) <= 1024:
        raise ValueError('Use 2 to 1024 points')
    if any(len(p) != 2 or not all(math.isfinite(v) and abs(v) <= 10000 for v in p) for p in points):
        raise ValueError('Expected finite [x,z] points within 10000 metres')
    if not math.isfinite(heading_degrees) or not 1 <= subdivisions <= 256:
        raise ValueError('Invalid heading or subdivision count')
    phi = math.radians(heading_degrees)
    u = (math.cos(phi), math.sin(phi))
    v = (-u[1], u[0])
    local = [(x*u[0]+z*u[1], x*v[0]+z*v[1]) for x,z in points]
    centre = (min(p[1] for p in local)+max(p[1] for p in local))/2
    half_width = (max(p[1] for p in local)-min(p[1] for p in local))/2
    # Stay away from acos singularities; this margin is numerical, NOT a
    # native speed/collision safety bound.
    radius = max(1.0, half_width*1.25) if radius is None else radius
    if not math.isfinite(radius) or radius <= half_width or radius <= 0:
        raise ValueError('Radius must exceed half the transverse route width')
    samples = []
    distance = 0.0
    for index, (a,b) in enumerate(zip(local,local[1:])):
        length = math.dist(a,b)
        if length < .01:
            raise ValueError('Consecutive points must be at least 1 cm apart')
        for j in range(subdivisions):
            q = j/subdivisions
            samples.append((distance+q*length,a[0]+q*(b[0]-a[0]),a[1]+q*(b[1]-a[1])))
        distance += length
    samples.append((distance,*local[-1]))
    signals = []
    errors = []
    for s,x,z in samples:
        # Mathematical convention: positive angle rotates +v towards +u.
        # Actual native sign/frame requires calibration before any exporter.
        theta = math.acos((z-centre)/radius)
        slide = x-radius*math.sin(theta)
        rx = slide+radius*math.sin(theta)
        rz = centre+radius*math.cos(theta)
        errors.append(math.hypot(rx-x,rz-z))
        signals.append(dict(distance=s,translation=slide,angleDegrees=math.degrees(theta)))

    def diagnostic(key):
        values = [s[key] for s in signals]
        lo,hi = min(values),max(values)
        eps = max(1e-7,(hi-lo)*1e-6)
        # Interior extrema obstruct a simple monotonic full-range easing key.
        # Not a lower-bound theorem: exotic Back/Elastic/Bounce easings exist.
        signs = []
        interior = 0
        last_index = 0
        for i in range(1,len(values)):
            delta = values[i]-values[i-1]
            sign = 1 if delta > eps else -1 if delta < -eps else 0
            if sign:
                if signs and sign != signs[-1]:
                    value = values[last_index]
                    if lo+eps < value < hi-eps:
                        interior += 1
                signs.append(sign)
                last_index = i
        turns = sum(a!=b for a,b in zip(signs,signs[1:]))
        return dict(min=lo,max=hi,sampledDirectionChanges=turns,
                    sampledInteriorExtrema=interior)

    return dict(status='geometric-inverse-only',nativeExportSupported=False,
                headingDegrees=heading_degrees,radius=radius,transverseCentre=centre,
                routeLength=distance,inputPointCount=len(points),sampleCount=len(samples),
                maxReconstructionError=max(errors),
                translation=diagnostic('translation'),rotation=diagnostic('angleDegrees'),
                limitations=[
                    'Arbitrary signal samples are not native SubAnimFunc keys.',
                    'No native interpolation, timing fit, sign/frame calibration or collision test is performed.',
                    'Rotation is determined by path position; tangent-facing orientation is not independently controllable.',
                    'Sharp drawn corners still require stopping or geometric rounding.',
                    'Direction changes/interior extrema are diagnostics, not a proof of native impossibility.'],
                signals=signals)


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('route', help='JSON file containing [[x,z], ...]')
    p.add_argument('--radius',type=float)
    p.add_argument('--heading',type=float,default=0)
    a = p.parse_args()
    with open(a.route,encoding='utf-8-sig') as f:
        result = analyze(json.load(f),a.radius,a.heading)
    print(json.dumps(result,indent=2))
