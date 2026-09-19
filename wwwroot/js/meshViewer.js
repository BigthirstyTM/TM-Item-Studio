let scene, camera, renderer, controls, transformControls, gridHelper;
let staticGroup, movingGroup, collisionGroup, pivotsGroup, lightsGroup;
let dotNetHelper = null, selectedGizmo = null;
let isWireframe = false, isPlaying = true, animSpeed = 1;
let animTime = 0, lastFrameTime = null, animationFrameId = null, resizeHandler = null;
let resizeObserver = null;
let showMeshes = true, showCollision = false, showPivots = true, showLights = true;
let typedMotions = [], motionGroups = new Map(), previewPhase01 = 0, currentPayload = null;
let legacyMotion = null;
let geometryEpoch = null, sharedGeometry = new Map();
const pooledGeometry = new WeakSet();
let sceneFilter = { materialIndex: null, materialPath: null, lodMask: null };
let textureDirectory = null, textureFiles = new Map(), textureRenderVersion = 0, lastTextureMatches = null;

function normalizedPath(value) {
    return value.replaceAll('\\', '/').toLowerCase();
}
function isPreviewTexture(name) {
    return /\.(dds|png|jpe?g|webp)$/i.test(name);
}
async function indexTextureDirectory(directory, prefix = '') {
    for await (const [name, handle] of directory.entries()) {
        const path = prefix ? `${prefix}/${name}` : name;
        if (handle.kind === 'directory') await indexTextureDirectory(handle, path);
        else textureFiles.set(normalizedPath(path), handle);
    }
}
window.selectStudioTextureDirectory = async function () {
    if (!window.showDirectoryPicker) throw new Error('This browser does not support selecting a local folder. Use a current Chromium-based browser.');
    const directory = await window.showDirectoryPicker({ mode: 'read' });
    const previousFiles = textureFiles;
    textureFiles = new Map();
    try {
        await indexTextureDirectory(directory);
        textureDirectory = directory;
        return { name: directory.name, fileCount: textureFiles.size };
    } catch (error) {
        textureFiles = previousFiles;
        throw error;
    }
};
function materialToken(material) {
    const source = material.gameMaterialLink || material.gameMaterialName || '';
    const last = source.replaceAll('\\', '/').split('/').pop() || '';
    return last.replace(/_asset(?:\.\d+)?$/i, '').toLowerCase();
}
function comparePaths(first, second) {
    // Codepoint ordering (not localeCompare) keeps matching identical across
    // browsers, locales and reloads.
    return first < second ? -1 : first > second ? 1 : 0;
}
function textureCandidates(token) {
    if (!token) return [];
    const candidates = [...textureFiles.entries()].filter(([path]) => {
        const filename = path.split('/').pop();
        return isPreviewTexture(filename) && (filename === `${token}.dds` || filename.startsWith(`${token}_`) || filename.startsWith(`${token}.`));
    });
    candidates.sort(([first], [second]) => {
        const rank = path => /(?:_d|_diffuse|_albedo|_color)\.(?:dds|png|jpe?g|webp)$/i.test(path) ? 0 : 1;
        return rank(first) - rank(second) || comparePaths(first, second);
    });
    return candidates;
}
// Deterministic material-to-file matching: a pure function of the payload's
// material identities and the indexed filenames. Equal identities (one game
// material used by several solids) legitimately share one texture, but
// distinct identities collapsing onto the same candidate files stay neutral
// instead of guessing which material the files belong to.
function matchTextureFiles(materials) {
    const identities = new Map();
    for (const material of materials) {
        if (!material?.path) continue;
        const identity = material.gameMaterialLink || material.gameMaterialName || '';
        if (!identities.has(identity))
            identities.set(identity, { token: materialToken(material), key: null, file: null, handle: null, status: 'absent' });
    }
    const shared = new Map();
    for (const [identity, match] of identities) {
        const candidates = textureCandidates(match.token);
        if (!candidates.length) continue;
        match.key = candidates.map(([path]) => path).join('\n');
        match.file = candidates[0][0]; match.handle = candidates[0][1]; match.status = 'matched';
        shared.set(match.key, (shared.get(match.key) ?? new Set()).add(identity));
    }
    for (const match of identities.values())
        if (match.key != null && shared.get(match.key).size > 1) { match.status = 'ambiguous'; match.file = match.handle = null; }
    const matches = new Map();
    for (const material of materials) {
        if (!material?.path) continue;
        const match = identities.get(material.gameMaterialLink || material.gameMaterialName || '');
        matches.set(material.path, { token: match.token, status: match.status, file: match.file, handle: match.handle });
    }
    return matches;
}
let ddsLoaderPromise = null;
function ensureDdsLoader() {
    if (typeof THREE.DDSLoader === 'function') return Promise.resolve();
    // three r128 ships DDSLoader as a separate examples module; fetch it once,
    // on demand, pinned to the same revision as the page's three.js build.
    ddsLoaderPromise ??= new Promise((resolve, reject) => {
        const script = document.createElement('script');
        script.src = 'https://cdn.jsdelivr.net/npm/three@0.128.0/examples/js/loaders/DDSLoader.js';
        script.onload = () => resolve();
        script.onerror = () => { ddsLoaderPromise = null; script.remove(); reject(new Error('The DDS preview loader could not be downloaded.')); };
        document.head.appendChild(script);
    });
    return ddsLoaderPromise;
}
async function loadDdsTexture(file, path) {
    await ensureDdsLoader();
    const buffer = await file.arrayBuffer();
    if (buffer.byteLength < 20 || String.fromCharCode(...new Uint8Array(buffer, 0, 4)) !== 'DDS ')
        throw new Error(`${path} is not a readable DDS file.`);
    // CompressedTextureLoader.load() runs parse() uncaught inside the fetch
    // callback (three r128), so parse here where malformed files reject
    // cleanly and the viewer keeps its neutral material.
    const dds = new THREE.DDSLoader().parse(buffer, true);
    if (dds.isCubemap || !dds.format || !dds.mipmaps.length) throw new Error(`${path} uses an unsupported DDS layout.`);
    const texture = new THREE.CompressedTexture();
    texture.image = { width: dds.width, height: dds.height };
    texture.mipmaps = dds.mipmaps; texture.format = dds.format;
    if (dds.mipmapCount === 1) texture.minFilter = THREE.LinearFilter;
    texture.needsUpdate = true;
    return texture;
}
function loadImageTexture(file) {
    return new Promise((resolve, reject) => {
        const url = URL.createObjectURL(file);
        new THREE.TextureLoader().load(url, texture => { URL.revokeObjectURL(url); resolve(texture); },
            undefined, error => { URL.revokeObjectURL(url); reject(error); });
    });
}
function loadTexture(handle, path) {
    return handle.getFile().then(file => /\.dds$/i.test(path) ? loadDdsTexture(file, path) : loadImageTexture(file));
}
async function applyLocalTextures(data, renderVersion) {
    if (!textureDirectory || !Array.isArray(data.materials)) return;
    const matches = matchTextureFiles(data.materials);
    lastTextureMatches = [...matches.entries()]
        .map(([materialPath, match]) => ({ materialPath, token: match.token, status: match.status, file: match.file }))
        .sort((first, second) => comparePaths(first.materialPath, second.materialPath));
    const requested = new Map();
    for (const group of [staticGroup, movingGroup]) group?.traverse(mesh => {
        if (!mesh.isMesh || !Array.isArray(mesh.userData.mappings)) return;
        const mapping = mesh.userData.mappings.find(candidate => matches.has(candidate.materialPath));
        if (!mapping || requested.has(mapping.materialPath)) return;
        const match = matches.get(mapping.materialPath);
        if (match.status === 'matched') requested.set(mapping.materialPath, loadTexture(match.handle, match.file));
    });
    for (const [materialPath, texturePromise] of requested) {
        try {
            const texture = await texturePromise;
            if (renderVersion !== textureRenderVersion) { texture.dispose(); continue; }
            for (const group of [staticGroup, movingGroup]) group?.traverse(mesh => {
                if (!mesh.isMesh || !mesh.userData.mappings?.some(mapping => mapping.materialPath === materialPath)) return;
                mesh.material.map = texture; mesh.material.needsUpdate = true;
            });
        } catch (error) {
            console.warn(`Could not load local texture for ${materialPath}.`, error);
        }
    }
}
window.getStudioTexturePreviewInfo = function () {
    return { directory: textureDirectory?.name ?? null, fileCount: textureFiles.size,
        renderVersion: textureRenderVersion, matches: lastTextureMatches ?? [] };
}

window.getStudioViewerDebugInfo = function () {
    let meshes = 0;
    const colors = new Set();
    for (const group of [staticGroup, movingGroup]) group?.traverse(node => {
        if (!node.isMesh) return;
        meshes++;
        for (const material of (Array.isArray(node.material) ? node.material : [node.material]))
            if (material?.color) colors.add(material.color.getHexString());
    });
    return {
        initialized: Boolean(renderer?.domElement.isConnected), meshes,
        materialColors: [...colors].slice(0, 128),
        cameraPosition: camera?.position.toArray() ?? null,
        cameraTarget: controls?.target.toArray() ?? null,
        playing: isPlaying, wireframe: isWireframe,
        layers: { meshes: showMeshes, collision: showCollision, pivots: showPivots, lights: showLights }
    };
};

window.normalizeIcon = async function (bytes, size) {
    const bitmap = await createImageBitmap(new Blob([bytes])), canvas = document.createElement('canvas');
    canvas.width = canvas.height = size;
    const context = canvas.getContext('2d', { willReadFrequently: true });
    const scale = Math.max(size / bitmap.width, size / bitmap.height);
    context.drawImage(bitmap, (size - bitmap.width * scale) / 2, (size - bitmap.height * scale) / 2, bitmap.width * scale, bitmap.height * scale);
    const pixels = context.getImageData(0, 0, size, size).data;
    bitmap.close();
    return { pixels: Array.from(pixels), webPBase64: canvas.toDataURL('image/webp', .9).split(',')[1] };
};
window.renderIconPreview = function (canvasId, pixels, width, height) {
    const canvas = document.getElementById(canvasId);
    if (!canvas) return;
    canvas.width = width; canvas.height = height;
    const context = canvas.getContext('2d'), image = context.createImageData(width, height);
    image.data.set(new Uint8ClampedArray(pixels)); context.putImageData(image, 0, 0);
};
function finite(value, name) {
    if (typeof value !== 'number' || !Number.isFinite(value)) throw new Error(`${name} must be finite.`);
    return value;
}
function nonnegative(value, name) {
    if (finite(value, name) < 0) throw new Error(`${name} must be nonnegative.`);
    return value;
}
function phase(value) {
    if (nonnegative(value, 'Preview phase') > 1) throw new Error('Preview phase must be in [0, 1].');
    return value;
}
function buffer(values, name, stride) {
    if (!(Array.isArray(values) || values instanceof Float32Array) || values.length % stride || values.some(x => typeof x !== 'number' || !Number.isFinite(x) || !Number.isFinite(Math.fround(x))))
        throw new Error(`${name} must be a finite ${stride}-component buffer.`);
    return values;
}
function restMatrix(values, name) {
    buffer(values, name, 16);
    if (values.length !== 16 || values[3] !== 0 || values[7] !== 0 || values[11] !== 0 || values[15] !== 1) throw new Error(`${name} must be affine.`);
    const matrix = new THREE.Matrix4().fromArray(values);
    if (!Number.isFinite(matrix.determinant()) || matrix.determinant() === 0 || matrix.clone().invert().elements.some(x => !Number.isFinite(x)))
        throw new Error(`${name} must be invertible.`);
    return matrix;
}
function timeline(source) {
    if (!source || typeof source.isDuration !== 'boolean' || !Array.isArray(source.keys) || source.keys.length > 4)
        throw new Error('Unsupported or absent timeline: expected a timing mode and at most four keys.');
    const keys = source.keys.map((key, index) => {
        if (!key || !Number.isInteger(key.ease) || key.ease < 0 || key.ease > 4 || typeof key.reverse !== 'boolean'
            || !Number.isInteger(key.durationMilliseconds) || key.durationMilliseconds < 0 || key.durationMilliseconds > 2147483647)
            throw new Error('Unsupported easing or invalid key.');
        // Loaded native functions use durations; GBX.NET retains raw archived endpoints.
        // Difference original neighbours, preserving the caller's flag and time words.
        const durationMilliseconds = source.isDuration || index === 0 ? key.durationMilliseconds
            : Math.max(0, key.durationMilliseconds - source.keys[index - 1].durationMilliseconds);
        return { ease: key.ease, reverse: key.reverse, durationMilliseconds };
    });
    const total = keys.reduce((sum, key) => sum + key.durationMilliseconds, 0);
    if (total > 2147483647) throw new Error('Duration exceeds the supported millisecond range.');
    return { keys, total };
}
function compileMotions(sources) {
    if (!Array.isArray(sources)) throw new Error('Motions must be an array.');
    const byChild = new Map(), paths = new Set();
    for (const source of sources) {
        if (!source || typeof source.path !== 'string' || !source.path || typeof source.childPath !== 'string' || !source.childPath
            || (source.parentPath != null && (typeof source.parentPath !== 'string' || !source.parentPath)) || paths.has(source.path) || byChild.has(source.childPath))
            throw new Error('Motion paths must be explicit; each child requires exactly one writer.');
        const fields = source.fields;
        if (!fields || ![0, 1, 2].includes(fields.translationAxis) || ![0, 1, 2].includes(fields.rotationAxis)) throw new Error('Unsupported motion axis.');
        for (const key of ['translationMin', 'translationMax', 'angleMinDegrees', 'angleMaxDegrees']) finite(fields[key], key);
        const childRest = restMatrix(source.childRest, 'Child rest'), parentRest = restMatrix(source.parentRest, 'Parent rest');
        if (source.parentPath == null && !parentRest.equals(new THREE.Matrix4())) throw new Error('World parent requires identity parent rest.');
        paths.add(source.path);
        byChild.set(source.childPath, { ...source, fields: { ...fields }, childRest, parentRest,
            inverseChild: childRest.clone().invert(), inverseParent: parentRest.clone().invert(), live: childRest.clone(),
            translation: timeline(fields.translation), rotation: timeline(fields.rotation) });
    }
    for (const motion of byChild.values())
        if (motion.parentPath != null && !byChild.has(motion.parentPath))
            throw new Error('Motion parent path is unresolved.');
    const ordered = [], visiting = new Set(), visited = new Set();
    function visit(motion) {
        if (visited.has(motion)) return;
        if (visiting.has(motion)) throw new Error('Motion parents contain a cycle.');
        visiting.add(motion); motion.parentMotion = byChild.get(motion.parentPath);
        if (motion.parentMotion) {
            if (!motion.parentRest.elements.every((x, i) => Math.abs(x - motion.parentMotion.childRest.elements[i]) <= 1e-5)) throw new Error('Parent rest transforms disagree.');
            visit(motion.parentMotion);
        }
        visiting.delete(motion); visited.add(motion); ordered.push(motion);
    }
    for (const motion of byChild.values()) visit(motion);
    return ordered;
}
function sampleTimeline(track, seconds, offset, min, max) {
    if (track.keys.length === 0) return min;
    if (track.total === 0) return 0;
    // Visual preview is quantized to nearest microsecond, matching the C# evaluator.
    // Integer-second reduction for huge times spans 1000 cycles and avoids overflowing safe integers.
    const totalUs = track.total * 1000;
    const timeUs = Math.round(seconds * 1e6 <= Number.MAX_SAFE_INTEGER ? seconds * 1e6 : (seconds % track.total) * 1e6);
    const phaseUs = Math.round(offset * totalUs);
    let position = ((timeUs % totalUs) + phaseUs) % totalUs;
    for (const key of track.keys) {
        if (key.durationMilliseconds === 0) continue;
        const duration = key.durationMilliseconds * 1000;
        if (position >= duration) { position -= duration; continue; }
        const x = position / duration;
        let u;
        switch (key.ease) {
            case 0: u = 0; break;
            case 1: u = x; break;
            case 2: u = x * x; break;
            case 3: u = x * (2 - x); break;
            case 4: u = x < .5 ? 2 * x * x : 1 - 2 * (1 - x) * (1 - x); break;
        }
        if (key.reverse) u = 1 - u;
        return (1 - u) * min + u * max;
    }
    return min;
}
function applyTypedMotion() {
    for (const motion of typedMotions) {
        const f = motion.fields;
        const distance = sampleTimeline(motion.translation, animTime / 1000, previewPhase01, f.translationMin, f.translationMax);
        const angle = sampleTimeline(motion.rotation, animTime / 1000, previewPhase01, f.angleMinDegrees, f.angleMaxDegrees);
        const axis = new THREE.Vector3().setComponent(f.rotationAxis, 1), translation = new THREE.Vector3().setComponent(f.translationAxis, distance);
        const signal = new THREE.Matrix4().makeTranslation(translation.x, translation.y, translation.z)
            .multiply(new THREE.Matrix4().makeRotationAxis(axis, THREE.MathUtils.degToRad(angle)));
        motion.live.copy(motion.parentMotion ? motion.parentMotion.live : motion.parentRest)
            .multiply(motion.inverseParent).multiply(motion.childRest).multiply(signal);
        for (const group of motionGroups.get(motion.childPath) || []) { group.matrix.copy(motion.live); group.matrixWorldNeedsUpdate = true; }
    }
}
function applyLegacyMotion() {
    if (!legacyMotion) return;
    const m = legacyMotion, period = Math.max(Number(m.period) || 2, .01);
    const t = (Math.sin(((animTime / 1000 + (Number(m.phase) || 0)) / period) * Math.PI * 2) + 1) / 2;
    const eased = m.harmonic ? t * t * (3 - 2 * t) : t;
    const value = m.oscillating ? (Number(m.min) || 0) + eased * ((Number(m.max) || 0) - (Number(m.min) || 0)) : eased * 360;
    const axis = ['x', 'y', 'z'].includes(m.axis) ? m.axis : 'y';
    movingGroup.rotation.set(0, 0, 0); movingGroup.rotation[axis] = THREE.MathUtils.degToRad(value);
    const distance = (eased - .5) * (Number(m.distance) || 0);
    movingGroup.position.set(0, 0, 0);
    if (m.translation) movingGroup.position[m.translation] = distance;
}
window.init3DViewer = function (containerId, dotNetRef) {
    const container = document.getElementById(containerId);
    if (!container) return;
    window.dispose3DViewer(); dotNetHelper = dotNetRef; container.innerHTML = '';
    const width = container.clientWidth || 800, height = container.clientHeight || 550;
    scene = new THREE.Scene(); scene.background = new THREE.Color(0x131316);
    camera = new THREE.PerspectiveCamera(45, width / height, .1, 2000); camera.position.set(12, 10, 12);
    renderer = new THREE.WebGLRenderer({ antialias: true }); renderer.setSize(width, height); renderer.shadowMap.enabled = true;
    container.appendChild(renderer.domElement);
    controls = new THREE.OrbitControls(camera, renderer.domElement); controls.enableDamping = true; controls.dampingFactor = .05;
    transformControls = new THREE.TransformControls(camera, renderer.domElement); transformControls.size = .75; scene.add(transformControls);
    transformControls.addEventListener('dragging-changed', event => { if (controls) controls.enabled = !event.value; });
    transformControls.addEventListener('change', () => {
        if (selectedGizmo?.userData.editable && transformControls.dragging && dotNetHelper) {
            const d = selectedGizmo.userData, p = selectedGizmo.position;
            dotNetHelper.invokeMethodAsync('OnGizmoMoved', d.type, d.index, p.x, p.y, p.z);
        }
    });
    const raycaster = new THREE.Raycaster(), mouse = new THREE.Vector2();
    renderer.domElement.addEventListener('pointerdown', event => {
        if (transformControls.dragging) return;
        const rect = renderer.domElement.getBoundingClientRect();
        mouse.set((event.clientX - rect.left) / rect.width * 2 - 1, -(event.clientY - rect.top) / rect.height * 2 + 1);
        raycaster.setFromCamera(mouse, camera);
        const groups = [pivotsGroup, lightsGroup];
        const hits = raycaster.intersectObjects(groups.filter(g => g.visible).flatMap(g => g.children), true);
        if (hits.length) {
            let obj = hits[0].object;
            while (obj.parent && !groups.includes(obj.parent)) obj = obj.parent;
            selectGizmo(obj);
        }
    });
    scene.add(new THREE.AmbientLight(0xffffff, .5));
    const light = new THREE.DirectionalLight(0xffffff, .7); light.position.set(30, 50, 30); scene.add(light);
    gridHelper = new THREE.GridHelper(50, 50, 0x38bdf8, 0x27272a); scene.add(gridHelper, new THREE.AxesHelper(4));
    [staticGroup, movingGroup, collisionGroup, pivotsGroup, lightsGroup] = Array.from({ length: 5 }, () => new THREE.Group());
    scene.add(staticGroup, movingGroup, collisionGroup, pivotsGroup, lightsGroup); applyLayerVisibility();
    function animate(timestamp) {
        if (!renderer) return;
        animationFrameId = requestAnimationFrame(animate);
        const delta = lastFrameTime === null ? 0 : Math.min(Math.max(timestamp - lastFrameTime, 0), 50);
        lastFrameTime = timestamp;
        if (isPlaying) animTime += delta * animSpeed;
        applyTypedMotion(); applyLegacyMotion(); controls.update(); renderer.render(scene, camera);
    }
    animationFrameId = requestAnimationFrame(animate);
    resizeHandler = () => {
        if (!container.isConnected || !renderer || !camera) return;
        const w = container.clientWidth || 1, h = container.clientHeight || 1;
        camera.aspect = w / h; camera.updateProjectionMatrix(); renderer.setSize(w, h);
    };
    window.addEventListener('resize', resizeHandler);
    resizeObserver = new ResizeObserver(resizeHandler);
    resizeObserver.observe(container);
};
function disposeObjectResources(root) {
    const geometries = new Set(), materials = new Set(), textures = new Set();
    root.traverse(child => {
        if (child.geometry && !pooledGeometry.has(child.geometry)) geometries.add(child.geometry);
        if (child.material) (Array.isArray(child.material) ? child.material : [child.material]).forEach(m => materials.add(m));
    });
    geometries.forEach(g => g.dispose()); materials.forEach(m => { if (m.map) textures.add(m.map); m.dispose(); }); textures.forEach(texture => texture.dispose());
}
window.dispose3DViewer = function () {
    if (animationFrameId !== null) cancelAnimationFrame(animationFrameId);
    animationFrameId = null; selectedGizmo = dotNetHelper = null;
    if (resizeHandler) window.removeEventListener('resize', resizeHandler);
    resizeObserver?.disconnect(); resizeObserver = null;
    resizeHandler = null;
    if (transformControls) { transformControls.detach(); transformControls.dispose(); scene?.remove(transformControls); }
    controls?.dispose(); if (scene) disposeObjectResources(scene);
    releaseGeometryPool();
    if (renderer) { renderer.dispose(); renderer.domElement.remove(); }
    renderer = scene = camera = controls = transformControls = null;
    staticGroup = movingGroup = collisionGroup = pivotsGroup = lightsGroup = gridHelper = null;
    typedMotions = []; motionGroups = new Map(); currentPayload = null; animTime = 0; lastFrameTime = null;
};
function selectGizmo(obj) {
    if (!transformControls) return;
    transformControls.detach(); selectedGizmo = obj;
    if (!obj) return;
    if (obj.userData.editable) transformControls.attach(obj);
    if (dotNetHelper) dotNetHelper.invokeMethodAsync('OnGizmoSelected', obj.userData.type, obj.userData.index);
}
window.scrollSelectedVariantIntoView = function () {
    document.querySelector('.studio-variant-buttons button[aria-pressed="true"]')
        ?.scrollIntoView({ block: 'nearest', inline: 'nearest' });
};

window.selectGizmoFromUI = function (type, index) {
    const group = { pivot: pivotsGroup, light: lightsGroup }[type];
    const obj = group?.children.find(child => child.userData.index === index); if (obj) selectGizmo(obj);
};
function mapping(value) {
    if (!value || value.materialIndex != null && (!Number.isInteger(value.materialIndex) || value.materialIndex < 0)) throw new Error('Invalid material mapping.');
    if (value.lodMask != null && (!Number.isInteger(value.lodMask) || value.lodMask < -2147483648 || value.lodMask > 2147483647)) throw new Error('Invalid LOD mask.');
    if (value.materialPath != null && (typeof value.materialPath !== 'string' || !value.materialPath)) throw new Error('Invalid material path.');
    return { materialIndex: value.materialIndex ?? null, lodMask: value.lodMask ?? null, materialPath: value.materialPath ?? null };
}
function geometryWords(bytes, integer = false) {
    if (!(bytes instanceof Uint8Array) || bytes.byteLength % 4) throw new Error('Invalid binary geometry buffer.');
    const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
    const values = integer ? new Int32Array(bytes.byteLength / 4) : new Float32Array(bytes.byteLength / 4);
    for (let i = 0; i < values.length; i++) values[i] = integer ? view.getInt32(i * 4, true) : view.getFloat32(i * 4, true);
    return values;
}
function buildGeometry(part) {
    const positions = buffer(part.positionsBytes == null ? part.positions ?? part.Positions : geometryWords(part.positionsBytes), 'Positions', 3);
    const indices = part.indicesBytes == null ? part.indices ?? part.Indices : geometryWords(part.indicesBytes, true);
    const normals = part.normalsBytes == null ? part.normals : geometryWords(part.normalsBytes);
    if (!(Array.isArray(indices) || indices instanceof Int32Array) || indices.length % 3 || indices.some(x => !Number.isInteger(x) || x < 0 || x >= positions.length / 3)) throw new Error('Invalid triangle indices.');
    for (const [value, key, stride] of [[normals, 'normals', 3], [part.uvs, 'uvs', 2]])
        if (value != null && buffer(value, key, stride).length / stride !== positions.length / 3) throw new Error(`${key} count does not match vertices.`);
    const geometry = new THREE.BufferGeometry(); geometry.setAttribute('position', new THREE.Float32BufferAttribute(positions, 3));
    geometry.setIndex(indices instanceof Int32Array ? new THREE.BufferAttribute(new Uint32Array(indices), 1) : indices);
    if (normals != null) geometry.setAttribute('normal', new THREE.Float32BufferAttribute(normals, 3)); else geometry.computeVertexNormals();
    if (part.uvs != null) geometry.setAttribute('uv', new THREE.Float32BufferAttribute(part.uvs, 2));
    if (geometry.attributes.normal.array.some(x => !Number.isFinite(x))) { geometry.dispose(); throw new Error('Normals overflow geometry.'); }
    geometry.computeBoundingBox();
    return geometry;
}
function makePart(part, owner, bounds, pool) {
    const mappings = part.mappings == null ? [mapping(part)] : part.mappings.map(mapping);
    const shared = part.geometryId != null;
    const world = shared ? restMatrix(part.worldTransform, 'Instance transform') : new THREE.Matrix4();
    const geometry = shared ? pool.get(part.geometryId) : buildGeometry(part);
    if (!geometry) throw new Error('Unknown shared geometry.');
    const instance = owner ? owner.inverseChild.clone().multiply(world) : world;
    const box = geometry.boundingBox.clone().applyMatrix4(world);
    if ([...(box.isEmpty() ? [] : [...box.min.toArray(), ...box.max.toArray()]), ...instance.elements].some(x => !Number.isFinite(x) || !Number.isFinite(Math.fround(x)))) {
        if (!shared) geometry.dispose();
        throw new Error('Rest transform overflows geometry.');
    }
    bounds.union(box);
    // Game textures are not available in this payload. Keep real geometry
    // readable with a neutral, lit material; never substitute fake geometry.
    const material = new THREE.MeshStandardMaterial({ color: part.isCollision ? 0x38d9b3 : 0xb8bdc6,
        metalness: 0, roughness: .75, side: THREE.DoubleSide, wireframe: part.isCollision || isWireframe, transparent: Boolean(part.isCollision), opacity: part.isCollision ? .35 : 1 });
    const mesh = new THREE.Mesh(geometry, material); mesh.name = part.name ?? part.path ?? '';
    mesh.matrixAutoUpdate = false; mesh.matrix.copy(instance);
    mesh.userData = { path: part.path, entityPath: part.entityPath, isCollision: Boolean(part.isCollision), mappings }; return mesh;
}
function makeGizmo(data, index, type) {
    const position = ['x', 'y', 'z'].map(key => finite(data[key], `${type} ${key}`));
    if (data.index != null && (!Number.isInteger(data.index) || data.index < 0)) throw new Error('Invalid gizmo index.');
    const group = new THREE.Group(); group.position.fromArray(position); group.userData = { type, index: data.index ?? index, editable: data.editable !== false };
    if (type === 'light') {
        const color = data.colorHex ?? 0xfffbeb, intensity = data.intensity ?? 1.5, radius = data.radius ?? 15;
        if (!Number.isInteger(color) || color < 0 || color > 0xffffff) throw new Error('Light color must be RGB24.');
        nonnegative(intensity, 'Light intensity'); nonnegative(radius, 'Light radius');
        const point = new THREE.PointLight(color, intensity, radius); point.name = 'realLight'; group.add(point);
        const bulb = new THREE.Mesh(new THREE.SphereGeometry(.28, 16, 16), new THREE.MeshBasicMaterial({ color })); bulb.name = 'bulb'; group.add(bulb);
        const wire = new THREE.Mesh(new THREE.SphereGeometry(1, 12, 12), new THREE.MeshBasicMaterial({ color, wireframe: true, transparent: true, opacity: .15 }));
        wire.name = 'radiusWire'; wire.scale.setScalar(radius); group.add(wire);
    } else if (type === 'pivot') group.add(new THREE.Mesh(new THREE.SphereGeometry(.22, 16, 16), new THREE.MeshBasicMaterial({ color: 0xfacc15 })), new THREE.AxesHelper(.9));
    else throw new Error('Unknown gizmo type.');
    return group;
}
function frameCamera(bounds) {
    if (bounds.isEmpty()) return;
    const center = bounds.getCenter(new THREE.Vector3()), radius = Math.max(bounds.getSize(new THREE.Vector3()).length() / 2, 1);
    const vertical = THREE.MathUtils.degToRad(camera.fov), horizontal = 2 * Math.atan(Math.tan(vertical / 2) * camera.aspect);
    const distance = radius / Math.sin(Math.min(vertical, horizontal) / 2) * 1.2;
    camera.position.copy(center).add(new THREE.Vector3(1, .8, 1).normalize().multiplyScalar(distance));
    camera.near = Math.max(distance / 10000, .001); camera.far = Math.max(distance * 10, radius * 100, 100);
    camera.updateProjectionMatrix(); controls.target.copy(center); controls.update(); controls.saveState();
}
function renderPayload(input, preserveCamera) {
    if (!scene) return;
    const data = structuredClone(typeof input === 'string' ? JSON.parse(input) : input);
    if (!data || typeof data !== 'object') throw new Error('Scene payload required.');
    const nextLegacyMotion = (data.motions?.length ?? 0) === 0 && (data.hasTranslationMotion || data.isOscillating)
        ? { period: data.animationPeriodSeconds, phase: data.animationPhaseSeconds, min: data.minAngle, max: data.maxAngle,
            axis: data.animAxis, oscillating: data.isOscillating, distance: data.translationDistance,
            translation: data.hasTranslationMotion ? data.translationAxis : null, harmonic: data.harmonicEasing }
        : null;
    const motions = compileMotions(data.motions ?? []), offset = phase(data.previewPhase01 ?? 0), owners = new Map(motions.map(m => [m.childPath, m]));
    const staged = Array.from({ length: 5 }, () => new THREE.Group()), groups = new Map(), bounds = new THREE.Box3();
    const epoch = data.geometryEpoch ?? null;
    if (epoch !== null && (!Number.isSafeInteger(epoch) || epoch < 0)) throw new Error('Invalid geometry epoch.');
    const pool = epoch === geometryEpoch ? new Map(sharedGeometry) : new Map();
    const added = [];
    // Stage before replacing: rejected payloads leave the current scene and playback intact.
    try {
        for (const definition of data.geometryDefinitions ?? []) {
            if (epoch === null || !Number.isSafeInteger(definition.geometryId) || definition.geometryId < 0 || pool.has(definition.geometryId))
                throw new Error('Invalid or duplicate shared geometry identity.');
            const geometry = buildGeometry(definition);
            pooledGeometry.add(geometry); added.push(geometry); pool.set(definition.geometryId, geometry);
        }
        for (const motion of motions) {
            const pair = [new THREE.Group(), new THREE.Group()];
            pair.forEach(group => { group.matrixAutoUpdate = false; group.matrix.copy(motion.childRest); });
            staged[1].add(pair[0]); staged[2].add(pair[1]); groups.set(motion.childPath, pair);
        }
        for (const part of data.parts ?? []) {
            const owner = owners.get(part.entityPath), mesh = makePart(part, owner, bounds, pool);
            if (owner) groups.get(owner.childPath)[part.isCollision ? 1 : 0].add(mesh);
            else staged[part.isCollision ? 2 : (part.isMoving ? 1 : 0)].add(mesh);
        }
        for (const [property, type, target] of [['pivots', 'pivot', 3], ['lights', 'light', 4]])
            (data[property] ?? []).forEach((gizmo, index) => { const group = makeGizmo(gizmo, index, type); staged[target].add(group); bounds.expandByPoint(group.position); });
    } catch (error) { staged.forEach(disposeObjectResources); added.forEach(g => g.dispose()); throw error; }
    window.clearViewerScene(true);
    if (epoch !== geometryEpoch) releaseGeometryPool();
    sharedGeometry = pool; geometryEpoch = epoch;
    const destinations = [staticGroup, movingGroup, collisionGroup, pivotsGroup, lightsGroup];
    staged.forEach((group, index) => { while (group.children.length) destinations[index].add(group.children[0]); });
    isPlaying = data.playing !== false; legacyMotion = nextLegacyMotion;
    typedMotions = motions; motionGroups = groups; previewPhase01 = offset;
    currentPayload = { ...data, geometryDefinitions: [] };
    applyTypedMotion(); applySceneFilter(); applyLayerVisibility(); if (!preserveCamera) frameCamera(bounds);
    const renderVersion = ++textureRenderVersion;
    void applyLocalTextures(data, renderVersion);
}
window.renderStudioScene = payload => renderPayload(payload, false);
window.setTypedMotionPreview = function (value) {
    if (!value) throw new Error('Typed preview payload required.');
    const motions = value.motions ?? currentPayload?.motions ?? [];
    compileMotions(motions); phase(value.previewPhase01 ?? previewPhase01);
    if (currentPayload) renderPayload({ ...currentPayload, motions, previewPhase01: value.previewPhase01 ?? previewPhase01 }, true);
};
function releaseGeometryPool() {
    sharedGeometry.forEach(g => g.dispose()); sharedGeometry.clear(); geometryEpoch = null;
}
window.clearViewerScene = function (keepGeometry = false) {
    transformControls?.detach(); selectedGizmo = null;
    for (const group of [staticGroup, movingGroup, collisionGroup, pivotsGroup, lightsGroup]) {
        if (!group) continue;
        while (group.children.length) { const child = group.children[0]; group.remove(child); disposeObjectResources(child); }
        group.position.set(0, 0, 0); group.rotation.set(0, 0, 0); group.scale.set(1, 1, 1);
    }
    typedMotions = []; motionGroups = new Map(); currentPayload = null; animTime = 0; lastFrameTime = null;
    if (!keepGeometry) releaseGeometryPool();
};
// Older hosts may call these APIs, but generic/fallback motion is never inferred from them.
window.setMotionPreview = value => { if (value?.motions) window.setTypedMotionPreview(value); };
window.setAnimationAxis = function () {};
window.setAnimationPlaying = playing => { isPlaying = Boolean(playing); };
window.setAnimationSpeed = speed => { animSpeed = nonnegative(Number(speed), 'Playback speed'); };
function applyLayerVisibility() {
    if (!staticGroup) return;
    staticGroup.visible = movingGroup.visible = showMeshes; collisionGroup.visible = showCollision;
    pivotsGroup.visible = showPivots; lightsGroup.visible = showLights;
}
window.toggleLayer = function (name) {
    let value;
    switch (name) {
        case 'meshes': value = showMeshes = !showMeshes; break;
        case 'collision': value = showCollision = !showCollision; break;
        case 'pivots': value = showPivots = !showPivots; break;
        case 'lights': value = showLights = !showLights; break;
        default: throw new Error('Unknown scene layer.');
    }
    applyLayerVisibility(); return value;
};
function applySceneFilter() {
    for (const group of [staticGroup, movingGroup]) group?.traverse(child => {
        if (!child.isMesh) return;
        child.visible = sceneFilter.materialIndex === null && sceneFilter.materialPath === null && sceneFilter.lodMask === null || child.userData.mappings.some(map =>
            (sceneFilter.materialIndex === null || map.materialIndex === sceneFilter.materialIndex)
            && (sceneFilter.materialPath === null || map.materialPath === sceneFilter.materialPath)
            && (sceneFilter.lodMask === null || map.lodMask !== null && (map.lodMask & sceneFilter.lodMask) !== 0));
    });
}
window.setSceneFilter = function (value) {
    const filter = mapping(value ?? {}); sceneFilter = filter; applySceneFilter();
};
// Light realtime updates cover color/intensity/radius only: position is display-only
// (owner transform is not serialized), so this API takes no coordinates.
window.updateLightRealtime = function (index, color, intensity, radius) {
    const group = lightsGroup?.children.find(child => child.userData.index === index); if (!group) return;
    if (!Number.isInteger(color) || color < 0 || color > 0xffffff) throw new Error('Light color must be RGB24.');
    nonnegative(intensity, 'Light intensity'); nonnegative(radius, 'Light radius');
    const point = group.getObjectByName('realLight'); point.color.setHex(color); point.intensity = intensity; point.distance = radius;
    group.getObjectByName('bulb').material.color.setHex(color);
    const wire = group.getObjectByName('radiusWire'); wire.material.color.setHex(color); wire.scale.setScalar(radius);
};
function updateGizmo(group, index, x, y, z) {
    [x, y, z].forEach(v => finite(v, 'Gizmo position')); group?.children.find(child => child.userData.index === index)?.position.set(x, y, z);
}
window.updatePivotRealtime = (index, x, y, z) => updateGizmo(pivotsGroup, index, x, y, z);
window.toggleWireframe = function () {
    isWireframe = !isWireframe;
    for (const group of [staticGroup, movingGroup]) group?.traverse(child => { if (child.isMesh) child.material.wireframe = isWireframe; });
};
window.resetCamera = () => controls?.reset();
window.downloadObjFile = function (filename, content) {
    const url = URL.createObjectURL(new Blob([content], { type: 'text/plain' })), link = document.createElement('a');
    link.href = url; link.download = filename; link.click(); URL.revokeObjectURL(url);
};
window.downloadBinaryGbx = function (filename, base64Data) {
    const link = document.createElement('a'); link.download = filename; link.href = 'data:application/octet-stream;base64,' + base64Data; link.click();
};
