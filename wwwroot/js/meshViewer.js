let scene, camera, renderer, controls, transformControls, gridHelper;
let staticGroup, movingGroup, collisionGroup, pivotsGroup, lightsGroup, waypointsGroup, flagAnchorGroup;
let dotNetHelper = null, selectedGizmo = null;
let isWireframe = false, isPlaying = true, animSpeed = 1, clothAnimationEnabled = true;
let animTime = 0, lastFrameTime = null, animationFrameId = null, resizeHandler = null;
let resizeObserver = null;
let viewKeyHandler = null;
let activeStudioView = 'perspective';
let showMeshes = true, showCollision = false, showPivots = true, showLights = true, showWaypoints = true;
let activeConstraintIndex = 0, activeMeshHelper = null;
let selectedMeshPath = null, selectedMeshObject = null, selectedMeshHelper = null;
let flagAnchorObject = null, pendingFlagAnchor = null;
let viewportSelectionQueue = Promise.resolve();
let typedMotions = [], typedGroupMotions = [], motionGroups = new Map(), previewPhase01 = 0, currentPayload = null;
let groupMotionContainers = new Map();
let selectedCompositeSources = new Set(), compositeGroupBySource = new Map(), compositeLeaderSource = null;
let hasAutoFramedScene = false;
let compositeSelectionHelpers = [], compositeGroupHelpers = [];
let drawPathEnabled = false, drawPathPlaneY = 0, drawPathPoints = [];
let drawPathLine = null, drawPathPointMarkers = [];
let legacyMotion = null;
let geometryEpoch = null, sharedGeometry = new Map();
const pooledGeometry = new WeakSet();
let sceneFilter = { materialIndex: null, materialPath: null, lodMask: null };
let textureDirectory = null, textureFiles = new Map(), textureManifest = null, textureRenderVersion = 0, lastTextureMatches = null;
const compositeGroupPalette = [0xf43f5e, 0xf59e0b, 0x10b981, 0x22d3ee, 0x6366f1, 0xa855f7, 0xe879f9, 0x84cc16];

function normalizedPath(value) {
    return value.replaceAll('\\', '/').toLowerCase();
}
function isPreviewTexture(name) {
    return /\.(dds|png|jpe?g|webp|tga)$/i.test(name);
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
        textureManifest = await readTextureManifest(directory);
        const selection = { name: directory.name, fileCount: textureFiles.size };
        if (textureManifest) {
            const stats = textureManifest.stats ?? {};
            selection.manifestMaterialCount = stats.materialEntries ?? stats.MaterialEntries ?? 0;
            selection.manifestTextureCount = stats.textureFiles ?? stats.TextureFiles ?? 0;
        }
        return selection;
    } catch (error) {
        textureFiles = previousFiles;
        throw error;
    }
};
async function readTextureManifest(directory) {
    try {
        if (typeof directory.getFileHandle !== 'function') return null;
        const handle = await directory.getFileHandle('manifest.json');
        const file = await handle.getFile();
        const manifest = JSON.parse(await file.text());
        if (manifest?.version !== 1 || !Array.isArray(manifest.materials)) return null;
        return manifest;
    } catch {
        // A normal user-selected texture folder does not need a manifest.
        // Filename matching remains the compatible fallback.
        return null;
    }
}
function materialTokens(material) {
    const sources = [material.gameMaterialLink, material.gameMaterialName].filter(Boolean);
    const tokens = [];
    const add = value => {
        const token = value.replace(/_asset(?:\.\d+)?$/i, '').toLowerCase();
        if (token && !tokens.includes(token)) tokens.push(token);
    };
    for (const source of sources) {
        const last = (source.replaceAll('\\', '/').split('/').pop() || '').replace(/\.material(?:\.gbx)?$/i, '');
        add(last);
        const withoutTm = last.replace(/^tm_/i, '').replace(/_asset(?:\.\d+)?$/i, '');
        add(withoutTm);
        const parts = withoutTm.split('_').filter(Boolean);
        for (let i = 1; i < parts.length; i++) add(parts.slice(i).join('_'));
    }
    return tokens;
}
function materialEnvironment(material) {
    const source = material.gameMaterialLink || '';
    if (/^skins[/\\]/i.test(source)) return normalizedPath(source).split('/').slice(0, -1).join('/');
    return source.replaceAll('/', '\\').split('\\')[0]?.toLowerCase() || '';
}
function materialTextureVariants(token) {
    const variants = [{ token, tails: [] }];
    const add = (value, tails = []) => {
        if (!value || variants.some(entry => entry.token === value && entry.tails.join('|') === tails.join('|'))) return;
        variants.push({ token: value, tails });
    };
    const withoutUv = token.replace(/uv$/i, '');
    if (withoutUv !== token) add(withoutUv);
    const withoutOff = token.replace(/off$/i, '');
    if (withoutOff !== token) add(withoutOff);
    const itemLamp = token.replace(/^itemlamp(?:light)([abc])$/i, 'itemlamp$1');
    if (itemLamp !== token) add(itemLamp);
    const obstacle = token.match(/^(.*?)(?:discontinuous)?level([0-2])$/i);
    if (obstacle) {
        const base = obstacle[1];
        const level = obstacle[2];
        add(base, [`_discontinuouslevel${level}`, `_level${level}`]);
    }
    const paint = token.replace(/^decalpaint2?/i, 'decal');
    if (paint !== token) add(paint);
    return variants;
}
function materialToken(material) {
    return materialTokens(material)[0] ?? '';
}
const pbrTextureRoles = {
    // Some Blendermania/game glass materials have no _D map; _T is their
    // authored glass-colour texture and is the best neutral PBR base fallback.
    base: ['d', 'b', 'diffuse', 'albedo', 'color', 't', ''],
    height: ['h', 'height'],
    normal: ['n', 'normal'],
    surface: ['r', 'roughness', 'metallic'],
    emissive: ['i', 'illum', 'emissive'],
    light: ['l', 'light'],
    overlay: ['o', 'overlay'],
    mask: ['m', 'mask'],
    large: ['x2'],
    hueMask: ['huemask', 'huemask2', 'hueshiftmask']
};
function comparePaths(first, second) {
    // Codepoint ordering (not localeCompare) keeps matching identical across
    // browsers, locales and reloads.
    return first < second ? -1 : first > second ? 1 : 0;
}
function textureCandidates(token, suffixes, tails = [], environment = '') {
    if (!token) return [];
    const skinRoot = environment.startsWith('skins/') ? environment.replace(/\/(?:standard|male|female)$/, '') : null;
    const candidates = [...textureFiles.entries()].filter(([path]) => {
        if (skinRoot && !path.startsWith(skinRoot + '/')) return false;
        if (skinRoot && ![environment, skinRoot, skinRoot + '/common'].includes(path.slice(0, path.lastIndexOf('/')))) return false;
        const filename = path.split('/').pop(), extension = filename.lastIndexOf('.');
        if (!isPreviewTexture(filename) || extension < 1) return false;
        const stem = filename.slice(0, extension);
        return suffixes.some(suffix => {
            const base = suffix ? `${token}_${suffix}` : token;
            // A variant such as ItemObstaclePusherLevel2 must not fall back to
            // the generic ItemObstaclePusher_D file when the level-specific
            // texture exists. Otherwise Level1 and Level2 collapse onto the
            // same candidate set and are incorrectly marked ambiguous.
            return tails.length
                ? tails.some(tail => stem === `${base}${tail}`)
                : stem === base;
        });
    });
    candidates.sort(([first], [second]) => {
        const rank = path => {
            const filename = path.split('/').pop(), stem = filename.slice(0, filename.lastIndexOf('.'));
            const suffix = stem === token ? '' : stem.slice(token.length + 1);
            const roleRank = suffixes.indexOf(suffix);
            const environmentRank = environment && ('/' + path + '/').includes('/' + environment + '/') ? 0 : skinRoot && path.startsWith(skinRoot + '/common/') ? 1 : 2;
            return environmentRank * 100 + roleRank;
        };
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
            identities.set(identity, { tokens: materialTokens(material), environment: materialEnvironment(material), direct: material.texturePaths,
                manifest: materialManifestEntry(material), token: materialToken(material), key: null, file: null,
                handle: null, status: 'absent' });
    }
    const shared = new Map();
    for (const [identity, match] of identities) {
        if (match.direct && Object.keys(match.direct).length) {
            const textures = {};
            for (const [slot, source] of Object.entries(match.direct)) {
                const slotName = slot.toLowerCase();
                const role = ({ diffuse: 'base', basetexture: 'base', basecolor: 'base', normal: 'normal', normalmap: 'normal', specular: 'surface', roughness: 'surface', selfillum: 'emissive', illum: 'emissive' })[slotName]
                    ?? Object.entries(pbrTextureRoles).find(([, suffixes]) => suffixes.includes(slotName))?.[0];
                if (!role) continue;
                const key = normalizedPath(source).replace(/^.*?gamedata\//, '');
                const found = [...textureFiles].find(([path]) => path === key || path.endsWith('/' + key) || key.endsWith('/' + path));
                if (found) textures[role] = { file: found[0], handle: found[1] };
            }
            if (Object.values(textures).some(Boolean)) {
                match.textures = textures; match.status = 'matched'; match.fromManifest = true;
                match.colorable = /^custom/i.test(match.token) || /material[_/\\]blockcustom/i.test(identity);
                match.targetColor = match.manifest?.targetColor ?? null;
                match.file = (textures.base ?? Object.values(textures)[0]).file;
                match.handle = (textures.base ?? Object.values(textures)[0]).handle;
                continue;
            }
        }
        const manifestTexturesForMaterial = manifestTextures(match.manifest);
        if (manifestTexturesForMaterial) {
            match.key = Object.values(manifestTexturesForMaterial).flatMap(texture => texture?.file ?? []).join('\n');
            const primary = manifestTexturesForMaterial.base ?? Object.values(manifestTexturesForMaterial).find(Boolean);
            match.token = match.manifest.token || materialToken({ gameMaterialLink: match.manifest.link, gameMaterialName: '' });
            match.file = primary.file; match.handle = primary.handle;
            match.textures = manifestTexturesForMaterial;
            // TargetColor is also present on ordinary hue-mask materials. Only
            // the dedicated custom-material family uses the green base-color
            // marker that this preview shader converts to a color mask.
            match.colorable = /^custom/i.test(match.token) || /material[_/\\]blockcustom/i.test(identity);
            match.targetColor = match.manifest.targetColor ?? null;
            match.status = 'matched';
            match.fromManifest = true;
            continue;
        }
        for (const token of match.tokens) {
            if (match.status === 'matched') break;
            for (const variant of materialTextureVariants(token)) {
                const textures = Object.fromEntries(Object.entries(pbrTextureRoles).map(([role, suffixes]) => {
                    const [file, handle] = textureCandidates(variant.token, suffixes, variant.tails, match.environment)[0] ?? [];
                    return [role, file ? { file, handle } : null];
                }));
                if (!Object.values(textures).some(Boolean)) continue;
                match.token = variant.token;
                match.key = Object.values(textures).flatMap(texture => texture?.file ?? []).join('\n');
                const primary = textures.base ?? Object.values(textures).find(Boolean);
                match.file = primary.file; match.handle = primary.handle; match.textures = textures;
                match.colorable = /^custom/i.test(match.token) || /material[_/\\]blockcustom/i.test(identity);
                match.targetColor = null;
                match.status = 'matched';
                if (identity.toLowerCase().startsWith('skins/')) match.fromManifest = true;
                shared.set(match.key, (shared.get(match.key) ?? new Set()).add(identity));
                break;
            }
        }
    }
    for (const match of identities.values())
        if (!match.fromManifest && match.key != null && shared.get(match.key).size > 1) {
            match.status = 'ambiguous'; match.file = match.handle = null; match.textures = null;
        }
    const matches = new Map();
    for (const material of materials) {
        if (!material?.path) continue;
        const match = identities.get(material.gameMaterialLink || material.gameMaterialName || '');
            matches.set(material.path, { token: match.token, status: match.status, file: match.file, handle: match.handle,
                color: material.color ?? null, colorable: Boolean(match.colorable), targetColor: match.targetColor ?? null,
                textures: match.textures ?? null });
    }
    return matches;
}
let ddsLoaderPromise = null;
let tgaLoaderPromise = null;
function ensureDdsLoader() {
    if (typeof THREE.DDSLoader === 'function') return Promise.resolve();
    // three r128 ships DDSLoader as a separate examples module; fetch it once,
    // on demand, pinned to the same revision as the page's three.js build.
    ddsLoaderPromise ??= new Promise((resolve, reject) => {
        const script = document.createElement('script');
        script.src = 'vendor/three/DDSLoader.js';
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
    const trackmaniaDds = decodeTrackmaniaBcDds(buffer);
    if (trackmaniaDds) return makeRgbaDdsTexture(trackmaniaDds);
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
function ensureTgaLoader() {
    if (typeof THREE.TGALoader === 'function') return Promise.resolve();
    tgaLoaderPromise ??= new Promise((resolve, reject) => {
        const script = document.createElement('script');
        script.src = 'vendor/three/TGALoader.js';
        script.onload = () => resolve();
        script.onerror = () => { tgaLoaderPromise = null; script.remove(); reject(new Error('The TGA preview loader could not be downloaded.')); };
        document.head.appendChild(script);
    });
    return tgaLoaderPromise;
}
async function loadTgaTexture(file, path) {
    await ensureTgaLoader();
    const parsed = new THREE.TGALoader().parse(await file.arrayBuffer());
    if (!parsed?.data || !parsed.width || !parsed.height)
        throw new Error(`${path} uses an unsupported TGA layout.`);
    const texture = new THREE.DataTexture(parsed.data, parsed.width, parsed.height, THREE.RGBAFormat, THREE.UnsignedByteType);
    texture.flipY = parsed.flipY ?? false;
    texture.minFilter = THREE.LinearFilter;
    texture.magFilter = THREE.LinearFilter;
    texture.generateMipmaps = false;
    texture.needsUpdate = true;
    return texture;
}
function normalizedMaterialKey(value) {
    return normalizedPath(value || '').replace(/^\/+|\/+$/g, '');
}
function materialManifestEntry(material) {
    if (!textureManifest?.materials?.length) return null;
    const link = material.gameMaterialLink || '';
    const name = material.gameMaterialName || material.name || '';
    const linkKey = normalizedMaterialKey(link);
    const exact = textureManifest.materials.find(entry => normalizedMaterialKey(entry.link) === linkKey && linkKey);
    if (exact) return exact;
    const keys = new Set([link, name]);
    const last = value => normalizedMaterialKey(value).split('/').pop() || '';
    if (link) keys.add(last(link));
    if (name) keys.add(last(name));
    const wanted = [...keys].map(normalizedMaterialKey).filter(Boolean);
    let candidates = textureManifest.materials.filter(entry =>
        (entry.aliases ?? []).some(alias => wanted.includes(normalizedMaterialKey(alias))));
    const environment = materialEnvironment(material);
    if (environment) {
        const inEnvironment = candidates.filter(entry => (entry.environment || '').toLowerCase() === environment);
        if (inEnvironment.length) candidates = inEnvironment;
    }
    return candidates.length === 1 ? candidates[0] : null;
}
function manifestTextures(entry) {
    if (!entry?.textures) return null;
    const textures = Object.fromEntries(Object.keys(pbrTextureRoles).map(role => {
        const file = entry.textures[role];
        const handle = file ? textureFiles.get(normalizedPath(file)) : null;
        return [role, file && handle ? { file, handle } : null];
    }));
    return Object.values(textures).some(Boolean) ? textures : null;
}
function decodeBc4Block(bytes, offset) {
    const first = bytes[offset], second = bytes[offset + 1];
    const palette = [first, second];
    if (first > second) {
        for (let i = 1; i <= 6; i++) palette.push(Math.round(((7 - i) * first + i * second) / 7));
    } else {
        for (let i = 1; i <= 4; i++) palette.push(Math.round(((5 - i) * first + i * second) / 5));
        palette.push(0, 255);
    }
    // The BC4 index field is 48 little-endian bits. It is below Number's
    // exact-integer limit, so arithmetic avoids JavaScript's 32-bit shifts.
    let indices = 0;
    for (let i = 0; i < 6; i++) indices += bytes[offset + 2 + i] * 2 ** (8 * i);
    const values = new Uint8Array(16);
    for (let i = 0; i < 16; i++) values[i] = palette[Math.floor(indices / 2 ** (3 * i)) % 8];
    return values;
}
function decodeTrackmaniaBcDds(buffer) {
    if (buffer.byteLength < 128) return null;
    const bytes = new Uint8Array(buffer), view = new DataView(buffer);
    const fourCC = String.fromCharCode(bytes[84], bytes[85], bytes[86], bytes[87]);
    const channels = fourCC === 'ATI1' ? 1 : fourCC === 'ATI2' ? 2 : 0;
    if (!channels) return null;
    const width = view.getUint32(16, true), height = view.getUint32(12, true);
    const mipmapCount = Math.max(1, Math.min(16, view.getUint32(28, true) || 1));
    if (!width || !height) throw new Error(`${fourCC} texture has invalid dimensions.`);
    let offset = view.getUint32(4, true) + 4;
    const mipmaps = [];
    for (let level = 0; level < mipmapCount; level++) {
        const mipWidth = Math.max(1, width >> level), mipHeight = Math.max(1, height >> level);
        const blocksWide = Math.ceil(mipWidth / 4), blocksHigh = Math.ceil(mipHeight / 4);
        const blockBytes = channels === 2 ? 16 : 8, dataLength = blocksWide * blocksHigh * blockBytes;
        if (offset + dataLength > buffer.byteLength) throw new Error(`${fourCC} texture has truncated mip data.`);
        const data = new Uint8Array(mipWidth * mipHeight * 4);
        for (let blockY = 0; blockY < blocksHigh; blockY++) {
            for (let blockX = 0; blockX < blocksWide; blockX++) {
                const blockOffset = offset + (blockY * blocksWide + blockX) * blockBytes;
                const red = decodeBc4Block(bytes, blockOffset);
                const green = channels === 2 ? decodeBc4Block(bytes, blockOffset + 8) : red;
                for (let y = 0; y < 4; y++) {
                    for (let x = 0; x < 4; x++) {
                        const px = blockX * 4 + x, py = blockY * 4 + y;
                        if (px >= mipWidth || py >= mipHeight) continue;
                        const sourceIndex = y * 4 + x, targetIndex = (py * mipWidth + px) * 4;
                        const r = red[sourceIndex], g = green[sourceIndex];
                        // ATI2/BC5 is the two-channel X/Y form used by Trackmania
                        // normal and packed surface maps. Reconstruct Z so a decoded
                        // normal map behaves like the original three-channel input.
                        const nx = r / 127.5 - 1, ny = g / 127.5 - 1;
                        const nz = channels === 2 ? Math.sqrt(Math.max(0, 1 - nx * nx - ny * ny)) : g / 255;
                        data[targetIndex] = r;
                        data[targetIndex + 1] = g;
                        data[targetIndex + 2] = Math.round((nz * .5 + .5) * 255);
                        data[targetIndex + 3] = 255;
                    }
                }
            }
        }
        mipmaps.push({ data, width: mipWidth, height: mipHeight });
        offset += dataLength;
    }
    return { width, height, mipmaps };
}
function makeRgbaDdsTexture(dds) {
    const mip = dds.mipmaps[0];
    const texture = new THREE.DataTexture(mip.data, mip.width, mip.height, THREE.RGBAFormat, THREE.UnsignedByteType);
    texture.flipY = false;
    texture.minFilter = THREE.LinearFilter;
    texture.magFilter = THREE.LinearFilter;
    texture.generateMipmaps = false;
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
    return handle.getFile().then(file => /\.dds$/i.test(path)
        ? loadDdsTexture(file, path)
        : /\.tga$/i.test(path) ? loadTgaTexture(file, path) : loadImageTexture(file));
}
function configureTexture(texture, color) {
    texture.wrapS = texture.wrapT = THREE.RepeatWrapping;
    if (color) texture.encoding = THREE.sRGBEncoding;
    texture.needsUpdate = true;
    return texture;
}
function configureAnimatedTexture(texture) {
    texture.wrapS = THREE.RepeatWrapping;
    texture.wrapT = THREE.RepeatWrapping;
    texture.needsUpdate = true;
    return texture;
}
function applyTrackmaniaPbr(material, textures, color, colorable = false, targetColor = null) {
    const hasItemColor = Array.isArray(color) && color.length >= 3 && color.slice(0, 3).every(value => Number.isFinite(value));
    const hasTargetColor = Array.isArray(targetColor) && targetColor.length >= 3 && targetColor.slice(0, 3).every(value => Number.isFinite(value));
    if (colorable && textures.base) {
        // Trackmania custom maps use green as a visible color-mask marker. The
        // marker is not the material's final color: reduce it to a grayscale
        // mask in the shader and let the authored item color tint that mask.
        const values = hasItemColor ? color.slice(0, 3).map(value => Math.max(0, Math.min(255, value)) / 255)
            : hasTargetColor ? targetColor.slice(0, 3).map(value => Math.max(0, Math.min(1, value)))
            : [1, 1, 1];
        material.color.setRGB(values[0], values[1], values[2]);
    } else if (textures.base) {
        // Ordinary PBR maps already contain their final RGB values.
        material.color.setHex(0xffffff);
    } else if (hasItemColor) {
        material.color.setRGB(Math.max(0, Math.min(255, color[0])) / 255, Math.max(0, Math.min(255, color[1])) / 255, Math.max(0, Math.min(255, color[2])) / 255);
    }
    material.map = textures.base ? configureTexture(textures.base, true) : null;
    material.bumpMap = textures.height ? configureTexture(textures.height, false) : null;
    material.bumpScale = textures.height ? .04 : 0;
    material.normalMap = textures.normal ? configureTexture(textures.normal, false) : null;
    material.roughnessMap = textures.surface ? configureTexture(textures.surface, false) : null;
    material.metalnessMap = textures.surface ? configureTexture(textures.surface, false) : null;
    const emissive = textures.emissive ?? textures.light ?? null;
    material.emissiveMap = emissive ? configureTexture(emissive, true) : null;
    material.emissive.setHex(emissive ? 0xffffff : 0x000000);
    // Trackmania's _R map uses red for roughness and green for metallic,
    // whereas Three.js MeshStandardMaterial normally reads green and blue.
    material.onBeforeCompile = shader => {
        if (colorable && textures.base)
            shader.fragmentShader = shader.fragmentShader.replace('diffuseColor *= texelColor;', 'diffuseColor *= vec4(vec3(texelColor.g), texelColor.a);');
        shader.fragmentShader = shader.fragmentShader
            .replace('roughnessFactor *= texelRoughness.g;', 'roughnessFactor *= texelRoughness.r;')
            .replace('metalnessFactor *= texelMetalness.b;', 'metalnessFactor *= texelMetalness.g;');
    };
    material.needsUpdate = true;
}
async function applyLocalTextures(data, renderVersion) {
    if (!textureDirectory || !Array.isArray(data.materials)) return;
    const matches = matchTextureFiles(data.materials);
    lastTextureMatches = [...matches.entries()]
        .map(([materialPath, match]) => ({ materialPath, token: match.token, status: match.status, file: match.file,
            textures: Object.fromEntries(Object.entries(match.textures ?? {}).map(([role, texture]) => [role, texture?.file ?? null])) }))
        .sort((first, second) => comparePaths(first.materialPath, second.materialPath));
    const requested = new Map();
    for (const group of [staticGroup, movingGroup]) group?.traverse(mesh => {
        if (!mesh.isMesh || !Array.isArray(mesh.userData.mappings)) return;
        const mapping = mesh.userData.mappings.find(candidate => matches.has(candidate.materialPath));
        if (!mapping || requested.has(mapping.materialPath)) return;
        const match = matches.get(mapping.materialPath);
        if (match.status !== 'matched') return;
        requested.set(mapping.materialPath, Promise.all(Object.entries(match.textures).filter(([, texture]) => texture).map(async ([role, texture]) => {
            try {
                return [role, await loadTexture(texture.handle, texture.file)];
            } catch (error) {
                // Keep a usable diffuse map when a normal, roughness or emissive
                // companion is malformed/unsupported. One bad channel must not
                // turn the entire material neutral or black.
                console.warn(`Could not load ${role} texture ${texture.file} for ${mapping.materialPath}.`, error);
                return null;
            }
        })));
    });
    for (const [materialPath, texturePromise] of requested) {
        try {
            const textures = Object.fromEntries((await texturePromise).filter(entry => entry));
            if (renderVersion !== textureRenderVersion) { Object.values(textures).forEach(texture => texture.dispose()); continue; }
            const match = matches.get(materialPath);
            for (const group of [staticGroup, movingGroup]) group?.traverse(mesh => {
                if (!mesh.isMesh || !mesh.userData.mappings?.some(mapping => mapping.materialPath === materialPath)) return;
                applyTrackmaniaPbr(mesh.material, textures, match.color, match.colorable, match.targetColor);
                if (/glass/i.test(match.token)) { mesh.material.transparent = true; mesh.material.depthWrite = false; }
                const animated = /(?:water|waterfall|glasswaterwall)/i.test(match.token);
                mesh.userData.textureAnimation = animated;
                if (animated)
                    for (const texture of [mesh.material.map, mesh.material.normalMap, mesh.material.roughnessMap, mesh.material.emissiveMap])
                        if (texture) configureAnimatedTexture(texture);
            });
        } catch (error) {
            console.warn(`Could not load local texture for ${materialPath}.`, error);
        }
    }
}
window.getStudioTexturePreviewInfo = function () {
    return { directory: textureDirectory?.name ?? null, fileCount: textureFiles.size,
        manifest: textureManifest?.stats ?? null, renderVersion: textureRenderVersion, matches: lastTextureMatches ?? [] };
}

window.getStudioViewerDebugInfo = function () {
    let meshes = 0;
    const colors = new Set();
    const meshBounds = new THREE.Box3();
    for (const group of [staticGroup, movingGroup]) group?.traverse(node => {
        if (!node.isMesh) return;
        meshes++;
        meshBounds.expandByObject(node);
        for (const material of (Array.isArray(node.material) ? node.material : [node.material]))
            if (material?.color) colors.add(material.color.getHexString());
    });
    const fxEmitters = [];
    pivotsGroup?.traverse(node => {
        if (node.userData?.type === 'fx-emitter')
            fxEmitters.push({ index: node.userData.index, itemPosition: node.getWorldPosition(new THREE.Vector3()).toArray(),
                editable: node.userData.editable });
    });
    return {
        initialized: Boolean(renderer?.domElement.isConnected), meshes,
        meshBounds: meshBounds.isEmpty() ? null : { min: meshBounds.min.toArray(), max: meshBounds.max.toArray() },
        fxEmitters,
        materialColors: [...colors].slice(0, 128),
        cameraPosition: camera?.position.toArray() ?? null,
        cameraTarget: controls?.target.toArray() ?? null,
        playing: isPlaying, wireframe: isWireframe,
        layers: { meshes: showMeshes, collision: showCollision, pivots: showPivots, lights: showLights }
    };
};

let studioHelperMode = false, studioHelperTooltip = null, studioHelperBound = false, studioHelperTarget = null;
function helperDescription(element) {
    return window.studioHelp?.describe(element) || '';
}
function helperTarget(element) {
    for (let target = element; target instanceof Element && target !== document.body; target = target.parentElement) {
        if (helperDescription(target)) return target;
    }
    return null;
}
function hideStudioHelper() {
    studioHelperTooltip?.classList.remove('studio-helper-tooltip-visible');
    if (studioHelperTarget) {
        const ids = (studioHelperTarget.getAttribute('aria-describedby') || '').split(/\s+/).filter(id => id && id !== 'studio-helper-tooltip');
        if (ids.length) studioHelperTarget.setAttribute('aria-describedby', ids.join(' '));
        else studioHelperTarget.removeAttribute('aria-describedby');
    }
    studioHelperTarget = null;
}
function showStudioHelper(element) {
    if (!studioHelperMode || !element) { hideStudioHelper(); return; }
    const text = helperDescription(element);
    if (!text) { hideStudioHelper(); return; }
    hideStudioHelper();
    studioHelperTooltip ??= Object.assign(document.createElement('div'), { id: 'studio-helper-tooltip', className: 'studio-helper-tooltip' });
    studioHelperTooltip.setAttribute('role', 'tooltip');
    studioHelperTooltip.textContent = text;
    if (!studioHelperTooltip.isConnected) document.body.appendChild(studioHelperTooltip);
    studioHelperTarget = element;
    const describedBy = (element.getAttribute('aria-describedby') || '').trim();
    element.setAttribute('aria-describedby', `${describedBy} studio-helper-tooltip`.trim());
    const rect = element.getBoundingClientRect();
    const width = Math.min(360, window.innerWidth - 24);
    studioHelperTooltip.style.width = `${width}px`;
    studioHelperTooltip.style.left = `${Math.max(12, Math.min(rect.left, window.innerWidth - width - 12))}px`;
    studioHelperTooltip.classList.add('studio-helper-tooltip-visible');
    const height = studioHelperTooltip.offsetHeight;
    const below = rect.bottom + 8;
    studioHelperTooltip.style.top = `${Math.max(12, below + height <= innerHeight - 12 ? below : rect.top - height - 8)}px`;
}
window.setStudioHelperMode = function (enabled) {
    studioHelperMode = Boolean(enabled);
    window.studioHelp?.bind();
    document.body.classList.toggle('studio-helper-mode', studioHelperMode);
    if (!studioHelperBound) {
        studioHelperBound = true;
        document.addEventListener('pointerover', event => {
            if (studioHelperMode) showStudioHelper(helperTarget(event.target));
        });
        document.addEventListener('pointerout', event => {
            if (helperTarget(event.target) !== helperTarget(event.relatedTarget)) hideStudioHelper();
        });
        document.addEventListener('focusin', event => {
            if (studioHelperMode) showStudioHelper(helperTarget(event.target));
        });
        document.addEventListener('focusout', hideStudioHelper);
        document.addEventListener('keydown', event => { if (event.key === 'Escape') hideStudioHelper(); });
        document.addEventListener('scroll', hideStudioHelper, true);
    }
    if (!studioHelperMode) hideStudioHelper();
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
function vec3(values, name) {
    buffer(values, name, 3);
    return new THREE.Vector3(values[0], values[1], values[2]);
}
function timeline(source) {
    if (!source || typeof source.isDuration !== 'boolean' || !Array.isArray(source.keys) || source.keys.length > 10)
        throw new Error('Unsupported or absent timeline: expected a timing mode and at most ten keys.');
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
        const pos1 = source.pos1 ? vec3(source.pos1, 'Constraint pos1') : new THREE.Vector3(0, 0, 0);
        const pos2 = source.pos2 ? vec3(source.pos2, 'Constraint pos2') : new THREE.Vector3(0, 0, 0);
        if (source.parentPath == null && !parentRest.equals(new THREE.Matrix4())) throw new Error('World parent requires identity parent rest.');
        paths.add(source.path);
        byChild.set(source.childPath, { ...source, constraintIndex: source.constraintIndex, fields: { ...fields }, childRest, parentRest,
            pos1, pos2,
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
function compileGroupMotions(sources) {
    if (!Array.isArray(sources)) throw new Error('Group motions must be an array.');
    const compiled = [];
    for (const source of sources) {
        if (!source || !Number.isInteger(source.groupId) || source.groupId < 0) continue;
        const fields = source.fields;
        if (!fields || ![0, 1, 2].includes(fields.translationAxis) || ![0, 1, 2].includes(fields.rotationAxis)) continue;
        for (const key of ['translationMin', 'translationMax', 'angleMinDegrees', 'angleMaxDegrees']) finite(fields[key], key);
        const pivotX = Number.isFinite(source.pivotX) ? Number(source.pivotX) : 0;
        const pivotY = Number.isFinite(source.pivotY) ? Number(source.pivotY) : 0;
        const pivotZ = Number.isFinite(source.pivotZ) ? Number(source.pivotZ) : 0;
        compiled.push({
            groupId: source.groupId,
            pivotX,
            pivotY,
            pivotZ,
            fields: { ...fields },
            translation: timeline(fields.translation),
            rotation: timeline(fields.rotation)
        });
    }
    return compiled;
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
        const anchorSignal = new THREE.Matrix4()
            .makeTranslation(motion.pos1.x, motion.pos1.y, motion.pos1.z)
            .multiply(signal)
            .multiply(new THREE.Matrix4().makeTranslation(-motion.pos2.x, -motion.pos2.y, -motion.pos2.z));
        motion.live.copy(motion.parentMotion ? motion.parentMotion.live : motion.parentRest)
            .multiply(motion.inverseParent).multiply(motion.childRest).multiply(anchorSignal);
        for (const group of motionGroups.get(motion.childPath) || []) { group.matrix.copy(motion.live); group.matrixWorldNeedsUpdate = true; }
    }
    for (const motion of typedGroupMotions) {
        const groups = groupMotionContainers.get(motion.groupId) || [];
        if (groups.length === 0) continue;
        const f = motion.fields;
        const distance = sampleTimeline(motion.translation, animTime / 1000, previewPhase01, f.translationMin, f.translationMax);
        const angle = sampleTimeline(motion.rotation, animTime / 1000, previewPhase01, f.angleMinDegrees, f.angleMaxDegrees);
        const axis = new THREE.Vector3().setComponent(f.rotationAxis, 1), translation = new THREE.Vector3().setComponent(f.translationAxis, distance);
        const signal = new THREE.Matrix4().makeTranslation(translation.x, translation.y, translation.z)
            .multiply(new THREE.Matrix4().makeRotationAxis(axis, THREE.MathUtils.degToRad(angle)));
        const pivot = new THREE.Vector3(motion.pivotX, motion.pivotY, motion.pivotZ);
        const pivotSignal = new THREE.Matrix4()
            .makeTranslation(pivot.x, pivot.y, pivot.z)
            .multiply(signal)
            .multiply(new THREE.Matrix4().makeTranslation(-pivot.x, -pivot.y, -pivot.z));
        for (const group of groups) {
            group.matrix.copy(pivotSignal);
            group.matrixWorldNeedsUpdate = true;
        }
    }
}
function compositeGroupColor(groupId) {
    return compositeGroupPalette[Math.abs(groupId) % compositeGroupPalette.length];
}
function clearCompositeHelpers() {
    for (const helper of [...compositeSelectionHelpers, ...compositeGroupHelpers]) {
        scene?.remove(helper);
        helper.geometry?.dispose?.();
        helper.material?.dispose?.();
    }
    compositeSelectionHelpers = [];
    compositeGroupHelpers = [];
}
function applyCompositeVisualState() {
    if (!staticGroup || !movingGroup) return;
    clearCompositeHelpers();
    const groups = [staticGroup, movingGroup];
    const hasSelection = selectedCompositeSources.size > 0;

    for (const group of groups) {
        group.traverse(child => {
            if (!child?.isMesh || !child.material) return;
            const material = child.material;
            const sourceIndex = Number.isInteger(child.userData?.sourceIndex) ? child.userData.sourceIndex : null;
            const groupId = Number.isInteger(child.userData?.groupId) ? child.userData.groupId : null;
            const isSelected = sourceIndex !== null && selectedCompositeSources.has(sourceIndex);

            if (child.userData.__baseColor == null) {
                child.userData.__baseColor = material.color?.getHex?.() ?? 0xb8bdc6;
            }
            if (material.emissive && child.userData.__baseEmissive == null) {
                child.userData.__baseEmissive = material.emissive.getHex();
                child.userData.__baseEmissiveIntensity = material.emissiveIntensity ?? 0;
            }
            if (child.userData.__baseOpacity == null) {
                child.userData.__baseOpacity = material.opacity ?? 1;
                child.userData.__baseTransparent = Boolean(material.transparent);
            }

            const baseColor = new THREE.Color(child.userData.__baseColor);
            const displayColor = baseColor.clone();
            if (groupId !== null) {
                displayColor.lerp(new THREE.Color(compositeGroupColor(groupId)), 0.22);
            }
            if (isSelected) {
                displayColor.lerp(new THREE.Color(0xff2ea6), 0.6);
            }
            material.color?.copy?.(displayColor);

            const baseOpacity = child.userData.__baseOpacity;
            if (hasSelection && sourceIndex !== null && !isSelected) {
                material.transparent = true;
                material.opacity = Math.max(0.18, Math.min(baseOpacity, 0.45));
            } else {
                material.transparent = child.userData.__baseTransparent;
                material.opacity = baseOpacity;
            }

            if (material.emissive) {
                if (isSelected) {
                    material.emissive.setHex(0xff2ea6);
                    material.emissiveIntensity = 0.9;
                } else if (groupId !== null) {
                    material.emissive.setHex(compositeGroupColor(groupId));
                    material.emissiveIntensity = 0.12;
                } else {
                    material.emissive.setHex(child.userData.__baseEmissive ?? 0x000000);
                    material.emissiveIntensity = child.userData.__baseEmissiveIntensity ?? 0;
                }
            }
        });
    }

    const selectedBySource = new Map();
    const groupedByGroupId = new Map();
    for (const group of groups) {
        group.traverse(child => {
            if (!child?.isMesh || !child.visible) return;
            const sourceIndex = Number.isInteger(child.userData?.sourceIndex) ? child.userData.sourceIndex : null;
            const groupId = Number.isInteger(child.userData?.groupId) ? child.userData.groupId : null;
            if (sourceIndex !== null && selectedCompositeSources.has(sourceIndex)) {
                if (!selectedBySource.has(sourceIndex)) selectedBySource.set(sourceIndex, []);
                selectedBySource.get(sourceIndex).push(child);
            }
            if (groupId !== null) {
                if (!groupedByGroupId.has(groupId)) groupedByGroupId.set(groupId, []);
                groupedByGroupId.get(groupId).push(child);
            }
        });
    }

    for (const [groupId, meshes] of groupedByGroupId.entries()) {
        if (!meshes.length) continue;
        const box = new THREE.Box3();
        meshes.forEach(mesh => box.expandByObject(mesh));
        if (box.isEmpty()) continue;
        const helper = new THREE.Box3Helper(box, compositeGroupColor(groupId));
        compositeGroupHelpers.push(helper);
        scene?.add(helper);
    }
    for (const [sourceIndex, meshes] of selectedBySource.entries()) {
        if (!meshes.length) continue;
        const box = new THREE.Box3();
        meshes.forEach(mesh => box.expandByObject(mesh));
        if (box.isEmpty()) continue;
        const helper = new THREE.Box3Helper(box, 0xff2ea6);
        compositeSelectionHelpers.push(helper);
        scene?.add(helper);
    }
}
function clearSelectedMeshHighlight() {
    if (selectedMeshHelper) {
        scene?.remove(selectedMeshHelper);
        selectedMeshHelper.dispose?.();
        selectedMeshHelper = null;
    }
    const groups = [staticGroup, movingGroup, collisionGroup].filter(Boolean);
    for (const group of groups) {
        group.traverse(child => {
            if (!child?.isMesh || !child?.material) return;
            const material = child.material;
            if (material.emissive && typeof child.userData?.__baseEmissive === 'number') {
                material.emissive.setHex(child.userData.__baseEmissive);
            }
            if (typeof child.userData?.__baseEmissiveIntensity === 'number') {
                material.emissiveIntensity = child.userData.__baseEmissiveIntensity;
            }
            if (child.userData) child.userData.__selected = false;
        });
    }
}
function updateSelectedMeshHighlight() {
    clearSelectedMeshHighlight();
    if (!selectedMeshObject || !scene) return;
    selectedMeshHelper = new THREE.BoxHelper(selectedMeshObject, 0xff2ea6);
    scene.add(selectedMeshHelper);
    if (selectedMeshObject.material) {
        const material = selectedMeshObject.material;
        if (material.emissive) {
            selectedMeshObject.userData.__baseEmissive = material.emissive.getHex();
            selectedMeshObject.userData.__baseEmissiveIntensity = material.emissiveIntensity ?? 0;
            material.emissive.setHex(0xff2ea6);
            material.emissiveIntensity = 0.85;
        }
        selectedMeshObject.userData.__selected = true;
    }
}
function findMeshByPath(path) {
    if (!path) return null;
    const groups = [staticGroup, movingGroup, collisionGroup].filter(Boolean);
    for (const group of groups) {
        let found = null;
        group.traverse(child => {
            if (!found && child.isMesh && child.userData?.path === path) found = child;
        });
        if (found) return found;
    }
    return null;
}
function clearDrawPathVisual() {
    if (drawPathLine) {
        waypointsGroup?.remove(drawPathLine);
        drawPathLine.geometry?.dispose?.();
        drawPathLine.material?.dispose?.();
        drawPathLine = null;
    }
    for (const marker of drawPathPointMarkers) {
        waypointsGroup?.remove(marker);
        marker.geometry?.dispose?.();
        marker.material?.dispose?.();
    }
    drawPathPointMarkers = [];
}
function renderDrawPathVisual() {
    clearDrawPathVisual();
    if (!waypointsGroup || drawPathPoints.length === 0) return;
    for (let i = 0; i < drawPathPoints.length; i++) {
        const point = drawPathPoints[i];
        const marker = new THREE.Mesh(
            new THREE.SphereGeometry(i === 0 ? 0.18 : 0.12, 16, 16),
            new THREE.MeshBasicMaterial({ color: i === 0 ? 0x16a34a : 0xf97316 }));
        marker.position.copy(point);
        drawPathPointMarkers.push(marker);
        waypointsGroup.add(marker);
    }
    if (drawPathPoints.length > 1) {
        drawPathLine = new THREE.Line(
            new THREE.BufferGeometry().setFromPoints(drawPathPoints),
            new THREE.LineBasicMaterial({ color: 0xfb7185 }));
        drawPathLine.name = 'drawPathPreview';
        waypointsGroup.add(drawPathLine);
    }
}
function pickDrawPoint(raycaster) {
    const meshGroups = [staticGroup, movingGroup].filter(g => g && g.visible);
    const meshHit = raycaster.intersectObjects(meshGroups.flatMap(g => g.children), true).find(hit => isSelectableMesh(hit.object));
    if (meshHit?.point) return meshHit.point.clone();
    const plane = new THREE.Plane(new THREE.Vector3(0, 1, 0), -drawPathPlaneY);
    const point = new THREE.Vector3();
    return raycaster.ray.intersectPlane(plane, point) ? point : null;
}
function isVisibleInScene(object) {
    for (let current = object; current; current = current.parent) {
        if (!current.visible) return false;
    }
    return true;
}
function isSelectableMesh(object) {
    return Boolean(object?.isMesh && object.userData?.path && isVisibleInScene(object));
}
function ensureFlagAnchorObject() {
    if (!flagAnchorGroup) return null;
    if (!flagAnchorObject) {
        flagAnchorObject = new THREE.Group();
        flagAnchorObject.name = 'flagConversionAnchor';
        const marker = new THREE.Mesh(new THREE.SphereGeometry(.18, 16, 12),
            new THREE.MeshBasicMaterial({ color: 0xfacc15, depthTest: false }));
        marker.renderOrder = 100;
        flagAnchorObject.add(marker);
        const axes = new THREE.AxesHelper(1.1);
        axes.renderOrder = 99;
        flagAnchorObject.add(axes);
        flagAnchorObject.userData = { type: 'flag-anchor', index: 0, editable: true };
        flagAnchorGroup.add(flagAnchorObject);
    }
    return flagAnchorObject;
}
function flagAnchorMotionOwner() {
    if (!selectedMeshObject || !motionGroups?.size) return motionGroups?.values?.().next?.().value?.[0] ?? null;
    for (const pair of motionGroups.values()) {
        const owner = pair?.[0];
        if (!owner) continue;
        for (let current = selectedMeshObject; current; current = current.parent)
            if (current === owner) return owner;
    }
    return null;
}
function syncFlagAnchorObject() {
    if (!flagAnchorObject || !pendingFlagAnchor) return;
    const local = new THREE.Vector3(...pendingFlagAnchor);
    const owner = flagAnchorMotionOwner();
    if (owner) {
        owner.updateMatrixWorld(true);
        flagAnchorObject.position.copy(local).applyMatrix4(owner.matrixWorld);
        flagAnchorObject.quaternion.setFromRotationMatrix(owner.matrixWorld);
    } else {
        flagAnchorObject.position.copy(local);
        flagAnchorObject.quaternion.identity();
    }
}
window.setFlagConversionAnchor = function (x, y, z, visible = true) {
    const values = [Number(x), Number(y), Number(z)];
    if (values.some(value => !Number.isFinite(value))) return;
    pendingFlagAnchor = values;
    const object = ensureFlagAnchorObject();
    if (!object) return;
    syncFlagAnchorObject();
    object.visible = Boolean(visible);
};
function selectMeshObject(mesh, notify = true, additive = false, range = false) {
    selectedMeshObject = mesh ?? null;
    selectedMeshPath = mesh?.userData?.path ?? null;
    updateSelectedMeshHighlight();
    if (!mesh && selectedCompositeSources.size) {
        selectedCompositeSources.clear();
        applyCompositeVisualState();
    }
    if (notify && dotNetHelper) {
        const helper = dotNetHelper, path = selectedMeshPath;
        const sourceIndex = Number.isInteger(mesh?.userData?.sourceIndex) ? mesh.userData.sourceIndex : null;
        // Source switching rebuilds the payload asynchronously. Keep selection and clearing in
        // pointer order so a late source-switch render cannot restore an old selection.
        viewportSelectionQueue = viewportSelectionQueue.catch(() => {}).then(() =>
            helper === dotNetHelper && helper.invokeMethodAsync('OnViewportMeshSelected', path, sourceIndex, Boolean(additive), Boolean(range)));
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
function applyAnimatedMaterialTextures() {
    const phase = ((animTime / 1000) * .035) % 1;
    for (const group of [staticGroup, movingGroup]) group?.traverse(mesh => {
        if (!mesh.isMesh || !mesh.userData.textureAnimation) return;
        for (const texture of [mesh.material.map, mesh.material.normalMap, mesh.material.roughnessMap, mesh.material.emissiveMap]) {
            if (!texture) continue;
            texture.offset.set(phase, (phase * .42) % 1);
        }
    });
}
window.init3DViewer = function (containerId, dotNetRef) {
    const container = document.getElementById(containerId);
    if (!container) return;
    window.dispose3DViewer(); dotNetHelper = dotNetRef; container.innerHTML = '';
    const width = container.clientWidth || 800, height = container.clientHeight || 550;
    scene = new THREE.Scene(); scene.background = new THREE.Color(0x131316);
    camera = new THREE.PerspectiveCamera(45, width / height, .1, 2000); camera.position.set(12, 10, 12);
    renderer = new THREE.WebGLRenderer({ antialias: true }); renderer.setSize(width, height); renderer.outputEncoding = THREE.sRGBEncoding; renderer.shadowMap.enabled = true;
    container.appendChild(renderer.domElement);
    renderer.domElement.tabIndex = 0;
    renderer.domElement.setAttribute('aria-label', '3D item viewport');
    controls = new THREE.OrbitControls(camera, renderer.domElement); controls.enableDamping = true; controls.dampingFactor = .05;
    controls.addEventListener('start', () => {
        if (activeStudioView === 'top' && camera) {
            // Top view needs a Z up-vector to stay square, but orbiting should use
            // the normal world Y-up orientation so the grid never rolls with the camera.
            camera.up.set(0, 1, 0);
            activeStudioView = 'perspective';
        }
    });
    viewKeyHandler = event => {
        if (event.altKey || (event.target instanceof HTMLElement && event.target.closest('input, select, textarea, [contenteditable]:not([contenteditable="false"]), [role="textbox"]'))) return;
        const views = { Numpad7: 'top', Numpad1: 'front', Numpad3: 'right', Numpad5: 'perspective', 'Home:3': 'top', 'End:3': 'front', 'PageDown:3': 'right', 'Clear:3': 'perspective', '7:3': 'top', '1:3': 'front', '3:3': 'right', '5:3': 'perspective' };
        const view = views[event.code] || views[`${event.key}:${event.location}`];
        if (!view) return;
        event.preventDefault();
        window.setStudioView(view, event.ctrlKey || event.metaKey);
    };
    window.addEventListener('keydown', viewKeyHandler, true);
    transformControls = new THREE.TransformControls(camera, renderer.domElement); transformControls.size = .75; scene.add(transformControls);
    transformControls.addEventListener('dragging-changed', event => {
        if (controls) controls.enabled = !event.value;
        if (!event.value && selectedGizmo?.userData.type === 'waypoint' && dotNetHelper) {
            dotNetHelper.invokeMethodAsync('OnWaypointDragEnded', selectedGizmo.userData.constraintIndex);
        }
        if (!event.value && selectedGizmo?.userData?.editable && selectedGizmo?.userData?.type !== 'waypoint' && dotNetHelper) {
            dotNetHelper.invokeMethodAsync('OnGizmoDragEnded', selectedGizmo.userData.type, selectedGizmo.userData.index ?? -1);
        }
    });
    transformControls.addEventListener('change', () => {
        if (selectedGizmo?.userData.editable && transformControls.dragging && dotNetHelper) {
            const d = selectedGizmo.userData, p = selectedGizmo.position;
            if (d.type === 'waypoint') {
                handleWaypointDrag(d.constraintIndex, d.isPointB, p);
            } else if (d.type === 'composite-offset') {
                dotNetHelper.invokeMethodAsync(
                    'OnCompositeOffsetTransformMoved',
                    d.index,
                    p.x, p.y, p.z,
                    THREE.MathUtils.radToDeg(selectedGizmo.rotation.x),
                    THREE.MathUtils.radToDeg(selectedGizmo.rotation.y),
                    THREE.MathUtils.radToDeg(selectedGizmo.rotation.z));
            } else if (d.type === 'composite-group') {
                dotNetHelper.invokeMethodAsync(
                    'OnCompositeGroupTransformMoved',
                    d.index,
                    p.x, p.y, p.z,
                    THREE.MathUtils.radToDeg(selectedGizmo.rotation.x),
                    THREE.MathUtils.radToDeg(selectedGizmo.rotation.y),
                    THREE.MathUtils.radToDeg(selectedGizmo.rotation.z));
            } else if (d.type === 'flag-anchor') {
                const local = p.clone(), owner = flagAnchorMotionOwner();
                if (owner) {
                    owner.updateMatrixWorld(true);
                    local.applyMatrix4(owner.matrixWorld.clone().invert());
                }
                dotNetHelper.invokeMethodAsync('OnFlagAnchorMoved', local.x, local.y, local.z);
            } else if (d.type === 'fx-emitter') {
                dotNetHelper.invokeMethodAsync('OnFxEmitterPointMoved', p.x, p.y, p.z);
            } else {
                dotNetHelper.invokeMethodAsync('OnGizmoMoved', d.type, d.index, p.x, p.y, p.z);
            }
        }
    });
    const raycaster = new THREE.Raycaster(), mouse = new THREE.Vector2();
    renderer.domElement.addEventListener('pointerdown', event => {
        if (event.button !== 0) return;
        renderer.domElement.focus({ preventScroll: true });
        if (transformControls.dragging) return;
        const rect = renderer.domElement.getBoundingClientRect();
        mouse.set((event.clientX - rect.left) / rect.width * 2 - 1, -(event.clientY - rect.top) / rect.height * 2 + 1);
        raycaster.setFromCamera(mouse, camera);
        if (drawPathEnabled) {
            const point = pickDrawPoint(raycaster);
            if (point) {
                drawPathPoints.push(point);
                renderDrawPathVisual();
            }
            return;
        }
        const groups = [pivotsGroup, lightsGroup, waypointsGroup, flagAnchorGroup].filter(g => g && g.visible);
        const hits = raycaster.intersectObjects(groups.flatMap(g => g.children), true).filter(hit => isVisibleInScene(hit.object));
        if (hits.length) {
            let obj = hits[0].object;
            while (obj.parent && !groups.includes(obj.parent)) obj = obj.parent;
            selectGizmo(obj);
            return;
        }
        const meshGroups = [staticGroup, movingGroup].filter(g => g && g.visible);
        const meshHits = raycaster.intersectObjects(meshGroups.flatMap(g => g.children), true)
            .find(hit => isSelectableMesh(hit.object));
        const additive = event.ctrlKey || event.metaKey;
        const range = event.shiftKey;
        if (meshHits?.object?.isMesh) {
            selectMeshObject(meshHits.object, true, additive, range);
            return;
        }
        // Only item groups are selectable; transform-control pickers are not items.
        selectGizmo(null);
        selectMeshObject(null, true, false, false);
    });
    const ambient = new THREE.AmbientLight(0xffffff, .5); ambient.userData.studioDefaultLight = true; scene.add(ambient);
    const light = new THREE.DirectionalLight(0xffffff, .7); light.userData.studioDefaultLight = true; light.position.set(30, 50, 30); scene.add(light);
    const gridGroup = new THREE.Group();
    // Stadium uses 48 Trackmania blocks across the editor floor; each block is 32 m.
    gridGroup.add(new THREE.GridHelper(64, 64, 0x536277, 0x303d4d));
    gridGroup.add(new THREE.GridHelper(512, 64, 0x657b96, 0x34465c));
    gridGroup.add(new THREE.GridHelper(1536, 48, 0x6b8bb5, 0x34465c));
    // Short line segments also render correctly when the camera is inside a
    // large grid (some WebGL drivers clip long lines at the near plane).
    for (const grid of gridGroup.children) {
        const positions = grid.geometry.attributes.position, colors = grid.geometry.attributes.color;
        const points = [], vertexColors = [];
        for (let i = 0; i < positions.count; i += 2) {
            const a = new THREE.Vector3().fromBufferAttribute(positions, i), b = new THREE.Vector3().fromBufferAttribute(positions, i + 1);
            const steps = Math.ceil(a.distanceTo(b) / (a.distanceTo(b) <= 64 ? 16 : 64));
            for (let step = 0; step < steps; step++) {
                points.push(...a.clone().lerp(b, step / steps).toArray(), ...a.clone().lerp(b, (step + 1) / steps).toArray());
                const color = [colors.getX(i), colors.getY(i), colors.getZ(i)]; vertexColors.push(...color, ...color);
            }
        }
        grid.geometry.dispose(); grid.geometry = new THREE.BufferGeometry();
        grid.geometry.setAttribute('position', new THREE.Float32BufferAttribute(points, 3));
        grid.geometry.setAttribute('color', new THREE.Float32BufferAttribute(vertexColors, 3));
    }
    gridHelper = gridGroup; scene.add(gridHelper, new THREE.AxesHelper(4));
    [staticGroup, movingGroup, collisionGroup, pivotsGroup, lightsGroup, waypointsGroup, flagAnchorGroup] = Array.from({ length: 7 }, () => new THREE.Group());
    scene.add(staticGroup, movingGroup, collisionGroup, pivotsGroup, lightsGroup, waypointsGroup, flagAnchorGroup); applyLayerVisibility();
    if (pendingFlagAnchor) window.setFlagConversionAnchor(...pendingFlagAnchor, true);
    function animate(timestamp) {
        if (!renderer) return;
        animationFrameId = requestAnimationFrame(animate);
        if (lastFrameTime !== null && timestamp - lastFrameTime < 1000 / 30) return;
        const delta = lastFrameTime === null ? 0 : Math.min(Math.max(timestamp - lastFrameTime, 0), 50);
        lastFrameTime = timestamp;
        if (isPlaying) animTime += delta * animSpeed;
        applyAnimatedCloth(); applyTypedMotion(); applyLegacyMotion(); applyAnimatedMaterialTextures(); syncFlagAnchorObject();
        if (activeMeshHelper) activeMeshHelper.update();
        if (selectedMeshHelper) selectedMeshHelper.update();
        controls.update(); renderer.render(scene, camera);
    }
    animationFrameId = requestAnimationFrame(animate);
    if (typeof studioMoodName !== 'undefined' && studioMoodName !== 'neutral') void window.setStudioMood(studioMoodName);
    resizeHandler = () => {
        if (!container.isConnected || !renderer || !camera) return;
        const w = container.clientWidth || 1, h = container.clientHeight || 1;
        camera.aspect = w / h; camera.updateProjectionMatrix(); renderer.setSize(w, h);
    };
    window.addEventListener('resize', resizeHandler);
    resizeObserver = new ResizeObserver(resizeHandler);
    resizeObserver.observe(container);
};
function applyAnimatedCloth() {
    if (!clothAnimationEnabled) return;
    const geometries = new Set();
    for (const group of [staticGroup, movingGroup]) {
        group?.traverse(child => { if (child.isMesh && child.geometry?.userData?.clothAnimation) geometries.add(child.geometry); });
    }
    for (const geometry of geometries) {
        const animation = geometry.userData.clothAnimation;
        const positions = geometry.attributes.position;
        if (!animation || !positions || animation.vertexCount <= 0) continue;
        const phase = ((animTime / 1000) / animation.periodSeconds) % 1;
        const frame = phase * (animation.frameCount - 1);
        const first = Math.floor(frame);
        const second = (first + 1) % animation.frameCount;
        const blend = frame - first;
        const firstOffset = first * animation.vertexCount * 3;
        const secondOffset = second * animation.vertexCount * 3;
        for (let i = 0; i < animation.vertexCount; i++) {
            const offset = i * 3;
            for (let axis = 0; axis < 3; axis++) {
                const a = animation.positions[firstOffset + offset + axis];
                const b = animation.positions[secondOffset + offset + axis];
                positions.array[offset + axis] = a + (b - a) * blend;
            }
        }
        positions.needsUpdate = true;
        const normals = geometry.attributes.normal;
        if (animation.normals && normals) {
            for (let i = 0; i < animation.vertexCount; i++) {
                const offset = i * 3;
                const a0 = animation.normals[firstOffset + offset], a1 = animation.normals[firstOffset + offset + 1], a2 = animation.normals[firstOffset + offset + 2];
                const b0 = animation.normals[secondOffset + offset], b1 = animation.normals[secondOffset + offset + 1], b2 = animation.normals[secondOffset + offset + 2];
                let x = a0 + (b0 - a0) * blend, y = a1 + (b1 - a1) * blend, z = a2 + (b2 - a2) * blend;
                const length = Math.hypot(x, y, z) || 1;
                normals.array[offset] = x / length; normals.array[offset + 1] = y / length; normals.array[offset + 2] = z / length;
            }
            normals.needsUpdate = true;
        }
    }
}
function disposeObjectResources(root) {
    const geometries = new Set(), materials = new Set(), textures = new Set();
    root.traverse(child => {
        if (child.geometry && !pooledGeometry.has(child.geometry)) geometries.add(child.geometry);
        if (child.material) (Array.isArray(child.material) ? child.material : [child.material]).forEach(m => materials.add(m));
    });
    geometries.forEach(g => g.dispose());
    materials.forEach(material => {
        for (const property of ['map', 'normalMap', 'roughnessMap', 'metalnessMap', 'emissiveMap', 'alphaMap'])
            if (material[property]) textures.add(material[property]);
        material.dispose();
    });
    textures.forEach(texture => texture.dispose());
}
window.dispose3DViewer = function () {
    viewportSelectionQueue = Promise.resolve();
    window.disposeStudioMood?.();
    if (animationFrameId !== null) cancelAnimationFrame(animationFrameId);
    animationFrameId = null; selectedGizmo = dotNetHelper = null;
    selectedMeshObject = null; selectedMeshPath = null; clearSelectedMeshHighlight();
    if (resizeHandler) window.removeEventListener('resize', resizeHandler);
    if (viewKeyHandler) window.removeEventListener('keydown', viewKeyHandler, true);
    resizeObserver?.disconnect(); resizeObserver = null;
    resizeHandler = null;
    viewKeyHandler = null;
    activeStudioView = 'perspective';
    if (activeMeshHelper) { scene?.remove(activeMeshHelper); activeMeshHelper.dispose?.(); activeMeshHelper = null; }
    if (transformControls) { transformControls.detach(); transformControls.dispose(); scene?.remove(transformControls); }
    controls?.dispose(); if (scene) disposeObjectResources(scene);
    clearCompositeHelpers();
    releaseGeometryPool();
    if (renderer) { renderer.dispose(); renderer.domElement.remove(); }
    renderer = scene = camera = controls = transformControls = null;
    staticGroup = movingGroup = collisionGroup = pivotsGroup = lightsGroup = waypointsGroup = flagAnchorGroup = gridHelper = null;
    flagAnchorObject = null;
    typedMotions = []; typedGroupMotions = []; motionGroups = new Map(); groupMotionContainers = new Map(); currentPayload = null; animTime = 0; lastFrameTime = null;
    selectedCompositeSources = new Set(); compositeGroupBySource = new Map(); compositeLeaderSource = null;
    hasAutoFramedScene = false;
};
function selectGizmo(obj) {
    if (!transformControls) return;
    transformControls.detach(); selectedGizmo = obj;
    if (!obj) return;
    if (obj.userData.editable) transformControls.attach(obj);
    if (obj.userData.type === 'waypoint') {
        activeConstraintIndex = obj.userData.constraintIndex;
        updateWaypointHighlights();
        if (dotNetHelper) dotNetHelper.invokeMethodAsync('OnWaypointSelected', obj.userData.constraintIndex, obj.userData.isPointB);
    } else {
        if (dotNetHelper) dotNetHelper.invokeMethodAsync('OnGizmoSelected', obj.userData.type, obj.userData.index);
    }
}
window.scrollSelectedVariantIntoView = function () {
    const selected = document.querySelector('.studio-item-variant[aria-pressed="true"]')
        ?? document.querySelector('.studio-item-tab[aria-pressed="true"]');
    selected?.closest('.studio-item-switcher-row')?.scrollIntoView({ block: 'nearest', inline: 'nearest' });
};

window.selectGizmoFromUI = function (type, index) {
    if (type === 'waypoint') {
        const handle = findWaypointHandle(index, true);
        if (handle) selectGizmo(handle);
        return;
    }
    const group = { pivot: pivotsGroup, light: lightsGroup, 'composite-offset': pivotsGroup, 'composite-group': pivotsGroup }[type];
    const obj = group?.children.find(child => child.userData.index === index); if (obj) selectGizmo(obj);
};

window.highlightConstraintInViewer = function (constraintIndex) {
    activeConstraintIndex = constraintIndex;
    updateWaypointHighlights();
    const handleB = findWaypointHandle(constraintIndex, true);
    if (handleB) selectGizmo(handleB);
};

window.selectWaypointFromUI = function (constraintIndex, isPointB) {
    activeConstraintIndex = constraintIndex;
    updateWaypointHighlights();
    const handle = findWaypointHandle(constraintIndex, isPointB);
    if (handle) selectGizmo(handle);
};
window.selectMeshPartFromUI = function (path) {
    selectedMeshPath = path ?? null;
    const mesh = selectedMeshPath ? findMeshByPath(selectedMeshPath) : null;
    selectMeshObject(mesh, false);
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
    const uvs = part.uvsBytes == null ? part.uvs : geometryWords(part.uvsBytes);
    const animationPositions = part.animationPositionsBytes == null
        ? (part.animationPositions ?? null) : geometryWords(part.animationPositionsBytes);
    const animationNormals = part.animationNormalsBytes == null
        ? (part.animationNormals ?? null) : geometryWords(part.animationNormalsBytes);
    const animationFrameCount = Number(part.animationFrameCount ?? 0);
    const animationPeriodSeconds = Number(part.animationPeriodSeconds ?? 12);
    if (!(Array.isArray(indices) || indices instanceof Int32Array) || indices.length % 3 || indices.some(x => !Number.isInteger(x) || x < 0 || x >= positions.length / 3)) throw new Error('Invalid triangle indices.');
    for (const [value, key, stride] of [[normals, 'normals', 3], [uvs, 'uvs', 2]])
        if (value != null && buffer(value, key, stride).length / stride !== positions.length / 3) throw new Error(`${key} count does not match vertices.`);
    if (animationPositions != null) {
        if (!Number.isInteger(animationFrameCount) || animationFrameCount < 2 || animationFrameCount > 256
            || animationPositions.length !== positions.length * animationFrameCount
            || !Number.isFinite(animationPeriodSeconds) || animationPeriodSeconds <= 0)
            throw new Error('Invalid baked cloth animation.');
        if (animationNormals != null && animationNormals.length !== animationPositions.length)
            throw new Error('Animation normal count does not match animation positions.');
    }
    const geometry = new THREE.BufferGeometry(); geometry.setAttribute('position', new THREE.Float32BufferAttribute(positions, 3));
    geometry.setIndex(indices instanceof Int32Array ? new THREE.BufferAttribute(new Uint32Array(indices), 1) : indices);
    if (normals != null) geometry.setAttribute('normal', new THREE.Float32BufferAttribute(normals, 3)); else geometry.computeVertexNormals();
    if (uvs != null) geometry.setAttribute('uv', new THREE.Float32BufferAttribute(uvs, 2));
    if (geometry.attributes.normal.array.some(x => !Number.isFinite(x))) { geometry.dispose(); throw new Error('Normals overflow geometry.'); }
    if (animationPositions != null) {
        geometry.userData.clothAnimation = {
            positions: animationPositions instanceof Float32Array ? animationPositions : new Float32Array(animationPositions),
            normals: animationNormals == null ? null : (animationNormals instanceof Float32Array ? animationNormals : new Float32Array(animationNormals)),
            frameCount: animationFrameCount,
            vertexCount: positions.length / 3,
            periodSeconds: animationPeriodSeconds
        };
    }
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
    const enabled = part.enabled !== false;
    const visible = part.visible !== false;
    const movable = Boolean(part.movable);
    const collidable = Boolean(part.collidable);
    const waypoint = Boolean(part.waypoint);
    const effect = Boolean(part.effect);
    // Mapping-tools-like part coloring in preview:
    // default=neutral, collidable=yellow, waypoint/trigger=pink, movable=green.
    const color = waypoint ? 0xec4899 : collidable ? 0xeab308 : movable ? 0x34d399 : effect ? 0x22d3ee : (part.isCollision ? 0x38d9b3 : 0xb8bdc6);
    const material = new THREE.MeshStandardMaterial({ color,
        metalness: 0, roughness: .75, side: THREE.DoubleSide, wireframe: part.isCollision || isWireframe, transparent: Boolean(part.isCollision), opacity: part.isCollision ? .35 : 1 });
    const mesh = new THREE.Mesh(geometry, material); mesh.name = part.name ?? part.path ?? '';
    mesh.matrixAutoUpdate = false; mesh.matrix.copy(instance);
    mesh.visible = enabled && visible;
    mesh.userData = {
        path: part.path ?? part.name ?? '',
        entityPath: part.entityPath,
        sourceIndex: Number.isInteger(part.sourceIndex) ? part.sourceIndex : null,
        groupId: Number.isInteger(part.groupId) ? part.groupId : null,
        isCollision: Boolean(part.isCollision),
        mappings,
        meshMode: part.meshMode ?? 'auto',
        enabled,
        visible,
        collidable,
        effect,
        waypoint,
        movable
    };
    return mesh;
}
function markerScaleForBounds(bounds) {
    if (!bounds || bounds.isEmpty()) return 0.35;
    const diagonal = bounds.getSize(new THREE.Vector3()).length();
    return THREE.MathUtils.clamp(diagonal * 0.04, 0.12, 0.9);
}
function makeGizmo(data, index, type, markerScale = 1) {
    const position = ['x', 'y', 'z'].map(key => finite(data[key], `${type} ${key}`));
    const rotation = ['rotX', 'rotY', 'rotZ'].map(key => Number.isFinite(data[key]) ? Number(data[key]) : 0);
    if (data.index != null && (!Number.isInteger(data.index) || data.index < 0)) throw new Error('Invalid gizmo index.');
    const group = new THREE.Group(); group.position.fromArray(position); group.rotation.set(
        THREE.MathUtils.degToRad(rotation[0]),
        THREE.MathUtils.degToRad(rotation[1]),
        THREE.MathUtils.degToRad(rotation[2]));
    group.userData = { type, index: data.index ?? index, editable: data.editable !== false };
    if (type === 'light') {
        const color = data.colorHex ?? 0xfffbeb, intensity = data.intensity ?? 1.5, radius = data.radius ?? 15;
        if (!Number.isInteger(color) || color < 0 || color > 0xffffff) throw new Error('Light color must be RGB24.');
        nonnegative(intensity, 'Light intensity'); nonnegative(radius, 'Light radius');
        const point = new THREE.PointLight(color, intensity, radius); point.name = 'realLight'; group.add(point);
        const bulb = new THREE.Mesh(new THREE.SphereGeometry(.28 * markerScale, 16, 16), new THREE.MeshBasicMaterial({ color })); bulb.name = 'bulb'; group.add(bulb);
        const wire = new THREE.Mesh(new THREE.SphereGeometry(1, 12, 12), new THREE.MeshBasicMaterial({ color, wireframe: true, transparent: true, opacity: .15 }));
        wire.name = 'radiusWire'; wire.scale.setScalar(radius); group.add(wire);
    } else if (type === 'pivot') {
        group.add(new THREE.Mesh(new THREE.SphereGeometry(.22 * markerScale, 16, 16), new THREE.MeshBasicMaterial({ color: 0xfacc15 })), new THREE.AxesHelper(.9 * markerScale));
    } else if (type === 'composite-offset') {
        group.add(new THREE.Mesh(new THREE.BoxGeometry(.35 * markerScale, .35 * markerScale, .35 * markerScale), new THREE.MeshBasicMaterial({ color: 0x00ffff })), new THREE.AxesHelper(1.1 * markerScale));
    } else if (type === 'composite-group') {
        const groupColor = compositeGroupColor(data.index ?? index);
        group.add(new THREE.Mesh(new THREE.BoxGeometry(.42 * markerScale, .42 * markerScale, .42 * markerScale), new THREE.MeshBasicMaterial({ color: groupColor })), new THREE.AxesHelper(1.25 * markerScale));
    } else if (type === 'fx-emitter') {
        // The payload supplies the primary emitter in item-space metres;
        // native export converts it through the FX owner's prefab transform.
        const color = 0x22d3ee;
        group.add(new THREE.Mesh(new THREE.SphereGeometry(.27 * markerScale, 16, 12), new THREE.MeshBasicMaterial({ color })), new THREE.AxesHelper(1.15 * markerScale));
        const arrow = new THREE.Mesh(new THREE.ConeGeometry(.12 * markerScale, .42 * markerScale, 12), new THREE.MeshBasicMaterial({ color }));
        arrow.position.y = .38 * markerScale;
        group.add(arrow);
    }
    else throw new Error('Unknown gizmo type.');
    return group;
}
function frameCamera(bounds) {
    if (bounds.isEmpty()) return;
    camera.up.set(0, 1, 0);
    activeStudioView = 'perspective';
    const center = bounds.getCenter(new THREE.Vector3()), radius = Math.max(bounds.getSize(new THREE.Vector3()).length() / 2, 1);
    const vertical = THREE.MathUtils.degToRad(camera.fov), horizontal = 2 * Math.atan(Math.tan(vertical / 2) * camera.aspect);
    const distance = radius / Math.sin(Math.min(vertical, horizontal) / 2) * 1.2;
    camera.position.copy(center).add(new THREE.Vector3(1, .8, 1).normalize().multiplyScalar(distance));
    camera.near = Math.max(distance / 10000, .001); camera.far = Math.max(distance * 10, radius * 100, 100);
    camera.updateProjectionMatrix(); controls.target.copy(center); controls.update(); controls.saveState();
}

function getMotionBaseMatrix(motion) {
    if (!motion.parentMotion) {
        return motion.childRest.clone();
    }
    const parentBase = getMotionBaseMatrix(motion.parentMotion);
    const parentSignalAtB = new THREE.Matrix4().makeTranslation(
        motion.parentMotion.fields.translationAxis === 0 ? motion.parentMotion.fields.translationMax : 0,
        motion.parentMotion.fields.translationAxis === 1 ? motion.parentMotion.fields.translationMax : 0,
        motion.parentMotion.fields.translationAxis === 2 ? motion.parentMotion.fields.translationMax : 0
    );
    const parentWorldAtB = parentBase.clone().multiply(parentSignalAtB);
    return parentWorldAtB.multiply(motion.inverseParent).multiply(motion.childRest);
}

function makeTextSprite(text, bgColor = '#22c55e', textColor = '#ffffff', markerScale = 1) {
    if (typeof document === 'undefined' || !document.createElement) return new THREE.Group();
    const canvas = document.createElement('canvas');
    canvas.width = 128;
    canvas.height = 64;
    const ctx = canvas.getContext('2d');
    if (!ctx) return new THREE.Group();

    const r = 16, x = 4, y = 4, w = 120, h = 56;
    ctx.fillStyle = bgColor;
    if (typeof ctx.roundRect === 'function') {
        ctx.beginPath();
        ctx.roundRect(x, y, w, h, r);
    } else {
        ctx.beginPath();
        ctx.moveTo(x + r, y);
        ctx.arcTo(x + w, y, x + w, y + h, r);
        ctx.arcTo(x + w, y + h, x, y + h, r);
        ctx.arcTo(x, y + h, x, y, r);
        ctx.arcTo(x, y, x + w, y, r);
        ctx.closePath();
    }
    ctx.fill();

    ctx.strokeStyle = '#ffffff';
    ctx.lineWidth = 3;
    ctx.stroke();

    ctx.fillStyle = textColor;
    ctx.font = 'bold 30px sans-serif';
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    ctx.fillText(text, 64, 32);

    const texture = new THREE.CanvasTexture(canvas);
    const spriteMaterial = new THREE.SpriteMaterial({ map: texture, depthTest: false, depthWrite: false });
    const sprite = new THREE.Sprite(spriteMaterial);
    sprite.scale.set(1.2 * markerScale, 0.6 * markerScale, 1);
    return sprite;
}

function makeWaypointHandle(pos, label, isPointB, constraintIndex, motion, markerScale = 1) {
    const group = new THREE.Group();
    group.position.copy(pos);
    group.userData = {
        type: 'waypoint',
        constraintIndex,
        isPointB,
        motionPath: motion.path,
        childPath: motion.childPath,
        editable: true
    };

    const color = isPointB ? 0xf97316 : 0x22c55e;
    const sphere = new THREE.Mesh(
        new THREE.SphereGeometry(0.32 * markerScale, 16, 16),
        new THREE.MeshStandardMaterial({ color, roughness: 0.3, metalness: 0.2, emissive: color, emissiveIntensity: 0.25 })
    );
    sphere.name = 'handleSphere';
    group.add(sphere);

    const halo = new THREE.Mesh(
        new THREE.RingGeometry(0.42 * markerScale, 0.55 * markerScale, 24),
        new THREE.MeshBasicMaterial({ color, side: THREE.DoubleSide, transparent: true, opacity: 0.5 })
    );
    halo.name = 'handleHalo';
    halo.rotation.x = Math.PI / 2;
    group.add(halo);

    const sprite = makeTextSprite(label, isPointB ? '#ea580c' : '#16a34a', '#ffffff', markerScale);
    sprite.position.set(0, 0.7 * markerScale, 0);
    sprite.name = 'handleSprite';
    group.add(sprite);

    return group;
}

function makePathLine(worldA, worldB, constraintIndex, markerScale = 1) {
    const group = new THREE.Group();
    group.name = `pathLineGroup_${constraintIndex}`;
    group.userData = { constraintIndex };

    const geom = new THREE.BufferGeometry().setFromPoints([worldA, worldB]);
    const mat = new THREE.LineBasicMaterial({
        color: 0x06b6d4,
        linewidth: 3,
        transparent: true,
        opacity: 0.85
    });
    const line = new THREE.Line(geom, mat);
    line.name = 'pathLine';
    group.add(line);

    const dir = new THREE.Vector3().subVectors(worldB, worldA);
    const len = dir.length();
    if (len > 0.05 * markerScale) {
        dir.normalize();
        const arrow = new THREE.Mesh(
            new THREE.ConeGeometry(0.2 * markerScale, 0.5 * markerScale, 12),
            new THREE.MeshBasicMaterial({ color: 0x06b6d4 })
        );
        arrow.name = 'pathArrow';
        const arrowPos = new THREE.Vector3().addVectors(worldA, dir.clone().multiplyScalar(len * 0.6));
        arrow.position.copy(arrowPos);
        arrow.quaternion.setFromUnitVectors(new THREE.Vector3(0, 1, 0), dir);
        group.add(arrow);
    }

    return group;
}

function buildWaypoints(motions, stagedGroup, bounds) {
    if (!motions || motions.length === 0) return;
    const markerScale = markerScaleForBounds(bounds);
    for (let i = 0; i < motions.length; i++) {
        const motion = motions[i];
        const constraintIndex = motion.constraintIndex ?? i;
        const baseMat = getMotionBaseMatrix(motion);

        const localA = new THREE.Vector3().setComponent(motion.fields.translationAxis, motion.fields.translationMin);
        const localB = new THREE.Vector3().setComponent(motion.fields.translationAxis, motion.fields.translationMax);

        const worldA = localA.clone().applyMatrix4(baseMat);
        const worldB = localB.clone().applyMatrix4(baseMat);

        bounds.expandByPoint(worldA);
        bounds.expandByPoint(worldB);

        const prefix = motions.length > 1 ? `${constraintIndex + 1}` : '';
        const labelA = `${prefix}A`;
        const labelB = `${prefix}B`;

        const handleA = makeWaypointHandle(worldA, labelA, false, constraintIndex, motion, markerScale);
        const handleB = makeWaypointHandle(worldB, labelB, true, constraintIndex, motion, markerScale);

        stagedGroup.add(handleA);
        stagedGroup.add(handleB);

        const pathLine = makePathLine(worldA, worldB, constraintIndex, markerScale);
        stagedGroup.add(pathLine);

        if (motion.parentMotion) {
            const parentBase = getMotionBaseMatrix(motion.parentMotion);
            const parentSignalAtB = new THREE.Matrix4().makeTranslation(
                motion.parentMotion.fields.translationAxis === 0 ? motion.parentMotion.fields.translationMax : 0,
                motion.parentMotion.fields.translationAxis === 1 ? motion.parentMotion.fields.translationMax : 0,
                motion.parentMotion.fields.translationAxis === 2 ? motion.parentMotion.fields.translationMax : 0
            );
            const parentWorldB = new THREE.Vector3().applyMatrix4(parentBase.clone().multiply(parentSignalAtB));
            if (parentWorldB.distanceTo(worldA) > 0.05) {
                const connGeom = new THREE.BufferGeometry().setFromPoints([parentWorldB, worldA]);
                const connMat = new THREE.LineDashedMaterial({ color: 0x94a3b8, dashSize: 0.3, gapSize: 0.15, opacity: 0.6, transparent: true });
                const connLine = new THREE.Line(connGeom, connMat);
                connLine.computeLineDistances();
                stagedGroup.add(connLine);
            }
        }
    }
}

function findWaypointHandle(constraintIndex, isPointB) {
    if (!waypointsGroup) return null;
    let found = null;
    waypointsGroup.traverse(child => {
        if (child.userData?.type === 'waypoint'
            && child.userData.constraintIndex === constraintIndex
            && child.userData.isPointB === isPointB) {
            found = child;
        }
    });
    return found;
}

function updatePathLineGeometry(constraintIndex, worldA, worldB) {
    if (!waypointsGroup) return;
    const pathGroup = waypointsGroup.getObjectByName(`pathLineGroup_${constraintIndex}`);
    if (!pathGroup) return;

    const line = pathGroup.getObjectByName('pathLine');
    if (line) {
        line.geometry.dispose();
        line.geometry = new THREE.BufferGeometry().setFromPoints([worldA, worldB]);
    }

    const arrow = pathGroup.getObjectByName('pathArrow');
    if (arrow) {
        const dir = new THREE.Vector3().subVectors(worldB, worldA);
        const len = dir.length();
        if (len > 0.05) {
            dir.normalize();
            const arrowPos = new THREE.Vector3().addVectors(worldA, dir.clone().multiplyScalar(len * 0.6));
            arrow.position.copy(arrowPos);
            arrow.quaternion.setFromUnitVectors(new THREE.Vector3(0, 1, 0), dir);
            arrow.visible = true;
        } else {
            arrow.visible = false;
        }
    }
}

function handleWaypointDrag(constraintIndex, isPointB, newPos) {
    const motion = typedMotions.find(m => (m.constraintIndex ?? 0) === constraintIndex) || typedMotions[constraintIndex];
    if (!motion) return;

    const baseMat = getMotionBaseMatrix(motion);
    const invBase = baseMat.clone().invert();
    const localPos = newPos.clone().applyMatrix4(invBase);

    let axis = motion.fields.translationAxis;
    let minVal = motion.fields.translationMin;
    let maxVal = motion.fields.translationMax;

    if (isPointB) {
        const localA = new THREE.Vector3().setComponent(axis, minVal);
        const delta = new THREE.Vector3().subVectors(localPos, localA);

        if (transformControls?.axis === 'X') axis = 0;
        else if (transformControls?.axis === 'Y') axis = 1;
        else if (transformControls?.axis === 'Z') axis = 2;
        else {
            const absX = Math.abs(delta.x), absY = Math.abs(delta.y), absZ = Math.abs(delta.z);
            if (absX >= absY && absX >= absZ && absX > 0.1) axis = 0;
            else if (absY >= absX && absY >= absZ && absY > 0.1) axis = 1;
            else if (absZ >= absX && absZ >= absY && absZ > 0.1) axis = 2;
        }

        maxVal = localPos.getComponent(axis);
        motion.fields.translationAxis = axis;
        motion.fields.translationMax = maxVal;
    } else {
        minVal = localPos.getComponent(axis);
        motion.fields.translationMin = minVal;
    }

    const worldA = new THREE.Vector3().setComponent(axis, minVal).applyMatrix4(baseMat);
    const worldB = new THREE.Vector3().setComponent(axis, maxVal).applyMatrix4(baseMat);

    const handleA = findWaypointHandle(constraintIndex, false);
    const handleB = findWaypointHandle(constraintIndex, true);
    if (handleA && isPointB) handleA.position.copy(worldA);
    if (handleB && !isPointB) handleB.position.copy(worldB);

    updatePathLineGeometry(constraintIndex, worldA, worldB);

    const axisNames = ['X', 'Y', 'Z'];
    const axisName = axisNames[axis] || 'X';

    if (dotNetHelper) {
        dotNetHelper.invokeMethodAsync('OnWaypointMoved', constraintIndex, isPointB, minVal, maxVal, axisName);
    }
}

function updateWaypointHighlights() {
    if (!waypointsGroup) return;

    if (activeMeshHelper) {
        scene?.remove(activeMeshHelper);
        activeMeshHelper.dispose?.();
        activeMeshHelper = null;
    }

    const activeMotion = typedMotions.find(m => (m.constraintIndex ?? 0) === activeConstraintIndex) || typedMotions[activeConstraintIndex];
    if (activeMotion && motionGroups.has(activeMotion.childPath)) {
        const groupPair = motionGroups.get(activeMotion.childPath);
        const visualGroup = groupPair[0];
        if (visualGroup && visualGroup.children.length > 0) {
            activeMeshHelper = new THREE.BoxHelper(visualGroup, 0x00ffff);
            scene.add(activeMeshHelper);
        }
    }

    waypointsGroup.traverse(child => {
        if (child.userData?.type === 'waypoint') {
            const isActive = child.userData.constraintIndex === activeConstraintIndex;
            const halo = child.getObjectByName('handleHalo');
            if (halo) {
                halo.scale.setScalar(isActive ? 1.4 : 1.0);
                halo.material.opacity = isActive ? 0.9 : 0.4;
            }
            const sphere = child.getObjectByName('handleSphere');
            if (sphere) {
                sphere.material.emissiveIntensity = isActive ? 0.6 : 0.2;
            }
        }
        if (child.name === 'pathLine') {
            const parentGroup = child.parent;
            const isActive = parentGroup?.userData?.constraintIndex === activeConstraintIndex;
            child.material.color.setHex(isActive ? 0x00ffff : 0x64748b);
            child.material.opacity = isActive ? 1.0 : 0.35;
        }
        if (child.name === 'pathArrow') {
            const parentGroup = child.parent;
            const isActive = parentGroup?.userData?.constraintIndex === activeConstraintIndex;
            child.material.color.setHex(isActive ? 0x00ffff : 0x64748b);
            child.scale.setScalar(isActive ? 1.3 : 0.9);
        }
    });
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
    const groupMotions = compileGroupMotions(data.groupMotions ?? []);
    const staged = Array.from({ length: 6 }, () => new THREE.Group()), groups = new Map(), bounds = new THREE.Box3();
    const compositeSelection = data.compositeSelection ?? {};
    const sourceGroups = new Map(
        (compositeSelection.sourceGroups ?? [])
            .filter(entry => Number.isInteger(entry?.sourceIndex) && Number.isInteger(entry?.groupId))
            .map(entry => [entry.sourceIndex, entry.groupId]));
    const stagedGroupLayers = [new Map(), new Map(), new Map()];
    const localGroupMotionContainers = new Map();
    function ensureGroupLayerContainer(groupId, layerIndex) {
        const layerMap = stagedGroupLayers[layerIndex];
        if (!layerMap.has(groupId)) {
            const container = new THREE.Group();
            container.matrixAutoUpdate = false;
            container.matrix.identity();
            layerMap.set(groupId, container);
            staged[layerIndex].add(container);
            if (!localGroupMotionContainers.has(groupId)) localGroupMotionContainers.set(groupId, []);
            localGroupMotionContainers.get(groupId).push(container);
        }
        return layerMap.get(groupId);
    }
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
            const sourceIndex = Number.isInteger(motion.sourceIndex) ? motion.sourceIndex : null;
            const groupId = sourceIndex !== null && sourceGroups.has(sourceIndex) ? sourceGroups.get(sourceIndex) : null;
            if (groupId != null) {
                ensureGroupLayerContainer(groupId, 1).add(pair[0]);
                ensureGroupLayerContainer(groupId, 2).add(pair[1]);
            } else {
                staged[1].add(pair[0]);
                staged[2].add(pair[1]);
            }
            groups.set(motion.childPath, pair);
        }
        for (const part of data.parts ?? []) {
            const mode = (part.meshMode ?? 'auto').toLowerCase();
            if (part.enabled === false) continue;
            const owner = owners.get(part.entityPath), mesh = makePart(part, owner, bounds, pool);
            const asMoving = mode === 'kinematic' || (mode === 'auto' && (Boolean(part.movable) || part.isMoving));
            if (owner && mode !== 'static') groups.get(owner.childPath)[part.isCollision ? 1 : 0].add(mesh);
            else {
                const layerIndex = part.isCollision ? 2 : (asMoving ? 1 : 0);
                if (Number.isInteger(part.groupId)) ensureGroupLayerContainer(part.groupId, layerIndex).add(mesh);
                else staged[layerIndex].add(mesh);
            }
        }
        for (const [property, type, target] of [['pivots', 'pivot', 3], ['compositeOffsets', 'composite-offset', 3], ['compositeGroups', 'composite-group', 3], ['lights', 'light', 4]])
            (data[property] ?? []).forEach((gizmo, index) => { const group = makeGizmo(gizmo, index, type, markerScaleForBounds(bounds)); staged[target].add(group); bounds.expandByPoint(group.position); });
        if (data.fxEmitter && typeof data.fxEmitter === 'object') {
            const group = makeGizmo(data.fxEmitter, 0, 'fx-emitter', markerScaleForBounds(bounds));
            staged[3].add(group); bounds.expandByPoint(group.position);
        }
        if (data.activeConstraintIndex != null) {
            activeConstraintIndex = data.activeConstraintIndex;
        }
        buildWaypoints(motions, staged[5], bounds);
    } catch (error) { staged.forEach(disposeObjectResources); added.forEach(g => g.dispose()); throw error; }
    window.clearViewerScene(true);
    if (epoch !== geometryEpoch) releaseGeometryPool();
    sharedGeometry = pool; geometryEpoch = epoch;
    const destinations = [staticGroup, movingGroup, collisionGroup, pivotsGroup, lightsGroup, waypointsGroup];
    staged.forEach((group, index) => { while (group.children.length) destinations[index].add(group.children[0]); });
    // clearViewerScene retires the old selection. Apply the new payload only afterwards.
    selectedMeshPath = typeof data.selectedMeshPath === 'string' && data.selectedMeshPath.length > 0 ? data.selectedMeshPath : null;
    selectedCompositeSources = new Set((compositeSelection.selectedSourceIndices ?? []).filter(Number.isInteger));
    compositeLeaderSource = Number.isInteger(compositeSelection.leaderSourceIndex) && compositeSelection.leaderSourceIndex >= 0
        ? compositeSelection.leaderSourceIndex : null;
    compositeGroupBySource = new Map((compositeSelection.sourceGroups ?? [])
        .filter(entry => Number.isInteger(entry?.sourceIndex) && Number.isInteger(entry?.groupId))
        .map(entry => [entry.sourceIndex, entry.groupId]));
    isPlaying = data.playing !== false; legacyMotion = nextLegacyMotion;
    typedMotions = motions; typedGroupMotions = groupMotions; motionGroups = groups; groupMotionContainers = localGroupMotionContainers; previewPhase01 = offset;
    currentPayload = { ...data, geometryDefinitions: [] };
    if (selectedMeshPath) {
        const mesh = findMeshByPath(selectedMeshPath);
        selectMeshObject(mesh, false);
    } else {
        selectMeshObject(null, false);
    }
    renderDrawPathVisual();
    applyTypedMotion(); applySceneFilter(); applyCompositeVisualState(); applyLayerVisibility(); updateWaypointHighlights(); if (!preserveCamera) frameCamera(bounds);
    const renderVersion = ++textureRenderVersion;
    void applyLocalTextures(data, renderVersion);
}
window.renderStudioScene = payload => {
    // Scene edits/variant switches retain the view. A new viewer or an explicit
    // frameCamera request frames the authored bounds without moving any geometry.
    renderPayload(payload, hasAutoFramedScene && payload.frameCamera !== true);
    hasAutoFramedScene = Boolean(payload.parts?.length);
};
window.setTransformMode = mode => {
    if (!transformControls) return;
    transformControls.setMode(mode === 'rotate' ? 'rotate' : 'translate');
};
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
    selectedMeshObject = null;
    selectedMeshPath = null;
    if (activeMeshHelper) { scene?.remove(activeMeshHelper); activeMeshHelper.dispose?.(); activeMeshHelper = null; }
    clearSelectedMeshHighlight();
    clearCompositeHelpers();
    clearDrawPathVisual();
    for (const group of [staticGroup, movingGroup, collisionGroup, pivotsGroup, lightsGroup, waypointsGroup]) {
        if (!group) continue;
        while (group.children.length) { const child = group.children[0]; group.remove(child); disposeObjectResources(child); }
        group.position.set(0, 0, 0); group.rotation.set(0, 0, 0); group.scale.set(1, 1, 1);
    }
    typedMotions = []; typedGroupMotions = []; motionGroups = new Map(); groupMotionContainers = new Map(); currentPayload = null; animTime = 0; lastFrameTime = null;
    selectedCompositeSources = new Set(); compositeGroupBySource = new Map(); compositeLeaderSource = null;
    if (!keepGeometry) {
        hasAutoFramedScene = false; animTime = 0; lastFrameTime = null;
        releaseGeometryPool();
    }
};
window.setDrawPathMode = function (enabled, planeY) {
    drawPathEnabled = Boolean(enabled);
    if (typeof planeY === 'number' && Number.isFinite(planeY)) drawPathPlaneY = planeY;
    return drawPathEnabled;
};
window.clearDrawnPath = function () {
    drawPathPoints = [];
    renderDrawPathVisual();
};
window.getDrawnPathPoints = function () {
    return drawPathPoints.map(point => ({ x: point.x, y: point.y, z: point.z }));
};
// Older hosts may call these APIs, but generic/fallback motion is never inferred from them.
window.setMotionPreview = value => { if (value?.motions) window.setTypedMotionPreview(value); };
window.setAnimationAxis = function () {};
window.setAnimationPlaying = playing => { isPlaying = Boolean(playing); };
window.setAnimationSpeed = speed => { animSpeed = nonnegative(Number(speed), 'Playback speed'); };
window.setClothAnimationPreview = enabled => { clothAnimationEnabled = Boolean(enabled); };
function applyLayerVisibility() {
    if (!staticGroup) return;
    staticGroup.visible = movingGroup.visible = showMeshes; collisionGroup.visible = showCollision;
    pivotsGroup.visible = showPivots; lightsGroup.visible = showLights;
    if (waypointsGroup) waypointsGroup.visible = showWaypoints;
    for (const helper of [...compositeSelectionHelpers, ...compositeGroupHelpers]) helper.visible = showMeshes;
}
window.toggleLayer = function (name) {
    let value;
    switch (name) {
        case 'meshes': value = showMeshes = !showMeshes; break;
        case 'collision': value = showCollision = !showCollision; break;
        case 'pivots': value = showPivots = !showPivots; break;
        case 'lights': value = showLights = !showLights; break;
        case 'waypoints': value = showWaypoints = !showWaypoints; break;
        default: throw new Error('Unknown scene layer.');
    }
    applyLayerVisibility(); return value;
};
function applySceneFilter() {
    const skinPreview = currentPayload?.materials?.some(material => /^skins\//i.test(material.gameMaterialLink || ''));
    const nativePreview = skinPreview || currentPayload?.materials?.some(material => /^material[/\\]/i.test(material.gameMaterialLink || ''));
    const lod = sceneFilter.lodMask ?? (nativePreview ? 1 : null);
    const damageLayers = new Set((currentPayload?.materials ?? []).filter(material => /DmgDecal_/i.test(material.gameMaterialName || '')).map(material => material.path));
    for (const group of [staticGroup, movingGroup]) group?.traverse(child => {
        if (!child.isMesh) return;
        child.visible = sceneFilter.materialIndex === null && sceneFilter.materialPath === null && lod === null || child.userData.mappings.some(map =>
            (sceneFilter.materialIndex === null || map.materialIndex === sceneFilter.materialIndex)
            && (sceneFilter.materialPath === null || map.materialPath === sceneFilter.materialPath)
            && !damageLayers.has(map.materialPath)
            && (lod === null || map.lodMask !== null && (map.lodMask & lod) !== 0));
    });
    applyCompositeVisualState();
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
window.resetCamera = function () {
    if (!camera || !controls) return;
    const bounds = new THREE.Box3();
    for (const group of [staticGroup, movingGroup, collisionGroup]) group?.traverse(mesh => {
        if (isSelectableMesh(mesh)) bounds.expandByObject(mesh);
    });
    frameCamera(bounds);
};
window.setStudioView = function (view, reverse = false) {
    if (!camera || !controls) return;
    const target = controls.target.clone();
    const distance = Math.max(camera.position.distanceTo(target), 1);
    let direction;
    let up = new THREE.Vector3(0, 1, 0);
    switch ((view ?? '').toLowerCase()) {
        case 'top':
            direction = new THREE.Vector3(0, 1, 0);
            up.set(0, 0, -1);
            break;
        case 'front':
            direction = new THREE.Vector3(0, 0, 1);
            break;
        case 'right':
            direction = new THREE.Vector3(1, 0, 0);
            break;
        default:
            direction = new THREE.Vector3(1, .8, 1).normalize();
            break;
    }
    if (reverse) direction.negate();
    const requested = (view ?? '').toLowerCase();
    activeStudioView = ['top', 'front', 'right'].includes(requested) ? requested : 'perspective';
    camera.up.copy(up);
    camera.position.copy(target).add(direction.multiplyScalar(distance));
    camera.lookAt(target);
    controls.update();
};
window.downloadObjFile = function (filename, content) {
    const url = URL.createObjectURL(new Blob([content], { type: 'text/plain' })), link = document.createElement('a');
    link.href = url; link.download = filename; link.click(); URL.revokeObjectURL(url);
};
window.downloadBinaryGbx = function (filename, base64Data) {
    const link = document.createElement('a'); link.download = filename; link.href = 'data:application/octet-stream;base64,' + base64Data; link.click();
};
