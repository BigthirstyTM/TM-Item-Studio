"""Original low-poly gull geometry, +X forward / +Y up. No external assets."""
import math,json
from pathlib import Path
parts={name:dict(positions=[],normals=[],uvs=[],indices=[]) for name in ['body','left','right']}
def tri(part,a,b,c):
 d=parts[part];u=[b[i]-a[i] for i in range(3)];v=[c[i]-a[i] for i in range(3)];n=[u[1]*v[2]-u[2]*v[1],u[2]*v[0]-u[0]*v[2],u[0]*v[1]-u[1]*v[0]];l=math.sqrt(sum(x*x for x in n));n=[x/l for x in n]
 for p in [a,b,c]:d['indices'].append(len(d['positions'])//3);d['positions']+=p;d['normals']+=n;d['uvs'] += [p[0]*.2,p[2]*.2]
def ellipsoid(center,scale):
 # Closed latitude rings without degenerate polar triangles.
 rings=[]
 for i in range(1,6):
  t=math.pi*i/6;rings.append([[center[0]+scale[0]*math.cos(t),center[1]+scale[1]*math.sin(t)*math.cos(j*math.tau/10),center[2]+scale[2]*math.sin(t)*math.sin(j*math.tau/10)] for j in range(10)])
 for j in range(10):
  k=(j+1)%10;tri('body',[center[0]+scale[0],center[1],center[2]],rings[0][j],rings[0][k]);tri('body',[center[0]-scale[0],center[1],center[2]],rings[-1][k],rings[-1][j])
  for a,b in zip(rings,rings[1:]):tri('body',a[j],b[j],b[k]);tri('body',a[j],b[k],a[k])
def prism(part,outline,thickness):
 if sum(outline[i][0]*outline[(i+1)%len(outline)][1]-outline[(i+1)%len(outline)][0]*outline[i][1] for i in range(len(outline)))<0:outline=list(reversed(outline))
 top=[[x,thickness,z] for x,z in outline];bottom=[[x,-thickness,z] for x,z in outline]
 for i in range(1,len(top)-1):tri(part,top[0],top[i+1],top[i]);tri(part,bottom[0],bottom[i],bottom[i+1])
 for i in range(len(top)):
  j=(i+1)%len(top);tri(part,top[i],bottom[i],bottom[j]);tri(part,top[i],bottom[j],top[j])
ellipsoid([0,0,0],[1.5,.42,.48]);ellipsoid([1.2,.25,0],[.58,.38,.35]);prism('body',[(1.5,-.2),(2.25,0),(1.5,.2)],.12);prism('body',[(-.8,-.3),(-2.1,-.75),(-1.8,0),(-2.1,.75),(-.8,.3)],.07)
for name,sign in [('left',-1),('right',1)]:
 prism(name,[(.5,sign*.25),(.35,sign*1.9),(-.45,sign*3.8),(-1.1,sign*4.3),(-.9,sign*2.3),(-.8,sign*.3)],.055)
for part in parts.values():part['positions']=[v*.3 for v in part['positions']]
out=Path('artifacts/coaster-flight');out.mkdir(exist_ok=True);(out/'bird-mesh.json').write_text(json.dumps(parts),encoding='utf-8')
print({k:len(v['positions'])//3 for k,v in parts.items()})
