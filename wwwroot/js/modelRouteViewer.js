let routeReference = null, routeReferenceParts = [], routeReferenceOffset = new THREE.Vector3();
window.routeSurfacePicking = false;
window.clearRouteReference = function() {
    if(routeReference){routeReference.parent?.remove(routeReference);disposeObjectResources(routeReference);}
    routeReference=null;routeReferenceParts=[];window.routeSurfacePicking=false;
};
window.loadRouteReference = function(parts) {
    if(!scene)throw Error('Open an item first.');
    if(!parts?.length)throw Error('No decoded visual geometry in this reference.');
    const staged=new THREE.Group();
    try { for(const part of parts) {
        if(part.positions.length>300000||part.indices.length>600000)throw Error('A reference part exceeds 100,000 vertices / 200,000 triangles. Simplify that part first.');
        const geometry=new THREE.BufferGeometry();geometry.setAttribute('position',new THREE.Float32BufferAttribute(part.positions,3));geometry.setIndex(part.indices);geometry.computeVertexNormals();
        const mesh=new THREE.Mesh(geometry,new THREE.MeshBasicMaterial({color:0x60a5fa,transparent:true,opacity:.4,side:THREE.DoubleSide,depthWrite:false}));
        mesh.name=part.name;staged.add(mesh);
    }}catch(e){disposeObjectResources(staged);throw e;}
    window.clearRouteReference();routeReference=staged;routeReferenceParts=parts;scene.add(staged);routeReferenceOffset.set(0,0,0);
    window.configureRouteReference(parts[0].name,false,0,0,0);return parts.map(p=>p.name);
};
window.captureRouteReference = function() {
    if(!selectedMeshObject)throw Error('Select a mesh part in the viewport first.');
    const mesh=selectedMeshObject;mesh.updateWorldMatrix(true,false);const attr=mesh.geometry.getAttribute('position'),positions=[];
    for(let i=0;i<attr.count;i++){const p=new THREE.Vector3().fromBufferAttribute(attr,i).applyMatrix4(mesh.matrixWorld);positions.push(p.x,p.y,p.z);}
    return window.loadRouteReference([{name:mesh.userData.path??mesh.name??'Selected mesh',positions,indices:mesh.geometry.index?Array.from(mesh.geometry.index.array):Array.from({length:attr.count},(_,i)=>i)}]);
};
window.configureRouteReference = function(name,pick,x,y,z) {
    if(!routeReference)return;
    if(![x,y,z].every(Number.isFinite))throw Error('Reference offset must be finite.');
    routeReferenceOffset.set(x,y,z);routeReference.position.copy(routeReferenceOffset);
    routeReference.children.forEach(m=>m.visible=m.name===name);routeReference.updateWorldMatrix(true,true);window.routeSurfacePicking=!!pick;
};
function routeMesh(){const mesh=routeReference?.children.find(m=>m.visible);if(!mesh)throw Error('Load and select a reference part first.');return mesh;}
window.fitRouteReference=function(){frameCamera(new THREE.Box3().setFromObject(routeMesh()));};
window.pickRouteSurface=function(raycaster){
    if(!routeReference)return null;
    const hit=raycaster.intersectObject(routeMesh(),false)[0];return hit?.point??null;
};
function nearestRouteSurface(point) {
    const mesh=routeMesh(),positions=mesh.geometry.getAttribute('position'),index=mesh.geometry.index;
    const local=mesh.worldToLocal(point.clone()),tri=new THREE.Triangle(),candidate=new THREE.Vector3();let nearest=null,best=Infinity;
    for(let i=0;i<index.count;i+=3){tri.a.fromBufferAttribute(positions,index.getX(i));tri.b.fromBufferAttribute(positions,index.getX(i+1));tri.c.fromBufferAttribute(positions,index.getX(i+2));tri.closestPointToPoint(local,candidate);const d=candidate.distanceToSquared(local);if(d<best){best=d;nearest=candidate.clone();}}
    if(!nearest)throw Error('Empty reference surface.');return mesh.localToWorld(nearest);
}
window.projectRouteOnModel=function(maxDistance){
    if(!Number.isFinite(maxDistance)||maxDistance<=0)throw Error('Set a positive snap distance.');
    const result=drawPathPoints.map(p=>{const q=nearestRouteSurface(p);if(q.distanceTo(p)>maxDistance)throw Error('A route point is outside the snap distance. Move it closer or increase the distance.');return q;});
    if(result.length<2)throw Error('Draw at least two guide points first.');
    window.setDrawnPathPoints(result);return result.length;
};
window.generateModelRoute=function(tolerance,closed){
    if(!Number.isFinite(tolerance)||tolerance<0||tolerance>10)throw Error('Route tolerance must be 0–10 m.');
    if(drawPathPoints.length<2)throw Error('Draw at least two guide points on the reference: start, end and optional intermediate stops.');
    const mesh=routeMesh(),part=routeReferenceParts.find(p=>p.name===mesh.name),graph=ModelRoute.create(part.positions,part.indices);
    const anchors=drawPathPoints.map(p=>mesh.worldToLocal(nearestRouteSurface(p))),route=[];
    if(closed)anchors.push(anchors[0]);
    for(let i=1;i<anchors.length;i++){
        const segment=ModelRoute.simplify(graph.between(anchors[i-1].toArray(),anchors[i].toArray()),tolerance);
        route.push(...(route.length?segment.slice(1):segment));
    }
    if(closed&&route.length>1)route.pop();
    if(route.length<2)throw Error('Guide points map to the same mesh vertex. Place them farther apart.');
    if(route.length>(closed?128:129))throw Error(`Found ${route.length} route points. Increase tolerance or use fewer guide points to fit the 128-segment limit.`);
    const points=route.map(p=>mesh.localToWorld(new THREE.Vector3(...p)));
    window.setDrawnPathPoints(points);return points.length;
};
