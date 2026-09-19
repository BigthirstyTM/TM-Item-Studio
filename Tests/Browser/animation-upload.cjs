const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs');

// Bespoke fixture contract: two XY triangles, one static at x=0 and one
// moving at x=2. Both meshes author identical LOCAL vertices (0..1); the +2 m
// placement travels in the moving entity's world transform. Since the shared-
// geometry payload change, authored buffers are local vertices and placement
// is enforced below through matrixWorld. Upload the archive normally; never
// inject scene/motion data.
const fixture = process.argv[2];
assert.ok(fixture, 'Usage: node animation-upload.cjs <bespoke-animation.Item.Gbx>');
const buffer = fs.readFileSync(fixture);

(async () => {
    const browser = await chromium.launch({ headless: true, args: ['--enable-unsafe-swiftshader'] });
    try {
        const page = await browser.newPage({ viewport: { width: 1280, height: 900 } });
        const errors = [];
        page.on('pageerror', error => errors.push(error.message));
        await page.goto(process.env.STUDIO_BASE_URL || 'http://127.0.0.1:5183/', { waitUntil: 'networkidle' });
        // Only the clock boundary is replaced. The real viewer callbacks,
        // uploaded GBX parser, component and Three renderer remain in use.
        await page.evaluate(() => {
            let next = 1;
            const frames = new Map();
            window.requestAnimationFrame = callback => { const id = next++; frames.set(id, callback); return id; };
            window.cancelAnimationFrame = id => frames.delete(id);
            window.advanceUploadTest = timestamp => {
                const callbacks = [...frames.values()];
                frames.clear();
                callbacks.forEach(callback => callback(timestamp));
            };
        });
        await page.getByLabel('Open item files').setInputFiles({
            name: 'bespoke-animation.Item.Gbx', mimeType: 'application/octet-stream', buffer
        });
        // Interval polling: requestAnimationFrame is replaced above so playback
        // advances only via advanceUploadTest, but waitForFunction's default
        // 'raf' polling would then never re-fire — an upload that outlasts the
        // first evaluation would hang for the full timeout.
        await page.waitForFunction(() => {
            if (typeof scene === 'undefined' || !scene) return false;
            let triangles = 0;
            scene.traverse(object => { if (object.isMesh && object.geometry.attributes.position?.count === 3) triangles++; });
            return triangles === 2;
        }, null, { polling: 50 });
        const observed = await page.evaluate(() => {
            const meshes = [];
            scene.traverse(object => { if (object.isMesh && object.geometry.attributes.position?.count === 3) meshes.push(object); });
            // Identify meshes by world placement, not implementation-assigned names,
            // groups, or part order: both parts author identical local vertices, so
            // only the entity transform (static at x=0, moving at x=2) tells them apart.
            scene.updateMatrixWorld(true);
            meshes.sort((a, b) => a.matrixWorld.elements[12] - b.matrixWorld.elements[12]);
            const snapshot = () => {
                scene.updateMatrixWorld(true);
                return meshes.map(mesh => Array.from({ length: 3 }, (_, i) =>
                    new THREE.Vector3().fromBufferAttribute(mesh.geometry.attributes.position, i)
                        .applyMatrix4(mesh.matrixWorld).toArray()).flat());
            };
            const authored = meshes.map(mesh => Array.from(mesh.geometry.attributes.position.array));
            advanceUploadTest(0);
            const before = snapshot();
            for (let time = 50; time <= 250; time += 50) advanceUploadTest(time);
            const after = snapshot();
            return { authored, before, after };
        });
        assert.deepEqual(observed.authored, [[0, 0, 0, 1, 0, 0, 0, 1, 0], [0, 0, 0, 1, 0, 0, 0, 1, 0]],
            'Fixture geometry changed (authored buffers are shared-geometry LOCAL vertices)');
        assert.deepEqual(observed.before[0], [0, 0, 0, 1, 0, 0, 0, 1, 0], 'Static base moved before the first frame');
        assert.deepEqual(observed.before[1], [2, 0, 0, 3, 0, 0, 2, 1, 0],
            'Moving geometry must be placed at x=2..3 by its entity world transform');
        assert.deepEqual(observed.after[0], observed.authored[0], 'Static base must remain fixed during playback');
        assert.ok(observed.after[1].some((value, i) => Math.abs(value - observed.before[1][i]) > 0.01), 'Moving geometry must change during playback');
        assert.deepEqual(errors, [], 'Upload/playback must not raise page errors');
        console.log('PASS: real GBX upload animates moving geometry while its static base stays fixed.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
