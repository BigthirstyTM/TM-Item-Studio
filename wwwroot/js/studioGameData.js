// Local files stay in the browser. Keep their full paths: GameData contains many
// unrelated MainBody.Mesh / Material / SkyColor files with the same basename.
let studioGameFiles = new Map();
let studioMoodCache = new Map(), studioMoodVersion = 0, studioMoodName = 'neutral';
let studioMoodResources = [], studioMoodState = { name: 'neutral', source: null };
function gameFile(path) {
    const key = normalizedPath(path).replace(/^.*?gamedata\//, '').replace(/^\/+/, '');
    if (studioGameFiles.has(key)) return [key, studioGameFiles.get(key)];
    const matches = [...studioGameFiles].filter(([p]) => p.endsWith('/' + key) || key.endsWith('/' + p));
    return matches.length === 1 ? matches[0] : null;
}
window.indexStudioGameData = async function (inputId, useAsGameData = true) {
    const files = [...document.getElementById(inputId).files];
    const indexed = new Map(files.map(file => {
        const relative = file.webkitRelativePath || file.name;
        const parts = relative.replaceAll('\\', '/').split('/');
        return [normalizedPath(parts.length > 1 ? parts.slice(1).join('/') : relative), file];
    }));
    textureFiles = new Map([...indexed].map(([path, file]) => [path, { getFile: async () => file }]));
    textureDirectory = { name: files[0]?.webkitRelativePath?.split('/')[0] || 'Local textures' };
    const manifestFile = indexed.get('manifest.json');
    textureManifest = manifestFile ? JSON.parse(await manifestFile.text()) : null;
    if (useAsGameData) { studioGameFiles = indexed; studioMoodCache.clear(); }
    if (currentPayload) void applyLocalTextures(currentPayload, ++textureRenderVersion);
    return { name: textureDirectory.name, fileCount: indexed.size };
};
window.readStudioGameDataFile = async function (path) {
    const resolved = gameFile(path);
    return resolved ? new Uint8Array(await resolved[1].arrayBuffer()) : null;
};
window.findStudioSkinMesh = function (skinReference) {
    const base = normalizedPath(skinReference).replace(/\.zip$/i, '');
    const choices = [base + '/mainbody.mesh.gbx', base + '/standard/mainbody.mesh.gbx', base + '/male/player.mesh.gbx'];
    for (const path of choices) if (gameFile(path)) return path;
    return null;
};
window.findStudioAssetPath = function (name, size) {
    const matches = [...studioGameFiles].filter(([path, file]) => path.endsWith('/' + name.toLowerCase()) && (size == null || file.size === size));
    return matches.length === 1 ? matches[0][0] : null;
};
window.getStudioMoodInfo = () => ({ ...studioMoodState });
window.disposeStudioMood = function () {
    ++studioMoodVersion;
    for (const resource of studioMoodResources) { if (resource.isObject3D) resource.parent?.remove(resource); resource.dispose?.(); }
    studioMoodResources = [];
    if (scene) scene.environment = null;
};
async function moodDds(path) {
    const found = gameFile(path);
    if (!found) throw new Error(`Missing GameData file: ${path}`);
    const data = await found[1].arrayBuffer(), view = new DataView(data);
    const width = view.getUint32(16, true), height = view.getUint32(12, true);
    const mips = Math.max(1, view.getUint32(28, true));
    const fourCC = String.fromCharCode(...new Uint8Array(data, 84, 4));
    const cube = Boolean(view.getUint32(112, true) & 0x200);
    const dxgi = fourCC === 'DX10' ? view.getUint32(128, true) : 0;
    const decoder = (await import('../vendor/texture2ddecoder/index.mjs')).Texture2DDecoder;
    const faces = [];
    let offset = fourCC === 'DX10' ? 148 : 128;
    for (let face = 0; face < (cube ? 6 : 1); face++) {
        let pixels;
        if (dxgi === 95 || dxgi === 96) {
            const size = Math.ceil(width / 4) * Math.ceil(height / 4) * 16;
            pixels = await decoder.decodeBc6(new Uint8Array(data, offset, size), width, height);
        } else if (fourCC === 'DXT5' || fourCC === 'DXT1') {
            const size = Math.ceil(width / 4) * Math.ceil(height / 4) * (fourCC === 'DXT1' ? 8 : 16);
            pixels = await decoder[fourCC === 'DXT1' ? 'decodeBc1' : 'decodeBc3'](new Uint8Array(data, offset, size), width, height);
        } else if (!dxgi && view.getUint32(88, true) === 24) {
            const source = new Uint8Array(data, offset, width * height * 3);
            pixels = new Uint8Array(width * height * 4);
            for (let i = 0; i < width * height; i++) { pixels.set(source.subarray(i * 3, i * 3 + 3), i * 4); pixels[i * 4 + 3] = 255; }
        } else throw new Error(`Unsupported mood DDS format: ${path} (${dxgi || fourCC})`);
        // Decoder and the game's 24-bit DDS images use BGRA order.
        for (let i = 0; i < pixels.length; i += 4) { const b = pixels[i]; pixels[i] = pixels[i + 2]; pixels[i + 2] = b; }
        faces.push({ data: pixels, width, height });
        for (let mip = 0; mip < mips; mip++) {
            const w = Math.max(1, width >> mip), h = Math.max(1, height >> mip);
            offset += fourCC === '\0\0\0\0' ? w * h * 3 : Math.ceil(w / 4) * Math.ceil(h / 4) * (fourCC === 'DXT1' ? 8 : 16);
        }
    }
    return { faces, cube, path: found[0] };
}
function moodTexture(image) {
    const texture = new THREE.DataTexture(image.data, image.width, image.height, THREE.RGBAFormat);
    texture.minFilter = texture.magFilter = THREE.LinearFilter;
    texture.needsUpdate = true;
    return texture;
}
async function loadMood(name) {
    const folder = { morning: 'Sunrise', day: 'Day', evening: 'Sunset', night: 'Night' }[name];
    if (!folder) throw new Error('Unknown Trackmania mood.');
    const prefix = `Stadium/Media/Moods/${folder}/`;
    const xmlFile = gameFile(prefix + 'Mood.MoodSetting.xml');
    if (!xmlFile) throw new Error('Choose the extracted GameData or Stadium folder to load the original Trackmania moods.');
    const xml = new DOMParser().parseFromString(await xmlFile[1].text(), 'application/xml');
    const color = tag => (xml.querySelector(tag)?.getAttribute('HdrColor') || '0 0 0').split(/\s+/).map(Number);
    const [sky, clouds, environment] = await Promise.all([moodDds(prefix + 'SkyColor.dds'), moodDds(prefix + 'SkyClouds.dds'), moodDds(prefix + 'EnvCubicHdr.dds')]);
    return { source: xmlFile[0], sky, clouds, environment, ambient: color('LAmbient'), sun: color('LDirSun'), moon: color('LDirMoon'),
        time: Number(xml.querySelector('Light').getAttribute('DayTime01')), latitude: Number(xml.querySelector('Light').getAttribute('Latitude')),
        fogColor: xml.querySelector('Fog').getAttribute('Diffuse_sRGB'), fogNear: Number(xml.querySelector('Fog').getAttribute('DepthMin')),
        fogFar: Number(xml.querySelector('Fog').getAttribute('DepthMax')), cloudIntensity: Number(xml.querySelector('SkyClouds').getAttribute('GlobalIntens')) };
}
window.setStudioMood = async function (name) {
    const version = ++studioMoodVersion;
    if (name !== 'neutral' && !studioMoodCache.has(name)) studioMoodCache.set(name, await loadMood(name));
    if (version !== studioMoodVersion || !scene) return;
    for (const resource of studioMoodResources) { if (resource.isObject3D) resource.parent?.remove(resource); resource.dispose?.(); }
    studioMoodResources = [];
    scene.children.filter(child => child.userData.studioDefaultLight).forEach(light => { light.visible = name === 'neutral'; });
    studioMoodName = name;
    scene.environment = null; scene.fog = null;
    if (name === 'neutral') { scene.background = new THREE.Color(0x131316); renderer.toneMapping = THREE.NoToneMapping; studioMoodState = { name, source: null }; return; }
    const mood = studioMoodCache.get(name);
    const sky = moodTexture(mood.sky.faces[0]), clouds = moodTexture(mood.clouds.faces[0]);
    const cube = new THREE.CubeTexture(mood.environment.faces.map(image => { const canvas = document.createElement('canvas'); canvas.width = image.width; canvas.height = image.height; canvas.getContext('2d').putImageData(new ImageData(new Uint8ClampedArray(image.data), image.width, image.height), 0, 0); return canvas; }));
    cube.needsUpdate = true;
    scene.environment = cube;
    const ambient = new THREE.AmbientLight(new THREE.Color(...mood.ambient), 1);
    const sun = new THREE.DirectionalLight(new THREE.Color(...mood.sun), 1);
    const moon = new THREE.DirectionalLight(new THREE.Color(...mood.moon), 1);
    const angle = (mood.time - .25) * Math.PI * 2, latitude = mood.latitude * Math.PI / 180;
    sun.position.set(Math.cos(angle) * 100, Math.sin(angle) * Math.cos(latitude) * 100, Math.sin(angle) * Math.sin(latitude) * 100);
    moon.position.copy(sun.position).multiplyScalar(-1);
    scene.add(ambient, sun, moon);
    // SkyColor is the original game's hemisphere lookup, not a made-up color preset.
    const skyMaterial = new THREE.ShaderMaterial({ side: THREE.BackSide, depthWrite: false,
        uniforms: { sky: { value: sky }, clouds: { value: clouds }, cloudIntensity: { value: mood.cloudIntensity } },
        vertexShader: 'varying vec3 direction; void main(){ direction=position; gl_Position=projectionMatrix*mat4(mat3(viewMatrix))*vec4(position,1.0); gl_Position.z=gl_Position.w; }',
        fragmentShader: 'uniform sampler2D sky; uniform sampler2D clouds; uniform float cloudIntensity; varying vec3 direction; void main(){ vec3 d=normalize(direction); vec2 uv=vec2(atan(d.z,d.x)/6.2831853+0.5, asin(clamp(d.y,-1.0,1.0))/3.14159265+0.5); vec3 c=texture2D(sky,uv).rgb; vec4 cl=texture2D(clouds,uv); c=mix(c,cl.rgb,cl.a*cloudIntensity*max(0.0,d.y)); gl_FragColor=vec4(c,1.0); }' });
    const skyGeometry = new THREE.SphereGeometry(1, 48, 24), dome = new THREE.Mesh(skyGeometry, skyMaterial); dome.frustumCulled = false; dome.renderOrder = -100;
    scene.background = null;
    scene.add(dome);
    renderer.toneMapping = THREE.ACESFilmicToneMapping; renderer.toneMappingExposure = name === 'night' ? 2 : 1;
    scene.fog = new THREE.Fog(parseInt(mood.fogColor, 16), mood.fogNear, mood.fogFar);
    studioMoodResources = [dome, skyGeometry, skyMaterial, sky, clouds, cube, ambient, sun, moon];
    studioMoodState = { name, source: mood.source, textures: [mood.sky.path, mood.clouds.path, mood.environment.path], ambient: mood.ambient, sun: mood.sun, moon: mood.moon, time: mood.time };
};
