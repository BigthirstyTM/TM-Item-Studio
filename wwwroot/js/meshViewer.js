let scene, camera, renderer, controls, transformControls, gridHelper;
let staticGroup, movingGroup, pivotsGroup, lightsGroup, socketsGroup;
let dotNetHelper = null;
let selectedGizmo = null;

let isWireframe = false;
let isPlaying = true;
let animSpeed = 1.0;
let animAxis = 'y';
let isOscillating = false;
let minAngle = 0;
let maxAngle = 0;
let animTime = 0;

let showMeshes = true;
let showPivots = true;
let showLights = true;
let showSockets = true;

window.init3DViewer = function (containerId, dotNetRef) {
    dotNetHelper = dotNetRef;
    const container = document.getElementById(containerId);
    if (!container) return;

    container.innerHTML = '';

    const width = container.clientWidth || 800;
    const height = container.clientHeight || 550;

    scene = new THREE.Scene();
    scene.background = new THREE.Color(0x131316);

    camera = new THREE.PerspectiveCamera(45, width / height, 0.1, 2000);
    camera.position.set(12, 10, 12);

    renderer = new THREE.WebGLRenderer({ antialias: true });
    renderer.setSize(width, height);
    renderer.shadowMap.enabled = true;
    container.appendChild(renderer.domElement);

    controls = new THREE.OrbitControls(camera, renderer.domElement);
    controls.enableDamping = true;
    controls.dampingFactor = 0.05;

    // TransformControls voor interactief verplaatsen in 3D
    transformControls = new THREE.TransformControls(camera, renderer.domElement);
    transformControls.size = 0.75;
    scene.add(transformControls);

    transformControls.addEventListener('dragging-changed', function (event) {
        controls.enabled = !event.value;
    });

    transformControls.addEventListener('change', function () {
        if (selectedGizmo && transformControls.dragging && dotNetHelper) {
            const data = selectedGizmo.userData;
            const pos = selectedGizmo.position;
            dotNetHelper.invokeMethodAsync('OnGizmoMoved', data.type, data.index, pos.x, pos.y, pos.z);
        }
    });

    // Raycaster om gizmo's aan te klikken met de muis
    const raycaster = new THREE.Raycaster();
    const mouse = new THREE.Vector2();

    renderer.domElement.addEventListener('pointerdown', function (e) {
        if (transformControls.dragging) return;

        const rect = renderer.domElement.getBoundingClientRect();
        mouse.x = ((e.clientX - rect.left) / rect.width) * 2 - 1;
        mouse.y = -((e.clientY - rect.top) / rect.height) * 2 + 1;

        raycaster.setFromCamera(mouse, camera);

        const clickable = [];
        pivotsGroup.children.forEach(c => clickable.push(c));
        lightsGroup.children.forEach(c => clickable.push(c));
        socketsGroup.children.forEach(c => clickable.push(c));

        const intersects = raycaster.intersectObjects(clickable, true);
        if (intersects.length > 0) {
            let top = intersects[0].object;
            while (top.parent && top.parent !== pivotsGroup && top.parent !== lightsGroup && top.parent !== socketsGroup) {
                top = top.parent;
            }
            selectGizmo(top);
        }
    });

    // Belichting
    const ambientLight = new THREE.AmbientLight(0xffffff, 0.5);
    scene.add(ambientLight);

    const dirLight = new THREE.DirectionalLight(0xffffff, 0.7);
    dirLight.position.set(30, 50, 30);
    scene.add(dirLight);

    gridHelper = new THREE.GridHelper(50, 50, 0x38bdf8, 0x27272a);
    scene.add(gridHelper);

    const axesHelper = new THREE.AxesHelper(4);
    scene.add(axesHelper);

    staticGroup = new THREE.Group();
    movingGroup = new THREE.Group();
    pivotsGroup = new THREE.Group();
    lightsGroup = new THREE.Group();
    socketsGroup = new THREE.Group();

    scene.add(staticGroup);
    scene.add(movingGroup);
    scene.add(pivotsGroup);
    scene.add(lightsGroup);
    scene.add(socketsGroup);

    function animate() {
        requestAnimationFrame(animate);
        controls.update();

        if (isPlaying && movingGroup && movingGroup.children.length > 0) {
            animTime += 0.03 * animSpeed;

            let currentAngle = 0;
            if (isOscillating) {
                const t = (Math.sin(animTime) + 1) / 2;
                currentAngle = THREE.MathUtils.degToRad(minAngle + t * (maxAngle - minAngle));
            } else {
                currentAngle = animTime;
            }

            if (animAxis === 'x') movingGroup.rotation.set(currentAngle, 0, 0);
            else if (animAxis === 'y') movingGroup.rotation.set(0, currentAngle, 0);
            else if (animAxis === 'z') movingGroup.rotation.set(0, 0, currentAngle);
        }

        renderer.render(scene, camera);
    }
    animate();

    window.addEventListener('resize', () => {
        if (!container) return;
        camera.aspect = container.clientWidth / container.clientHeight;
        camera.updateProjectionMatrix();
        renderer.setSize(container.clientWidth, container.clientHeight);
    });
};

function selectGizmo(obj) {
    if (!obj) {
        transformControls.detach();
        selectedGizmo = null;
        return;
    }
    selectedGizmo = obj;
    transformControls.attach(obj);

    if (dotNetHelper && obj.userData) {
        dotNetHelper.invokeMethodAsync('OnGizmoSelected', obj.userData.type, obj.userData.index);
    }
}

window.selectGizmoFromUI = function (type, index) {
    let group = null;
    if (type === 'pivot') group = pivotsGroup;
    else if (type === 'light') group = lightsGroup;
    else if (type === 'socket') group = socketsGroup;

    if (group && index >= 0 && index < group.children.length) {
        selectGizmo(group.children[index]);
    }
};

window.renderStudioScene = function (payloadJson) {
    if (!scene) return;
    transformControls.detach();
    selectedGizmo = null;

    const data = typeof payloadJson === 'string' ? JSON.parse(payloadJson) : payloadJson;

    [staticGroup, movingGroup, pivotsGroup, lightsGroup, socketsGroup].forEach(group => {
        while (group.children.length > 0) {
            const obj = group.children[0];
            group.remove(obj);
            if (obj.geometry) obj.geometry.dispose();
            if (obj.material) {
                if (Array.isArray(obj.material)) obj.material.forEach(m => m.dispose());
                else obj.material.dispose();
            }
        }
    });

    animAxis = (data.animAxis || 'y').toLowerCase();
    isOscillating = data.isOscillating || false;
    minAngle = data.minAngle || 0;
    maxAngle = data.maxAngle || 0;
    animTime = 0;

    let totalBox = new THREE.Box3();

    // 1. Meshes
    if (data.parts && data.parts.length > 0) {
        data.parts.forEach(part => {
            const pos = part.positions || part.Positions;
            const idx = part.indices || part.Indices;
            const isMoving = part.isMoving !== undefined ? part.isMoving : part.IsMoving;

            if (!pos || pos.length === 0) return;

            const geometry = new THREE.BufferGeometry();
            geometry.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
            if (idx && idx.length > 0) geometry.setIndex(idx);
            geometry.computeVertexNormals();

            const color = isMoving ? 0xf59e0b : 0x2563eb;
            const material = new THREE.MeshStandardMaterial({
                color: color,
                metalness: 0.15,
                roughness: 0.45,
                side: THREE.DoubleSide,
                wireframe: isWireframe
            });

            const mesh = new THREE.Mesh(geometry, material);
            if (isMoving) movingGroup.add(mesh);
            else staticGroup.add(mesh);

            totalBox.expandByObject(mesh);
        });
    }

    // 2. Pivots
    if (data.pivots && data.pivots.length > 0) {
        data.pivots.forEach((p, idx) => {
            const pivotContainer = new THREE.Group();
            pivotContainer.position.set(p.x, p.y, p.z);
            pivotContainer.userData = { type: 'pivot', index: idx };

            const sphere = new THREE.Mesh(
                new THREE.SphereGeometry(0.22, 16, 16),
                new THREE.MeshBasicMaterial({ color: 0xfacc15 })
            );
            pivotContainer.add(sphere);

            const axes = new THREE.AxesHelper(0.9);
            pivotContainer.add(axes);

            pivotsGroup.add(pivotContainer);
            totalBox.expandByPoint(new THREE.Vector3(p.x, p.y, p.z));
        });
    }

    // 3. Lichten
    if (data.lights && data.lights.length > 0) {
        data.lights.forEach((l, idx) => {
            const lightContainer = new THREE.Group();
            lightContainer.position.set(l.x, l.y, l.z);
            lightContainer.userData = { type: 'light', index: idx };

            const hexColor = l.colorHex || 0xfffbeb;

            const pointLight = new THREE.PointLight(hexColor, l.intensity || 1.5, l.radius || 15);
            pointLight.name = 'realLight';
            lightContainer.add(pointLight);

            const bulb = new THREE.Mesh(
                new THREE.SphereGeometry(0.28, 16, 16),
                new THREE.MeshBasicMaterial({ color: hexColor })
            );
            bulb.name = 'bulb';
            lightContainer.add(bulb);

            const wireSphere = new THREE.Mesh(
                new THREE.SphereGeometry(Math.min(l.radius || 5, 8), 12, 12),
                new THREE.MeshBasicMaterial({ color: hexColor, wireframe: true, transparent: true, opacity: 0.15 })
            );
            wireSphere.name = 'radiusWire';
            lightContainer.add(wireSphere);

            lightsGroup.add(lightContainer);
            totalBox.expandByPoint(new THREE.Vector3(l.x, l.y, l.z));
        });
    }

    // 4. Sockets
    if (data.sockets && data.sockets.length > 0) {
        data.sockets.forEach((s, idx) => {
            const socketContainer = new THREE.Group();
            socketContainer.position.set(s.x, s.y, s.z);
            socketContainer.userData = { type: 'socket', index: idx };

            const ring = new THREE.Mesh(
                new THREE.TorusGeometry(0.4, 0.06, 8, 24),
                new THREE.MeshBasicMaterial({ color: 0x06b6d4 })
            );
            ring.rotation.x = Math.PI / 2;
            socketContainer.add(ring);

            const dirArrow = new THREE.ArrowHelper(new THREE.Vector3(0, 1, 0), new THREE.Vector3(0, 0, 0), 0.7, 0x06b6d4, 0.2, 0.1);
            socketContainer.add(dirArrow);

            socketsGroup.add(socketContainer);
            totalBox.expandByPoint(new THREE.Vector3(s.x, s.y, s.z));
        });
    }

    if (!totalBox.isEmpty()) {
        const center = totalBox.getCenter(new THREE.Vector3());
        const size = totalBox.getSize(new THREE.Vector3());
        const maxDim = Math.max(size.x, size.y, size.z, 2);

        [staticGroup, movingGroup, pivotsGroup, lightsGroup, socketsGroup].forEach(g => {
            g.position.sub(center);
            g.position.y += size.y / 2;
        });

        const distance = Math.max(maxDim * 1.6, 6);
        camera.position.set(distance, distance * 0.8, distance);
        controls.target.set(0, size.y / 2, 0);
        controls.update();
    }
};

window.updateLightRealtime = function (index, hexColor, intensity, radius, x, y, z) {
    if (index >= 0 && index < lightsGroup.children.length) {
        const container = lightsGroup.children[index];
        if (x !== undefined && y !== undefined && z !== undefined) {
            container.position.set(x, y, z);
        }
        const pointLight = container.getObjectByName('realLight');
        if (pointLight) {
            pointLight.color.setHex(hexColor);
            pointLight.intensity = intensity;
            pointLight.distance = radius;
        }
        const bulb = container.getObjectByName('bulb');
        if (bulb) bulb.material.color.setHex(hexColor);

        const wire = container.getObjectByName('radiusWire');
        if (wire) {
            wire.material.color.setHex(hexColor);
            wire.scale.setScalar(Math.min(radius, 8) / 5);
        }
    }
};

window.updatePivotRealtime = function (index, x, y, z) {
    if (index >= 0 && index < pivotsGroup.children.length) {
        pivotsGroup.children[index].position.set(x, y, z);
    }
};

window.updateSocketRealtime = function (index, x, y, z) {
    if (index >= 0 && index < socketsGroup.children.length) {
        socketsGroup.children[index].position.set(x, y, z);
    }
};

window.toggleLayer = function (layerName) {
    if (layerName === 'meshes') {
        showMeshes = !showMeshes;
        staticGroup.visible = showMeshes;
        movingGroup.visible = showMeshes;
        return showMeshes;
    } else if (layerName === 'pivots') {
        showPivots = !showPivots;
        pivotsGroup.visible = showPivots;
        return showPivots;
    } else if (layerName === 'lights') {
        showLights = !showLights;
        lightsGroup.visible = showLights;
        return showLights;
    } else if (layerName === 'sockets') {
        showSockets = !showSockets;
        socketsGroup.visible = showSockets;
        return showSockets;
    }
    return true;
};

window.setAnimationPlaying = function (playing) { isPlaying = playing; };
window.setAnimationSpeed = function (speed) { animSpeed = parseFloat(speed); };
window.setAnimationAxis = function (axis) {
    animAxis = axis.toLowerCase();
    if (movingGroup) movingGroup.rotation.set(0, 0, 0);
};

window.toggleWireframe = function () {
    isWireframe = !isWireframe;
    [staticGroup, movingGroup].forEach(group => {
        group.traverse(child => {
            if (child.isMesh) child.material.wireframe = isWireframe;
        });
    });
};

window.resetCamera = function () { if (controls) controls.reset(); };

window.downloadObjFile = function (filename, content) {
    const blob = new Blob([content], { type: 'text/plain' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    a.click();
    URL.revokeObjectURL(url);
};

window.downloadBinaryGbx = function (filename, base64Data) {
    const link = document.createElement('a');
    link.download = filename;
    link.href = "data:application/octet-stream;base64," + base64Data;
    link.click();
};
