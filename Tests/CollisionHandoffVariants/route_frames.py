"""Research-only frames from the known NewCoaster deck strip; not a general mesh extractor.
Usage: python route_frames.py geometry.json route128.json output.json
Requires numpy. Source route points sit 4.2 m above the deck strip (cube clearance).
"""
import sys,json,collections
import numpy as np
from pathlib import Path
g=json.loads(Path(sys.argv[1]).read_text(encoding='utf-8'))[2]
p=np.array(g['positions']).reshape(-1,3)
u,inv=np.unique(np.round(p,3),axis=0,return_inverse=True)
t=inv[np.array(g['indices']).reshape(-1,3)]
edges=collections.defaultdict(list)
for i,tri in enumerate(t):
 for a,b in zip(tri,np.roll(tri,1)):edges[tuple(sorted((int(a),int(b))))].append(i)
adj=collections.defaultdict(list)
for e,ts in edges.items():
 if len(ts)==2:
  a,b=ts;adj[a].append(b);adj[b].append(a)
seen=set();components=[]
for a in range(len(t)):
 if a in seen:continue
 stack=[a];c=[]
 while stack:
  b=stack.pop()
  if b in seen:continue
  seen.add(b);c.append(b);stack.extend(adj[b])
 components.append(c)
c=max(components,key=len);start=next((i for i in c if len(adj[i])==1),c[0]);seq=[start];seen={start}
while True:
 ns=[x for x in adj[seq[-1]] if x not in seen]
 if not ns:break
 seq.append(ns[0]);seen.add(ns[0])
centres=[];sides=[]
for a,b in zip(seq,seq[1:]):
 v=u[list(set(t[a])&set(t[b]))]
 if len(v)==2 and 7.75<np.linalg.norm(v[0]-v[1])<7.95:
  centres.append(v.mean(0));sides.append(v[1]-v[0])
centres=np.array(centres);sides=np.array(sides)
points=np.array(json.loads(Path(sys.argv[2]).read_text(encoding='utf-8')))
frames=[]
for i,point in enumerate(points):
 forward=points[(i+1)%len(points)]-point;forward/=np.linalg.norm(forward)
 deck=point-np.array([0,4.2,0]);j=np.argmin(np.linalg.norm(centres-deck,axis=1))
 side=sides[j]-forward*np.dot(sides[j],forward);side/=np.linalg.norm(side)
 up=np.cross(side,forward)
 if up[1]<0:side=-side;up=-up
 reference=np.cross(forward,np.array([0.,1.,0.]));reference/=np.linalg.norm(reference)
 # Native frame uses local X forward, local Y up and local Z across.
 if np.dot(side,reference)<0:side=-side
 up=np.cross(side,forward)
 bank=float(np.degrees(np.arctan2(np.dot(np.cross(reference,side),forward),np.dot(reference,side))))
 frames.append(dict(point=point.tolist(),forward=forward.tolist(),up=up.tolist(),side=side.tolist(),pitchDegrees=float(np.degrees(np.arcsin(forward[1]))),bankDegrees=bank,nearestCrossSectionDistance=float(np.linalg.norm(centres[j]-deck))))
Path(sys.argv[3]).write_text(json.dumps(dict(researchOnly=True,sourceGeometry=sys.argv[1],deckOffset=4.2,frames=frames),indent=2),encoding='utf-8')
print('Frames',len(frames),'pitch range',min(f['pitchDegrees'] for f in frames),max(f['pitchDegrees'] for f in frames),'max abs bank',max(abs(f['bankDegrees']) for f in frames),'max sample distance',max(f['nearestCrossSectionDistance'] for f in frames))
