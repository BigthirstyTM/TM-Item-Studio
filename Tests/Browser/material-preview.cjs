const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const zlib = require('node:zlib');

// Texture-preview correctness contract, driven through the real published app:
// a real uploaded item for the export pipeline, plus synthetic scene payloads
// (renderStudioScene stays the real function) with known material links. The
// directory picker is stubbed deterministically because showDirectoryPicker
// needs a user gesture headless Chromium cannot provide; every function under
// test (selectStudioTextureDirectory, indexTextureDirectory, matching, loading,
// applyLocalTextures) runs unmodified. All texture bytes are synthetic images
// generated below — no DefaultTextures.zip content is used.
const fixture = process.argv[2];
assert.ok(fixture, 'Usage: node material-preview.cjs <light-capability.Item.Gbx>');

// --- Synthetic image fixtures (deterministic, generated in-test) ---
function crc32(bytes) {
    crc32.table ??= Array.from({ length: 256 }, (_, n) => {
        let c = n;
        for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
        return c >>> 0;
    });
    let crc = 0xffffffff;
    for (const byte of bytes) crc = crc32.table[(crc ^ byte) & 0xff] ^ (crc >>> 8);
    return (crc ^ 0xffffffff) >>> 0;
}
function pngChunk(type, data) {
    const out = Buffer.alloc(12 + data.length);
    out.writeUInt32BE(data.length); out.write(type, 4, 'ascii'); data.copy(out, 8);
    out.writeUInt32BE(crc32(out.subarray(4, 8 + data.length)), 8 + data.length);
    return out;
}
// Minimal 8-bit RGBA PNG, one flat color. Sizes double as per-material markers.
function syntheticPng(size, [r, g, b]) {
    const ihdr = Buffer.alloc(13);
    ihdr.writeUInt32BE(size, 0); ihdr.writeUInt32BE(size, 4); ihdr[8] = 8; ihdr[9] = 6;
    const raw = Buffer.alloc((1 + size * 4) * size);
    for (let y = 0; y < size; y++) for (let x = 0; x < size; x++) {
        const offset = 1 + (1 + size * 4) * y + x * 4;
        raw[offset] = r; raw[offset + 1] = g; raw[offset + 2] = b; raw[offset + 3] = 255;
    }
    return Buffer.concat([Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
        pngChunk('IHDR', ihdr), pngChunk('IDAT', zlib.deflateSync(raw, { level: 9 })), pngChunk('IEND', Buffer.alloc(0))]);
}
// Single-mipmap DXT5 (BC3) DDS: one opaque block per 4x4 face. Deterministic
// header + block bytes; no game data involved.
function syntheticDds(size, [r, g, b]) {
    const header = Buffer.alloc(128);
    header.write('DDS ', 0, 'ascii');
    header.writeUInt32LE(124, 4);                       // dwSize
    header.writeUInt32LE(0x8100f, 8);                   // CAPS|HEIGHT|WIDTH|PIXELFORMAT|LINEARSIZE
    header.writeUInt32LE(size, 12);                     // height
    header.writeUInt32LE(size, 16);                     // width
    header.writeUInt32LE(size * size, 20);              // linear size (DXT5 bytes)
    header.writeUInt32LE(32, 76);                       // ddspf.dwSize
    header.writeUInt32LE(0x4, 80);                      // DDPF_FOURCC
    header.write('DXT5', 84, 'ascii');
    header.writeUInt32LE(0x1000, 108);                  // DDSCAPS_TEXTURE
    const block = Buffer.alloc(16);
    block[0] = block[1] = 255;                          // alpha endpoints; all indices 0 → opaque
    const color = ((r >> 3) << 11) | ((g >> 2) << 5) | (b >> 3);
    block.writeUInt16LE(color, 10); block.writeUInt16LE(color, 12);
    return Buffer.concat([header, block]);
}
function syntheticAtiDds(fourCC) {
    const header = Buffer.alloc(128);
    header.write('DDS ', 0, 'ascii');
    header.writeUInt32LE(124, 4);
    header.writeUInt32LE(0x8100f, 8);
    header.writeUInt32LE(4, 12); header.writeUInt32LE(4, 16);
    header.writeUInt32LE(fourCC === 'ATI2' ? 16 : 8, 20);
    header.writeUInt32LE(32, 76); header.writeUInt32LE(0x4, 80); header.write(fourCC, 84, 'ascii');
    header.writeUInt32LE(0x1000, 108);
    const block = Buffer.alloc(fourCC === 'ATI2' ? 16 : 8);
    block[0] = block[1] = 255;
    if (fourCC === 'ATI2') block[8] = block[9] = 255;
    return Buffer.concat([header, block]);
}

// Matching expectations, all through the real viewer code paths:
// - link/name token: last path segment, backslash-insensitive, case-folded, `_asset[.N]` stripped
// - diffuse-suffix rank beats filename order; remaining ties by codepoint path order
// - distinct material identities sharing one candidate set stay neutral (ambiguous)
// - one identity referenced by several material slots still matches (no false ambiguity)
// - absent tokens, non-image files and unreadable DDS leave the neutral material
// - reads happen only for matched files, and only once a matching scene is rendered
const MATERIALS = [
    ['solid:0/material:0', 'Stadium\\Media\\Material\\RoadTech'],
    ['solid:1/material:0', 'Stadium\\Media\\Material\\RoadTech'],   // same identity, second slot
    ['solid:2/material:0', 'file://Media/Material/Signage_asset'], // _asset suffix stripped
    ['solid:3/material:0', null, 'Weird'],  // name fallback (no link)
    ['solid:4/material:0', 'FOLDER\\Deep\\CASEmixed'],             // case/separator normalization
    ['solid:5/material:0', 'x/multi'],                             // multi_d.png beats multi.png
    ['solid:6/material:0', 'a/clash'],                             // ambiguous pair...
    ['solid:7/material:0', 'b/clash'],                             // ...same candidate set
    ['solid:8/material:0', 'nothing_like_this'],                   // no candidate → neutral
    ['solid:9/material:0', 'corrupt'],                             // truncated DDS → neutral
    ['solid:10/material:0', 'dup'],                                // dup.png beats sub/dup.png
    ['solid:11/material:0', 'SWAP'],                               // stale-version probe
    ['solid:12/material:0', 'bc4'],                                // Trackmania ATI1/BC4 scalar map
    ['solid:13/material:0', 'bc5'],                                // Trackmania ATI2/BC5 packed map
    ['solid:14/material:0', null, 'TM_FixtureOnly_CustomMaterial_asset'], // Blender asset material-name fallback
    ['solid:15/material:0', null, 'GlassWaterWall']                         // glass _T base fallback
];
const EXPECTED_FILES = {
    'solid:0/material:0': 'roadtech_d.png', 'solid:1/material:0': 'roadtech_d.png',
    'solid:2/material:0': 'signage_d.dds', 'solid:3/material:0': 'weird.webp',
    'solid:4/material:0': 'sub/folder/casemixed.png', 'solid:5/material:0': 'multi_d.png',
    'solid:6/material:0': null, 'solid:7/material:0': null, 'solid:8/material:0': null,
    'solid:9/material:0': 'corrupt.dds', 'solid:10/material:0': 'dup.png', 'solid:11/material:0': 'fastswap.png',
    'solid:12/material:0': 'bc4_d.dds', 'solid:13/material:0': 'bc5_d.png', 'solid:14/material:0': 'custommaterial_d.png',
    'solid:15/material:0': 'glasswaterwall_t.dds'
};
const EXPECTED_STATUS = {
    'solid:6/material:0': 'ambiguous', 'solid:7/material:0': 'ambiguous', 'solid:8/material:0': 'absent'
};
const EXPECTED_MAPS = { // applied texture image dimensions, per material slot
    'solid:0/material:0': [3, 3], 'solid:1/material:0': [3, 3], 'solid:2/material:0': [4, 4],
    'solid:3/material:0': [5, 5], 'solid:4/material:0': [6, 6], 'solid:5/material:0': [7, 7],
    'solid:6/material:0': null, 'solid:7/material:0': null, 'solid:8/material:0': null,
    'solid:9/material:0': null, 'solid:10/material:0': [2, 2], 'solid:11/material:0': [4, 4],
    'solid:12/material:0': [4, 4], 'solid:13/material:0': [4, 4], 'solid:14/material:0': [4, 4],
    'solid:15/material:0': [4, 4]
};
const MUST_READ = ['roadtech_d.png', 'roadtech_n.png', 'roadtech_r.png', 'signage_d.dds', 'weird.webp', 'Sub/Folder/CaSeMiXeD.PNG',
    'multi_d.png', 'dup.png', 'corrupt.dds', 'slowload.png', 'fastswap.png', 'bc4_d.dds', 'bc5_d.png', 'bc5_n.dds',
    'custommaterial_d.png', 'glasswaterwall_t.dds'];
const NEVER_READ = ['multi.png', 'sub/dup.png', 'clash_a.png', 'clash_d.png', 'readme.txt'];
const FILE_COUNT = 21; // 20 images + readme.txt (indexed but never matched)

function scenePayload(swapLink) {
    const materials = MATERIALS.map(([slot, link, name]) => ({ path: slot, index: 0, sourceId: 1,
        name: name ?? link ?? 'fixture', state: 'present', representation: 'CustomMaterial',
        gameMaterialName: name ?? null, gameMaterialLink: link ?? null,
        // A game material may carry a selectable overlay tint. The viewport
        // must keep the diffuse texture's real colors when that tint is green.
        color: slot === 'solid:0/material:0' ? [0, 255, 0]
            : slot === 'solid:14/material:0' ? [255, 255, 255] : null }));
    materials[11].gameMaterialLink = swapLink;
    const part = (slot, offset) => ({ name: `part-${slot}`, positions: [offset, 0, 0, offset + 1, 0, 0, offset, 1, 0],
        uvs: [0, 0, 1, 0, 0, 1],
        indices: [0, 1, 2], mappings: [{ materialIndex: null, lodMask: null, materialPath: slot }] });
    // 13 meshes: slot 0 appears twice to pin "the right meshes" per material.
    const parts = MATERIALS.map(([slot], index) => part(slot, index)).concat([part('solid:0/material:0', 100)]);
    return { materials, parts, playing: true };
}

(async () => {
    const work = fs.mkdtempSync(path.join(os.tmpdir(), 'studio-material-preview-'));
    const browser = await chromium.launch({ headless: true, ...(process.env.CHROMIUM_EXECUTABLE ? { executablePath: process.env.CHROMIUM_EXECUTABLE } : {}), args: ['--enable-unsafe-swiftshader'] });
    try {
        const page = await browser.newPage({ acceptDownloads: true, viewport: { width: 1440, height: 1000 } });
        page.setDefaultTimeout(45000);
        const errors = [], warnings = [];
        page.on('pageerror', error => errors.push(error.message));
        page.on('console', message => { if (message.type() === 'warning') warnings.push(message.text()); });
        await page.goto(process.env.STUDIO_BASE_URL || 'http://127.0.0.1:5183/', { waitUntil: 'domcontentloaded' });
        await page.evaluate(() => {
            const originalRender = window.renderStudioScene;
            window.__lastAppScenePayload = null;
            window.renderStudioScene = payload => {
                window.__lastAppScenePayload = payload;
                return originalRender(payload);
            };
        });
        await page.getByLabel('Open item files').setInputFiles(fixture);
        await page.getByRole('button', { name: /Export selected file/ }).waitFor().catch(async error => {
            console.error('Fixture load state: ' + await page.locator('body').innerText());
            console.error('Buttons: ' + JSON.stringify(await page.locator('button').evaluateAll(buttons => buttons.filter(button => button.textContent.includes('Export')).map(button => ({ text: button.textContent, rect: button.getBoundingClientRect().toJSON(), visibility: getComputedStyle(button).visibility, aria: button.getAttribute('aria-label') })))));
            throw error;
        });
        await page.waitForFunction(() => typeof renderer !== 'undefined' && renderer?.domElement.isConnected);
        assert.ok(await page.evaluate(() => (window.__lastAppScenePayload?.geometryDefinitions ?? [])
            .some(definition => (definition.uvsBytes?.byteLength ?? definition.uvsBytes?.length ?? 0) > 0)),
            'the imported scene payload must carry UV0 buffers for textured meshes');
        console.log('PASS: imported scene payload carries UV0 buffers.');
        const mappingAudit = await page.evaluate(() => (window.__lastAppScenePayload?.parts ?? [])
            .filter(part => typeof part.path === 'string' && part.path.includes('/visual:'))
            .map(part => ({ path: part.path, mappings: part.mappings ?? [] })));
        assert.ok(mappingAudit.length > 1 && mappingAudit.every(part => part.mappings.length === 1),
            'each visual mesh must receive only its own shaded material mapping');
        console.log('PASS: each imported visual mesh carries its own material mapping.');

        // Export bytes must be identical before and after every preview step below.
        const exportTo = async filename => {
            const event = page.waitForEvent('download');
            await page.getByRole('button', { name: /Export selected file/ }).click();
            const file = path.join(work, filename);
            await (await event).saveAs(file);
            return fs.readFileSync(file);
        };
        const baseline = await exportTo('material-preview-baseline.Item.Gbx');

        // WebP fixture: encoded by the browser under test, kept in-page only.
        const webp = await page.evaluate(() => new Promise((resolve, reject) => {
            const canvas = document.createElement('canvas'); canvas.width = canvas.height = 5;
            const context = canvas.getContext('2d'); context.fillStyle = '#7a4ab8'; context.fillRect(0, 0, 5, 5);
            canvas.toBlob(async blob => {
                if (!blob) return reject(new Error('WebP encoding unavailable'));
                resolve(Array.from(new Uint8Array(await blob.arrayBuffer())));
            }, 'image/webp');
        }));
        assert.ok(String.fromCharCode(...webp.slice(0, 4)) === 'RIFF' && String.fromCharCode(...webp.slice(8, 12)) === 'WEBP',
            'headless Chromium could not encode a WebP fixture');

        // Deterministic showDirectoryPicker stub: read-only handles, every read recorded.
        await page.evaluate(files => {
            const state = window.__textureStubState = { reads: [], picks: 0, pickArgs: null };
            const root = { kind: 'directory', name: 'SyntheticTextures', children: new Map() };
            for (const [path, spec] of Object.entries(files)) {
                const segments = path.split('/'), filename = segments.pop();
                let folder = root;
                for (const segment of segments) {
                    if (!folder.children.has(segment)) folder.children.set(segment, { kind: 'directory', name: segment, children: new Map() });
                    folder = folder.children.get(segment);
                }
                folder.children.set(filename, { kind: 'file', getFile: async () => {
                    state.reads.push(path);
                    if (spec.delay) await new Promise(resolve => setTimeout(resolve, spec.delay));
                    return new File([Uint8Array.from(spec.bytes)], filename, { type: spec.type });
                } });
            }
            const wire = node => {
                node.entries = async function* () { for (const [name, child] of node.children) yield [name, child]; };
                for (const child of node.children.values()) if (child.kind === 'directory') wire(child);
            };
            wire(root);
            window.showDirectoryPicker = async (...args) => { state.picks++; state.pickArgs = args; return root; };
        }, {
            'roadtech_d.png': { type: 'image/png', bytes: [...syntheticPng(3, [200, 40, 40])] },
            'roadtech_n.png': { type: 'image/png', bytes: [...syntheticPng(3, [128, 128, 255])] },
            'roadtech_r.png': { type: 'image/png', bytes: [...syntheticPng(3, [100, 180, 0])] },
            'signage_d.dds': { type: 'image/vnd.ms-dds', bytes: [...syntheticDds(4, [40, 80, 220])] },
            'weird.webp': { type: 'image/webp', bytes: webp },
            'Sub/Folder/CaSeMiXeD.PNG': { type: 'image/png', bytes: [...syntheticPng(6, [20, 160, 170])] },
            'multi.png': { type: 'image/png', bytes: [...syntheticPng(9, [120, 120, 120])] },
            'multi_d.png': { type: 'image/png', bytes: [...syntheticPng(7, [130, 130, 130])] },
            'clash_d.png': { type: 'image/png', bytes: [...syntheticPng(5, [230, 140, 30])] },
            'clash_a.png': { type: 'image/png', bytes: [...syntheticPng(5, [230, 140, 30])] },
            'corrupt.dds': { type: 'image/vnd.ms-dds', bytes: [...syntheticDds(4, [90, 30, 160]).subarray(0, 136)] }, // truncated mid-block
            'dup.png': { type: 'image/png', bytes: [...syntheticPng(2, [40, 190, 60])] },
             'sub/dup.png': { type: 'image/png', bytes: [...syntheticPng(8, [50, 200, 70])] },
             'slowload.png': { type: 'image/png', bytes: [...syntheticPng(8, [170, 30, 200])], delay: 700 },
             'fastswap.png': { type: 'image/png', bytes: [...syntheticPng(4, [30, 200, 200])] },
             'bc4_d.dds': { type: 'image/vnd.ms-dds', bytes: [...syntheticAtiDds('ATI1')] },
             'bc5_d.png': { type: 'image/png', bytes: [...syntheticPng(4, [30, 200, 200])] },
             'bc5_n.dds': { type: 'image/vnd.ms-dds', bytes: [...syntheticAtiDds('ATI2')] },
             'custommaterial_d.png': { type: 'image/png', bytes: [...syntheticPng(4, [0, 235, 0])] },
             'glasswaterwall_t.dds': { type: 'image/vnd.ms-dds', bytes: [...syntheticDds(4, [40, 200, 220])] },
             'readme.txt': { type: 'text/plain', bytes: Array.from(new TextEncoder().encode('synthetic fixture index - not a texture')) }
        });

        // The real panel flow first, while the viewer state is still entirely
        // app-managed: click through Home.razor, confirm the status line and
        // the read-only picker call.
        await page.getByRole('button', { name: /Materials & LOD/ }).click();
        await page.getByRole('button', { name: 'Choose local texture folder' }).click();
        await page.getByText(`Using local folder SyntheticTextures (${FILE_COUNT} files indexed).`).waitFor();
        assert.equal(await page.evaluate(() => window.__textureStubState.pickArgs?.[0]?.mode), 'read',
            'picker must be opened read-only');

        // Lazy invariant: indexing the folder (UI and direct) reads metadata
        // only; no texture bytes before a matching material is rendered.
        assert.equal(await page.evaluate(() => window.__textureStubState.reads.length), 0,
            'indexing must not read texture bytes before a matching material is rendered');
        const selection = await page.evaluate(() => selectStudioTextureDirectory());
        assert.deepEqual(selection, { name: 'SyntheticTextures', fileCount: FILE_COUNT });
        assert.equal(await page.evaluate(() => window.__textureStubState.picks), 2);
        assert.equal(await page.evaluate(() => window.__textureStubState.reads.length), 0,
            're-indexing must still not read texture bytes');

        // Stale-version probe: v1 starts a 700ms read, v2 re-renders immediately;
        // the slow texture must be discarded, the fast one applied.
        await page.evaluate(payload => renderStudioScene(payload), scenePayload('deep/slowload'));
        await page.evaluate(payload => renderStudioScene(payload), scenePayload('deep/fastswap'));
        await page.waitForFunction(expected => {
            const seen = {};
            for (const group of [staticGroup, movingGroup]) group?.traverse(mesh => {
                if (!mesh.isMesh) return;
                for (const mapping of mesh.userData.mappings ?? [])
                    seen[mapping.materialPath] = mesh.material.map ? [mesh.material.map.image.width, mesh.material.map.image.height] : null;
            });
            return Object.entries(expected).every(([key, value]) => JSON.stringify(seen[key] ?? null) === JSON.stringify(value));
        }, EXPECTED_MAPS);
        const animatedBefore = await page.evaluate(() => {
            let offset = null;
            staticGroup.traverse(mesh => {
                if (mesh.isMesh && mesh.userData.mappings?.some(mapping => mapping.materialPath === 'solid:15/material:0'))
                    offset = [mesh.material.map?.offset?.x ?? null, mesh.material.map?.offset?.y ?? null];
            });
            return offset;
        });
        await page.waitForTimeout(150);
        const animatedAfter = await page.evaluate(() => {
            let offset = null;
            staticGroup.traverse(mesh => {
                if (mesh.isMesh && mesh.userData.mappings?.some(mapping => mapping.materialPath === 'solid:15/material:0'))
                    offset = [mesh.material.map?.offset?.x ?? null, mesh.material.map?.offset?.y ?? null];
            });
            return offset;
        });
        assert.notDeepEqual(animatedAfter, animatedBefore, 'water/glass material texture did not animate in the viewport');
        console.log('PASS: water/glass texture scrolls in the live viewport.');
        console.log('PASS: matched textures applied to the right meshes; neutral fallback for absent/ambiguous/unreadable.');

        assert.deepEqual(await page.evaluate(() => {
            let material;
            let hasUv = false;
            staticGroup.traverse(mesh => {
                if (mesh.isMesh && mesh.userData.mappings?.some(mapping => mapping.materialPath === 'solid:0/material:0')) {
                    material ??= mesh.material;
                    hasUv ||= Boolean(mesh.geometry.getAttribute('uv'));
                }
            });
            return {
                map: [material.map.image.width, material.map.image.height],
                normal: [material.normalMap.image.width, material.normalMap.image.height],
                roughness: [material.roughnessMap.image.width, material.roughnessMap.image.height],
                metalness: [material.metalnessMap.image.width, material.metalnessMap.image.height],
                baseEncoding: material.map.encoding,
                normalEncoding: material.normalMap.encoding,
                hasUv
            };
        }), {
            map: [3, 3], normal: [3, 3], roughness: [3, 3], metalness: [3, 3],
            baseEncoding: 3001, normalEncoding: 3000, hasUv: true
        }, 'Trackmania _D/_N/_R maps were not assigned with the expected color spaces');
        console.log('PASS: Trackmania _D/_N/_R maps bind to the PBR material with sRGB base color.');
        assert.equal(await page.evaluate(() => {
            let color;
            staticGroup.traverse(mesh => {
                if (mesh.isMesh && mesh.userData.mappings?.some(mapping => mapping.materialPath === 'solid:0/material:0'))
                    color ??= mesh.material.color.getHexString();
            });
            return color;
        }), 'ffffff', 'authored material color overlay tinted the textured preview');
        console.log('PASS: textured materials ignore authored color overlays in the viewport.');
        assert.deepEqual(await page.evaluate(() => {
            let material;
            staticGroup.traverse(mesh => {
                if (mesh.isMesh && mesh.userData.mappings?.some(mapping => mapping.materialPath === 'solid:14/material:0'))
                    material ??= mesh.material;
            });
            const shader = { fragmentShader: 'diffuseColor *= texelColor;' };
            material.onBeforeCompile(shader);
            return { color: material.color.getHexString(), shader: shader.fragmentShader };
        }), { color: 'ffffff', shader: 'diffuseColor *= vec4(vec3(texelColor.g), texelColor.a);' },
            'custom color-mask texture did not use its authored color');
        console.log('PASS: custom color-mask textures use the authored item color.');
        assert.deepEqual(await page.evaluate(() => {
            const result = {};
            staticGroup.traverse(mesh => {
                const path = mesh.userData.mappings?.[0]?.materialPath;
                if (!mesh.isMesh || !path) return;
                if (path === 'solid:12/material:0') result.bc4 = [mesh.material.map?.image?.width, mesh.material.map?.image?.height];
                if (path === 'solid:13/material:0') result.bc5 = [mesh.material.map?.image?.width, mesh.material.map?.image?.height,
                    mesh.material.normalMap?.image?.width, mesh.material.normalMap?.image?.height];
            });
            return result;
        }), { bc4: [4, 4], bc5: [4, 4, 4, 4] }, 'ATI1/ATI2 DDS maps were not decoded for Trackmania materials');
        console.log('PASS: Trackmania ATI1/ATI2 (BC4/BC5) maps decode to usable textures.');

        const info = await page.evaluate(() => getStudioTexturePreviewInfo());
        assert.equal(info.directory, 'SyntheticTextures');
        assert.equal(info.fileCount, FILE_COUNT);
        const matches = Object.fromEntries(info.matches.map(match => [match.materialPath, match]));
        assert.equal(info.matches.length, MATERIALS.length);
        for (const [slot, file] of Object.entries(EXPECTED_FILES))
            assert.equal(matches[slot]?.file, file, `wrong matched file for ${slot}`);
        for (const [slot, status] of Object.entries(EXPECTED_STATUS))
            assert.equal(matches[slot]?.status, status, `wrong match status for ${slot}`);
        assert.equal(matches['solid:3/material:0']?.token, 'weird', 'name-fallback token not derived');
        assert.equal(matches['solid:2/material:0']?.token, 'signage', '_asset suffix not stripped');
        assert.equal(matches['solid:4/material:0']?.token, 'casemixed', 'link token not case/backslash normalized');
        assert.equal(matches['solid:14/material:0']?.token, 'custommaterial', 'Blender TM_/compound material name fallback not derived');
        assert.equal(matches['solid:15/material:0']?.file, 'glasswaterwall_t.dds', 'glass _T texture was not used as a base fallback');
        console.log('PASS: deterministic matching report (tokens, statuses, files) matches the rules.');

        const manifestMatch = await page.evaluate(() => {
            const handle = { getFile: async () => new File([new Uint8Array([1])], 'manifest_d.png') };
            textureFiles.set('manifest/manifest_d.png', handle);
            textureManifest = { version: 1, materials: [{
                link: 'Stadium\\Media\\Material\\ManifestMaterial', environment: 'Stadium',
                aliases: ['manifestmaterial'], colorable: true, targetColor: [0, 1, 0],
                textures: { base: 'manifest/manifest_d.png' }
            }] };
            const result = matchTextureFiles([{
                path: 'manifest-slot', gameMaterialLink: 'Stadium\\Media\\Material\\ManifestMaterial'
            }]).get('manifest-slot');
            textureManifest = null;
            textureFiles.delete('manifest/manifest_d.png');
            return { status: result?.status, file: result?.file, colorable: result?.colorable };
        });
        assert.deepEqual(manifestMatch, { status: 'matched', file: 'manifest/manifest_d.png', colorable: false },
            'curated material manifest did not resolve the exact full material link or over-classify a regular material');
        console.log('PASS: curated material manifest resolves exact full links.');

        await page.waitForTimeout(1100); // let the stale 700ms load land and be discarded
        assert.deepEqual(await page.evaluate(() => {
            const seen = {};
            for (const group of [staticGroup, movingGroup]) group?.traverse(mesh => {
                if (!mesh.isMesh) return;
                for (const mapping of mesh.userData.mappings ?? [])
                    seen[mapping.materialPath] = mesh.material.map ? [mesh.material.map.image.width, mesh.material.map.image.height] : null;
            });
            return seen;
        }), EXPECTED_MAPS, 'a stale in-flight texture overwrote a newer render');
        assert.equal(await page.evaluate(() => getStudioViewerDebugInfo().meshes), MATERIALS.length + 1);

        const reads = await page.evaluate(() => window.__textureStubState.reads);
        const readSet = new Set(reads);
        for (const file of MUST_READ) assert.ok(readSet.has(file), `${file} was never read`);
        for (const file of NEVER_READ) assert.ok(!readSet.has(file), `${file} must not be read`);
        assert.ok(warnings.some(text => text.includes('solid:9/material:0')), 'truncated DDS did not fail through the warn path');
        console.log('PASS: only matched files were read; unreadable DDS failed neutrally; stale load discarded.');

        // Synthetic payloads render last: they replace the app-managed scene,
        // so the app's own re-render (which relies on its cached shared
        // geometry epoch) must not be triggered afterwards.
        const after = await exportTo('material-preview-after.Item.Gbx');
        assert.deepEqual(after, baseline, 'texture previewing changed exported bytes');
        console.log('PASS: export bytes unchanged after directory selection and preview rendering.');

        assert.deepEqual(errors, [], 'page errors during texture previewing');
        console.log('PASS: no page errors.');
    } finally {
        await browser.close();
        fs.rmSync(work, { recursive: true, force: true });
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
