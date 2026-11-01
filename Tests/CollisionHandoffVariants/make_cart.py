import bpy,math,json
from mathutils import Vector
from pathlib import Path
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
mats=[]
for name,color in [('Body red',(0.65,.025,.035,1)),('Seats and tyres',(.025,.035,.045,1)),('Restraints and hubs',(.48,.55,.62,1))]:
 m=bpy.data.materials.new(name);m.diffuse_color=color;mats.append(m)
def box(name,pos,size,mat):
 bpy.ops.mesh.primitive_cube_add(size=1,location=pos);o=bpy.context.object;o.name=name;o.dimensions=size;bpy.ops.object.transform_apply(location=False,rotation=False,scale=True);o.data.materials.append(mats[mat]);return o
def rod(name,a,b,r,mat):
 d=Vector(b)-Vector(a);bpy.ops.mesh.primitive_cylinder_add(vertices=12,radius=r,depth=d.length,location=(Vector(a)+Vector(b))/2);o=bpy.context.object;o.name=name;o.rotation_euler=d.to_track_quat('Z','Y').to_euler();o.data.materials.append(mats[mat]);return o
box('Chassis',(0,0,.8),(6.5,7.4,.5),0)
box('Front nose',(2.9,0,1.25),(.7,7.4,.9),0)
box('Rear',( -3,0,1.4),(.3,7.4,1.3),0)
for y in [-3.65,3.65]:box('Side wall',(0,y,1.35),(6,.25,1.25),0)
for x in [-1.7,.65]:
 for y in [-1.65,1.65]:
  box('Seat cushion',(x,y,1.45),(1.5,1.7,.35),1)
  box('Seat back',(x-.65,y,2.15),(.25,1.7,1.6),1)
  for side in [-.68,.68]:rod('Lap bar upright',(x+.65,y+side,1.5),(x+.65,y+side,2.15),.09,2)
  rod('Safety lap bar',(x+.65,y-.68,2.15),(x+.65,y+.68,2.15),.11,2)
for x in [-2.2,2.2]:
 for y in [-3.9,3.9]:
  rod('Wheel',(x,y-.24,.7),(x,y+.24,.7),.7,1)
  rod('Wheel hub',(x,y-.26,.7),(x,y+.26,.7),.32,2)
meshes=[dict(positions=[],normals=[],uvs=[],indices=[]) for _ in mats]
for o in list(bpy.context.scene.objects):
 if o.type!='MESH':continue
 m=o.data;m.calc_loop_triangles();target=meshes[mats.index(m.materials[0])]
 for t in m.loop_triangles:
  for vi in t.vertices:
   p=o.matrix_world@m.vertices[vi].co;n=(o.matrix_world.to_3x3()@t.normal).normalized();target['indices'].append(len(target['positions'])//3);target['positions']+=list((p.x,p.z,-p.y));target['normals']+=list((n.x,n.z,-n.y));target['uvs'] += [p.x*.15,p.y*.15]
out=Path('artifacts/handoff-cart');(out/'cart-mesh.json').write_text(json.dumps(meshes))
bpy.ops.wm.save_as_mainfile(filepath=str((out/'TMIS_CoasterCart.blend').resolve()))
