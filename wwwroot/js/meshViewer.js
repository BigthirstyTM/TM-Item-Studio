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
let animationPeriodSeconds = 2;
let animationPhaseSeconds = 0;
let translationAxis = 'y';
let translationDistance = 0;
let pivotOffset = [0, 0, 0];
let harmonicEasing = false;
let invertMotion = false;
let hasTranslationMotion = false;
let translationMin = [0, 0, 0];
let translationMax = [0, 0, 0];
let lastFrameTime = null;
let animationFrameId = null;
let resizeHandler = null;

let showMeshes = true;
let showPivots = true;
let showLights = true;
let showSockets = true;

window.normalizeIcon = async function (bytes, size) {
    const blob = new Blob([bytes]);
    const bitmap = await createImageBitmap(blob);
    const canvas = document.createElement('canvas');
    canvas.width = size;
    canvas.height = size;
    const context = canvas.getContext('2d', { willReadFrequently: true });
    const scale = Math.max(size / bitmap.width, size / bitmap.height);
    const width = bitmap.width * scale;
    const height = bitmap.height * scale;
    context.clearRect(0, 0, size, size);
    context.drawImage(bitmap, (size - width) / 2, (size - height) / 2, width, height);
    const pixels = context.getImageData(0, 0, size, size).data;
    bitmap.close();
    return {
        pixels: Array.from(pixels),
        webPBase64: canvas.toDataURL('image/webp', 0.9).split(',')[1]
    };
};

window.renderIconPreview = function (canvasId, pixels, width, height) {
    const canvas = document.getElementById(canvasId);
    if (!canvas) return;
    canvas.width = width;
    canvas.height = height;
    const context = canvas.getContext('2d');
    const image = context.createImageData(width, height);
    image.data.set(new Uint8ClampedArray(pixels));
    context.putImageData(image, 0, 0);
};

window.init3DViewer = function (containerId, dotNetRef) {
    const container = document.getElementById(containerId);
    if (!container) return;

    window.dispose3DViewer();
    dotNetHelper = dotNetRef;
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

    staticGroup.visible = movingGroup.visible = showMeshes;
    pivotsGroup.visible = showPivots;
    lightsGroup.visible = showLights;
    socketsGroup.visible = showSockets;

    function animate(timestamp) {
        if (!renderer) return;
        animationFrameId = requestAnimationFrame(animate);
        controls.update();

        const deltaSeconds = lastFrameTime === null
            ? 0
            : Math.min(Math.max((timestamp - lastFrameTime) / 1000, 0), 0.05);
        lastFrameTime = timestamp;

        const animatedGroup = movingGroup && movingGroup.children.length > 0 ? movingGroup : staticGroup;
        if (isPlaying && animatedGroup && animatedGroup.children.length > 0) {
            animTime += deltaSeconds * animSpeed;

            const period = Math.max(animationPeriodSeconds, 0.01);
            const phase = ((animTime + animationPhaseSeconds) / period) * Math.PI * 2;
            let t = (Math.sin(phase) + 1) / 2;
            if (harmonicEasing) t = t * t * (3 - 2 * t);
            if (invertMotion) t = 1 - t;
            const currentAngle = isOscillating
                ? THREE.MathUtils.degToRad(minAngle + t * (maxAngle - minAngle))
                : (phase * (invertMotion ? -1 : 1));

            if (animAxis === 'x') animatedGroup.rotation.set(currentAngle, 0, 0);
            else if (animAxis === 'y') animatedGroup.rotation.set(0, currentAngle, 0);
            else if (animAxis === 'z') animatedGroup.rotation.set(0, 0, currentAngle);

            const distance = hasTranslationMotion ? (t - 0.5) * translationDistance : 0;
            const translation = hasTranslationMotion && dataHasTranslationRange()
                ? translationMin.map((value, index) => value + (translationMax[index] - value) * t)
                : [
                    translationAxis === 'x' ? distance : 0,
                    translationAxis === 'y' ? distance : 0,
                    translationAxis === 'z' ? distance : 0
                ];
            animatedGroup.position.set(
                pivotOffset[0] + translation[0],
                pivotOffset[1] + translation[1],
                pivotOffset[2] + translation[2]
            );
        }

        renderer.render(scene, camera);
    }
    animationFrameId = requestAnimationFrame(animate);

    resizeHandler = () => {
        if (!container.isConnected || !renderer || !camera) return;
        camera.aspect = container.clientWidth / container.clientHeight;
        camera.updateProjectionMatrix();
        renderer.setSize(container.clientWidth, container.clientHeight);
    };
    window.addEventListener('resize', resizeHandler);
};

function disposeObjectResources(root) {
    const geometries = new Set();
    const materials = new Set();
    root.traverse(child => {
        if (child.geometry) geometries.add(child.geometry);
        if (child.material) {
            const values = Array.isArray(child.material) ? child.material : [child.material];
            values.forEach(material => materials.add(material));
        }
    });
    geometries.forEach(geometry => geometry.dispose());
    materials.forEach(material => material.dispose());
}

window.dispose3DViewer = function () {
    if (animationFrameId !== null) cancelAnimationFrame(animationFrameId);
    animationFrameId = null;
    selectedGizmo = dotNetHelper = null;
    if (resizeHandler) window.removeEventListener('resize', resizeHandler);
    resizeHandler = null;
    if (transformControls) {
        transformControls.detach();
        transformControls.dispose();
        if (scene) scene.remove(transformControls);
    }
    if (controls) controls.dispose();
    if (scene) disposeObjectResources(scene);
    if (renderer) {
        renderer.dispose();
        renderer.domElement.remove();
    }
    renderer = scene = camera = controls = transformControls = null;
    staticGroup = movingGroup = pivotsGroup = lightsGroup = socketsGroup = null;
    gridHelper = null;
    animTime = 0;
    lastFrameTime = null;
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
        group.position.set(0, 0, 0);
        group.rotation.set(0, 0, 0);
        while (group.children.length > 0) {
            const obj = group.children[0];
            group.remove(obj);
            disposeObjectResources(obj);
        }
    });

    animAxis = (data.animAxis || 'y').toLowerCase();
    isOscillating = data.isOscillating || false;
    minAngle = data.minAngle || 0;
    maxAngle = data.maxAngle || 0;
    animationPeriodSeconds = Math.max(Number(data.animationPeriodSeconds) || 2, 0.01);
    animationPhaseSeconds = Number(data.animationPhaseSeconds) || 0;
    translationAxis = (data.translationAxis || 'y').toLowerCase();
    translationDistance = Number(data.translationDistance) || 0;
    hasTranslationMotion = data.hasTranslationMotion || Math.abs(translationDistance) > 0.0001;
    translationMin = data.translationMin || [0, 0, 0];
    translationMax = data.translationMax || [0, 0, 0];
    pivotOffset = data.pivotOffset || [0, 0, 0];
    harmonicEasing = data.harmonicEasing || false;
    invertMotion = data.invertMotion || false;
    animTime = 0;
    lastFrameTime = null;

    let totalBox = new THREE.Box3();

    // 1. Meshes
    if (data.parts && data.parts.length > 0) {
        data.parts.forEach(part => {
            const pos = part.positions || part.Positions;
            const idx = part.indices || part.Indices;
            const isMoving = part.isMoving !== undefined ? part.isMoving : part.IsMoving;

            if (!pos || pos.length === 0) return;

            const geometry = new THREE.BufferGeometry();
            const positions = isMoving
                ? pos.map((value, vertexIndex) => value - pivotOffset[vertexIndex % 3])
                : pos;
            geometry.setAttribute('position', new THREE.Float32BufferAttribute(positions, 3));
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

    movingGroup.position.set(pivotOffset[0], pivotOffset[1], pivotOffset[2]);

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

window.clearViewerScene = function () {
    if (!scene || !transformControls) return;
    transformControls.detach();
    selectedGizmo = null;
    [staticGroup, movingGroup, pivotsGroup, lightsGroup, socketsGroup].forEach(group => {
        group.position.set(0, 0, 0);
        group.rotation.set(0, 0, 0);
        while (group.children.length > 0) {
            const object = group.children[0];
            group.remove(object);
            disposeObjectResources(object);
        }
    });
};

window.setMotionPreview = function (motion) {
    if (!motion) return;
    animAxis = (motion.animAxis || 'y').toLowerCase();
    isOscillating = Boolean(motion.isOscillating);
    minAngle = Number(motion.minAngle) || 0;
    maxAngle = Number(motion.maxAngle) || 0;
    animationPeriodSeconds = Math.max(Number(motion.animationPeriodSeconds) || 2, 0.01);
    animationPhaseSeconds = Number(motion.animationPhaseSeconds) || 0;
    translationAxis = (motion.translationAxis || 'y').toLowerCase();
    translationDistance = Number(motion.translationDistance) || 0;
    hasTranslationMotion = Boolean(motion.hasTranslationMotion) || Math.abs(translationDistance) > 0.0001;
    translationMin = motion.translationMin || [0, 0, 0];
    translationMax = motion.translationMax || [0, 0, 0];
    pivotOffset = motion.pivotOffset || [0, 0, 0];
    animTime = 0;
    lastFrameTime = null;
};

function dataHasTranslationRange() {
    return translationMin.some((value, index) => Math.abs(value - translationMax[index]) > 0.000001);
}

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
