"""Checks delivered schedules against geometric intent, independently of GBX serialization."""
import json,math
from pathlib import Path
import numpy as np
root=Path('artifacts/coaster-flight');j=json.loads((root/'cart-timing.json').read_text());p=np.array(j['points']);length=np.linalg.norm(np.roll(p,-1,axis=0)-p,axis=1);ms=np.array([x['moveMs'] for x in j['legs']]);speed=length/(ms/1000)
assert abs(length.sum()/(ms.sum()/1000)-10)<.002
assert min(ms)>=80
assert j['totalMs']==ms.sum()+j['delayMs']
assert all(j['legs'][i]['startMs']==j['delayMs']+int(ms[:i].sum()) for i in range(len(p)))
h=(p[:,1]+np.roll(p[:,1],-1))/2
assert np.corrcoef(h,speed)[0,1]<-.95,'Speed should decrease with altitude'
forward=np.roll(p,-1,axis=0)-p;forward/=np.linalg.norm(forward,axis=1)[:,None];want=forward+np.roll(forward,1,axis=0);want/=np.linalg.norm(want,axis=1)[:,None]
yaw=np.radians(j['yaw'][:-1]);pitch=np.radians(j['pitch'][:-1]);actual=np.stack([np.cos(yaw)*np.cos(pitch),np.sin(pitch),-np.sin(yaw)*np.cos(pitch)],axis=1)
assert np.max(np.linalg.norm(want-actual,axis=1))<1e-5
report=dict(averageSpeed=float(length.sum()/(ms.sum()/1000)),minSpeed=float(min(speed)),maxSpeed=float(max(speed)),cycleSeconds=j['totalMs']/1000,cartSegments=len(p),headingMaxError=float(np.max(np.linalg.norm(want-actual,axis=1))),birds=[])
deck=p+np.array([400,15,400])
for bird in json.loads((root/'bird-routes.json').read_text()):
 points=np.array(bird['points']);cross=[]
 for a,b in zip(points,np.roll(points,-1,axis=0)):
  for t in np.linspace(0,1,11):
   v=a*(1-t)+b*t
   for q,r in zip(deck,np.roll(deck,-1,axis=0)):
    d=(r-q)[[0,2]];u=np.clip(np.dot((v-q)[[0,2]],d)/np.dot(d,d),0,1);near=q+u*(r-q)
    if np.linalg.norm((v-near)[[0,2]])<4:cross.append(float(v[1]-near[1]))
 assert min(cross)<-8 and max(cross)>8,'Bird must pass both below and above track'
 assert min(abs(x) for x in cross)>4.5,'Keep clearance from the deck at crossings'
 assert min(points[:,1])>12,'Keep wing envelope above terrain'
 report['birds'].append(dict(bird=bird['bird'],minDeckDelta=min(cross),maxDeckDelta=max(cross),minAbsoluteDeckDelta=min(abs(x) for x in cross),wingPeriodMs=bird['wingPeriodMs']))
(root/'schedule-checks.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(json.dumps(report))
