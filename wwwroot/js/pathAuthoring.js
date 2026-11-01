let bezierState={enabled:false,detail:8,linked:true,handles:[]},bezierGroup=null,bezierDrag=null,authoringCanvas=null;
let lookAtMarker=null,clearanceGroup=null,clearanceMargin=.2,clearanceSamples=64,clearanceBusy=false;
function authoringRemove(group){if(group){group.parent?.remove(group);disposeObjectResources(group);}}
function ensureBezierHandles(){
    if(bezierState.handles.length===drawPathPoints.length)return;
    bezierState.handles=drawPathPoints.map((p,i)=>{const before=drawPathPoints[drawPathRepeat==='Closed'?(i-1+drawPathPoints.length)%drawPathPoints.length:Math.max(0,i-1)],after=drawPathPoints[drawPathRepeat==='Closed'?(i+1)%drawPathPoints.length:Math.min(drawPathPoints.length-1,i+1)],v=after.clone().sub(before).multiplyScalar(1/6);return {in:{x:-v.x,y:-v.y,z:-v.z},out:{x:v.x,y:v.y,z:v.z}};});
}
window.getBezierState=()=>{ensureBezierHandles();return JSON.stringify(bezierState);};
window.setBezierState=function(state){
    if(!state||!Number.isInteger(state.detail)||state.detail<2||state.detail>16||!Array.isArray(state.handles)||state.handles.length>129)throw Error('Invalid Bezier settings.');
    for(const h of state.handles)for(const key of ['in','out'])if(!h[key]||!['x','y','z'].every(k=>Number.isFinite(h[key][k])&&Math.abs(h[key][k])<=10000))throw Error('Invalid Bezier handle.');
    bezierState=structuredClone(state);window.drawBezierHandles();
};
window.configureBezier=function(enabled,detail,linked){bezierState.enabled=enabled;bezierState.detail=detail;bezierState.linked=linked;ensureBezierHandles();window.drawBezierHandles();};
window.resetBezierHandles=function(){bezierState.handles=[];ensureBezierHandles();window.drawBezierHandles();};
window.drawBezierHandles=function(){
    authoringRemove(bezierGroup);bezierGroup=null;
    if(!bezierState.enabled||!scene)return;ensureBezierHandles();bezierGroup=new THREE.Group();scene.add(bezierGroup);
    drawPathPoints.forEach((p,i)=>{for(const key of ['in','out']){const h=bezierState.handles[i][key],q=p.clone().add(new THREE.Vector3(h.x,h.y,h.z));
        const line=new THREE.Line(new THREE.BufferGeometry().setFromPoints([p,q]),new THREE.LineBasicMaterial({color:0xa78bfa,depthTest:false}));bezierGroup.add(line);
        const marker=new THREE.Mesh(new THREE.SphereGeometry(.2,8,8),new THREE.MeshBasicMaterial({color:key==='in'?0xa78bfa:0x22d3ee,depthTest:false}));marker.position.copy(q);marker.renderOrder=1100;marker.userData.bezier={i,key};bezierGroup.add(marker);
    }});
    if(authoringCanvas!==renderer.domElement){authoringCanvas=renderer.domElement;
        authoringCanvas.addEventListener('pointerdown',e=>{if(e.button!==0||!bezierState.enabled||!bezierGroup)return;const r=authoringCanvas.getBoundingClientRect(),ray=new THREE.Raycaster();ray.setFromCamera(new THREE.Vector2((e.clientX-r.left)/r.width*2-1,1-(e.clientY-r.top)/r.height*2),camera);const hit=ray.intersectObjects(bezierGroup.children).find(h=>h.object.userData.bezier);if(!hit)return;e.preventDefault();e.stopImmediatePropagation();const normal=drawPathPlane==='XY'?new THREE.Vector3(0,0,1):drawPathPlane==='YZ'?new THREE.Vector3(1,0,0):new THREE.Vector3(0,1,0);bezierDrag={...hit.object.userData.bezier,plane:new THREE.Plane().setFromNormalAndCoplanarPoint(normal,hit.object.position)};controls.enabled=false;authoringCanvas.setPointerCapture(e.pointerId);},true);
        authoringCanvas.addEventListener('pointermove',e=>{if(!bezierDrag)return;const r=authoringCanvas.getBoundingClientRect(),ray=new THREE.Raycaster();ray.setFromCamera(new THREE.Vector2((e.clientX-r.left)/r.width*2-1,1-(e.clientY-r.top)/r.height*2),camera);const q=ray.ray.intersectPlane(bezierDrag.plane,new THREE.Vector3());if(!q||q.length()>10000)return;const {i,key}=bezierDrag,d=q.sub(drawPathPoints[i]);bezierState.handles[i][key]={x:d.x,y:d.y,z:d.z};if(bezierState.linked)bezierState.handles[i][key==='in'?'out':'in']={x:-d.x,y:-d.y,z:-d.z};window.drawBezierHandles();});
        const finish=()=>{if(!bezierDrag)return;bezierDrag=null;controls.enabled=true;dotNetHelper?.invokeMethodAsync('OnBezierChanged');};
        authoringCanvas.addEventListener('pointerup',finish);authoringCanvas.addEventListener('pointercancel',finish);
    }
};
window.setLookAtMarker=function(target){authoringRemove(lookAtMarker);lookAtMarker=null;if(!target||!scene)return;lookAtMarker=new THREE.Mesh(new THREE.SphereGeometry(.35,12,12),new THREE.MeshBasicMaterial({color:0xfacc15,wireframe:true,depthTest:false}));lookAtMarker.position.set(target.x,target.y,target.z);scene.add(lookAtMarker);};
window.getLookAtSelection=function(){const mesh=selectedMeshObject??routeReference?.children.find(m=>m.visible);if(!mesh)throw Error('Select a mesh or load a reference first.');const p=new THREE.Box3().setFromObject(mesh).getCenter(new THREE.Vector3());return {x:p.x,y:p.y,z:p.z};};
window.clearClearance=function(){authoringRemove(clearanceGroup);clearanceGroup=null;};
window.checkPathClearance=async function(margin,samples){
    if(clearanceBusy)throw Error('A clearance check is already running.');
    if(!drawPathDraft)throw Error('Create a route preview first.');
    if(!Number.isFinite(margin)||margin< -2||margin>10||!Number.isInteger(samples)||samples<16||samples>256)throw Error('Invalid clearance settings.');
    clearanceMargin=margin;clearanceSamples=samples;window.clearClearance();const draft=drawPathDraft,instances=draft.instances;
    const triangles=[],blocks=[];
    if(routeReference) {const mesh=routeMesh(),g=mesh.geometry;mesh.updateWorldMatrix(true,false);for(let i=0;i<g.index.count;i+=3){const tri=new THREE.Triangle(...[0,1,2].map(j=>new THREE.Vector3().fromBufferAttribute(g.attributes.position,g.index.getX(i+j)).applyMatrix4(mesh.matrixWorld)));triangles.push(tri);}
        for(let i=0;i<triangles.length;i+=64){const list=triangles.slice(i,i+64),box=new THREE.Box3();for(const t of list){box.expandByPoint(t.a);box.expandByPoint(t.b);box.expandByPoint(t.c);}blocks.push({box,list});}}
    if(!blocks.length&&instances.length<2)throw Error('Load a reference model or create at least two followers to check spacing.');
    if(blocks.length*samples*instances.length>20000000)throw Error('This check is too large. Reduce samples/copies or choose a simpler reference part.');
    const issues=[],visible=instances.map(i=>i.ghost.visible);clearanceBusy=true;instances.forEach(i=>i.ghost.visible=false);
    try{for(let s=0;s<samples;s++){
        if(drawPathDraft!==draft)throw Error('Route changed during the check. Run it again.');
        const time=s*draft.plan.durationMs/samples,boxes=instances.map(instance=>{applyPathInstance(instance,time);instance.ghost.updateWorldMatrix(true,true);return new THREE.Box3().setFromObject(instance.ghost);});
        boxes.forEach((box,i)=>{
            const expanded=box.clone().expandByScalar(margin);
            if(!expanded.isEmpty()&&blocks.some(b=>b.box.intersectsBox(expanded)&&b.list.some(t=>expanded.intersectsTriangle(t))))issues.push({time,copy:i+1,other:'reference',position:box.getCenter(new THREE.Vector3())});
            for(let j=0;j<i;j++){const a=box.clone().expandByScalar(margin/2),b=boxes[j].clone().expandByScalar(margin/2);if(!a.isEmpty()&&!b.isEmpty()&&a.intersectsBox(b))issues.push({time,copy:i+1,other:`copy ${j+1}`,position:box.getCenter(new THREE.Vector3())});}
        });if(s%8===0)await new Promise(resolve=>setTimeout(resolve,0));
    }}finally{clearanceBusy=false;instances.forEach((instance,i)=>instance.ghost.visible=visible[i]);applyDrawPathPreview(performance.now());}
    clearanceGroup=new THREE.Group();scene.add(clearanceGroup);for(const issue of issues.slice(0,256)){const marker=new THREE.Mesh(new THREE.SphereGeometry(.3,8,8),new THREE.MeshBasicMaterial({color:0xef4444,depthTest:false}));marker.position.copy(issue.position);clearanceGroup.add(marker);}
    return {count:issues.length,samples,issues:issues.slice(0,100).map(i=>({time:Math.round(i.time),copy:i.copy,other:i.other}))};
};
window.getPathAuthoringState=function(){return JSON.stringify({bezier:JSON.parse(window.getBezierState()),reference:routeReferenceParts,referencePart:routeReference?.children.find(m=>m.visible)?.name??null,referenceOffset:routeReferenceOffset.toArray(),surfacePicking:window.routeSurfacePicking,drawing:{enabled:drawPathEnabled,plane:drawPathPlane,offset:drawPathPlaneY,view:drawPathViewLock??"free"},camera:{position:camera.position.toArray(),target:controls.target.toArray()},playback:window.getPathPlaybackState(),clearanceMargin,clearanceSamples});};
window.restorePathAuthoringState=function(state){
    window.setBezierState(state.bezier??{enabled:false,detail:8,linked:true,handles:[]});
    if(state.reference?.length){window.loadRouteReference(state.reference);window.configureRouteReference(state.referencePart,!!state.surfacePicking,...state.referenceOffset);}
    clearanceMargin=state.clearanceMargin??.2;clearanceSamples=state.clearanceSamples??64;
};
window.disposePathAuthoring=function(){authoringRemove(bezierGroup);bezierGroup=null;authoringRemove(lookAtMarker);lookAtMarker=null;window.clearClearance();bezierState={enabled:false,detail:8,linked:true,handles:[]};authoringCanvas=null;bezierDrag=null;};

window.restoreBezierJson=function(json){const s=JSON.parse(json),v=p=>({x:p.X,y:p.Y,z:p.Z});window.setBezierState({enabled:s.Enabled,detail:s.Detail,linked:s.Linked,handles:(s.Handles??[]).map(h=>({in:v(h.In),out:v(h.Out)}))});};

window.validatePathAuthoringState=function(s){
 try{
  if(!s||!s.bezier||typeof s.bezier.enabled!=="boolean"||typeof s.bezier.linked!=="boolean"||typeof s.surfacePicking!=="boolean"||!Number.isInteger(s.bezier.detail)||s.bezier.detail<2||s.bezier.detail>16||!Array.isArray(s.bezier.handles)||s.bezier.handles.length>129)throw Error('Invalid project handles.');
  for(const h of s.bezier.handles)for(const k of ['in','out'])if(!h[k]||!['x','y','z'].every(a=>Number.isFinite(h[k][a])&&Math.abs(h[k][a])<=10000))throw Error('Invalid project handle coordinates.');
  if(!Array.isArray(s.reference)||s.reference.length>2000||!Array.isArray(s.referenceOffset)||s.referenceOffset.length!==3||!s.referenceOffset.every(n=>Number.isFinite(n)&&Math.abs(n)<=10000))throw Error('Invalid project reference.');
  let size=0;for(const p of s.reference){if(typeof p.name!=='string'||!Array.isArray(p.positions)||!Array.isArray(p.indices)||p.positions.length%3||p.indices.length%3||p.positions.length>300000||p.indices.length>600000||!p.positions.every(Number.isFinite)||!p.indices.every(i=>Number.isInteger(i)&&i>=0&&i<p.positions.length/3))throw Error('Invalid project reference geometry.');size+=p.positions.length;}
  if(size>1500000||s.reference.length&&!s.reference.some(p=>p.name===s.referencePart))throw Error('Invalid project reference selection.');
  if(!Number.isFinite(s.clearanceMargin)||s.clearanceMargin< -2||s.clearanceMargin>10||!Number.isInteger(s.clearanceSamples)||s.clearanceSamples<16||s.clearanceSamples>256)throw Error('Invalid clearance settings.');
  if(s.drawing&&(typeof s.drawing.enabled!=='boolean'||!['XZ','XY','YZ'].includes(s.drawing.plane)||!['free','top','front','right'].includes(s.drawing.view)||!Number.isFinite(s.drawing.offset)||Math.abs(s.drawing.offset)>10000))throw Error('Invalid project drawing plane.');
  if(s.camera&&![s.camera.position,s.camera.target].every(p=>Array.isArray(p)&&p.length===3&&p.every(n=>Number.isFinite(n)&&Math.abs(n)<1000000)))throw Error('Invalid project camera.');
  if(s.playback&&(!Number.isFinite(s.playback.time)||s.playback.time<0||typeof s.playback.paused!=='boolean'))throw Error('Invalid playback position.');
  return null;
 }catch(e){return e.message;}
};
window.getRouteReferenceUi=function(){return {parts:routeReferenceParts.map(p=>p.name),part:routeReference?.children.find(m=>m.visible)?.name??'',x:routeReferenceOffset.x,y:routeReferenceOffset.y,z:routeReferenceOffset.z,drawing:window.routeSurfacePicking};};
window.configureClearance=function(margin,samples){clearanceMargin=margin;clearanceSamples=samples;};
window.getClearanceSettings=function(){return {margin:clearanceMargin,samples:clearanceSamples};};

window.getPathPlaybackState=function(){return drawPathDraft?{paused:!!drawPathDraft.paused,time:Math.round((drawPathDraft.paused?drawPathDraft.seekMs:performance.now()-drawPathDraft.started)%drawPathDraft.plan.durationMs)}:null;};
window.restoreProjectView=function(s){
 if(s.camera){camera.position.fromArray(s.camera.position);controls.target.fromArray(s.camera.target);controls.update();}
 if(s.playback&&drawPathDraft){window.seekDrawPathPreview(s.playback.time);window.pauseDrawPathPreview(s.playback.paused);}
};
