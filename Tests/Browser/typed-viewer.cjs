const { chromium } = require('playwright');
const assert = require('node:assert/strict');

// The production published route and production viewer are used; no copied evaluator or fake Three implementation.
const syntheticItem = Buffer.from('R0JYBgBCVUNSACAALgAAAAABAAAAAAAAAAQAAAAIAAAAFQHeyvoRAAA=', 'base64');
(async () => {
    const browser = await chromium.launch({ headless: true, args: ['--enable-unsafe-swiftshader'] });
    try {
        const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
        const errors = [];
        page.on('pageerror', error => errors.push(error.message));
        await page.goto(process.env.STUDIO_BASE_URL || 'http://127.0.0.1:5183/', { waitUntil: 'networkidle' });
        await page.locator('input[type=file]').first().setInputFiles({ name: 'typed.Item.Gbx', mimeType: 'application/octet-stream', buffer: syntheticItem });
        await page.waitForFunction(() => typeof renderer !== 'undefined' && renderer?.domElement.isConnected);
        const results = await page.evaluate(() => {
            dispose3DViewer();
            // Deterministic timestamps drive the real production animation callback and renderer.
            let nextFrame = 1;
            const frames = new Map();
            window.requestAnimationFrame = callback => { const id = nextFrame++; frames.set(id, callback); return id; };
            window.cancelAnimationFrame = id => frames.delete(id);
            const tick = timestamp => { const callbacks = [...frames.values()]; frames.clear(); callbacks.forEach(callback => callback(timestamp)); };
            const advance = milliseconds => { tick(0); for (let t = 50; t < milliseconds; t += 50) tick(t); if (milliseconds) tick(milliseconds); };
            const messages = [];
            init3DViewer('threeContainer', { invokeMethodAsync: (...args) => { messages.push(args); return Promise.resolve(); } });
            const results = [];
            const require = (value, message) => { if (!value) throw new Error(message); };
            const near = (actual, expected, message = 'Numerical mismatch') => require(actual.length === expected.length
                && actual.every((value, i) => Math.abs(value - expected[i]) < 1e-5), `${message}: ${actual} != ${expected}`);
            const check = (name, action) => { action(); results.push(name); };
            const identity = new THREE.Matrix4().toArray();
            const track = (duration = 100, ease = 1, reverse = false) => ({ isDuration: true, keys: [{ ease, reverse, durationMilliseconds: duration }] });
            const empty = () => ({ isDuration: true, keys: [] });
            const fields = (overrides = {}) => ({ translationAxis: 0, translationMin: 0, translationMax: 10,
                rotationAxis: 2, angleMinDegrees: 0, angleMaxDegrees: 0, translation: track(), rotation: empty(), ...overrides });
            const motion = (overrides = {}) => ({ path: 'constraint', childPath: 'child', parentPath: null,
                childRest: identity, parentRest: identity, fields: fields(), ...overrides });
            const part = (path = 'mesh', entityPath = 'child', overrides = {}) => ({ path, entityPath,
                positions: [1, 0, 0, 0, 1, 0, 0, 0, 0], normals: [0, 0, 1, 0, 0, 1, 0, 0, 1],
                indices: [0, 1, 2], uvs: [.1, .2, .3, .4, .5, .6], ...overrides });
            const mesh = path => { let found; scene.traverse(obj => { if (obj.isMesh && obj.userData.path === path) found = obj; }); return found; };
            const vertex = path => { scene.updateMatrixWorld(true); const obj = mesh(path); return new THREE.Vector3().fromBufferAttribute(obj.geometry.attributes.position, 0).applyMatrix4(obj.matrixWorld).toArray(); };
            const reject = action => { let rejected = false; try { action(); } catch { rejected = true; } require(rejected, 'Expected unsupported/invalid payload rejection'); };

            check('duration and endpoint storage produce equivalent motion without mutation', () => {
                for (const isDuration of [true, false]) {
                    const source = { isDuration, keys: [{ ease: 1, reverse: false, durationMilliseconds: 1000 },
                        { ease: 1, reverse: true, durationMilliseconds: isDuration ? 1000 : 2000 }] };
                    const before = JSON.stringify(source);
                    renderStudioScene({ parts: [part()], motions: [motion({ fields: fields({ translation: source }) })] });
                    advance(1500); near(vertex('mesh'), [6, 0, 0]);
                    const compiled = timeline(source);
                    for (const [seconds, expected] of [[0, 0], [.5, 5], [1, 10], [1.5, 5], [2, 0]])
                        near([sampleTimeline(compiled, seconds, 0, 0, 10)], [expected]);
                    require(JSON.stringify(source) === before, 'Sampling rewrote stored flag or times');
                }
            });

            check('endpoint duplicates, decreases, and normalized limits match native loading', () => {
                for (const middle of [500, 1000]) {
                    const source = { isDuration: false, keys: [
                        { ease: 1, reverse: false, durationMilliseconds: 1000 },
                        { ease: 0, reverse: false, durationMilliseconds: middle },
                        { ease: 1, reverse: true, durationMilliseconds: middle + 1000 }] };
                    const compiled = timeline(source);
                    for (const [seconds, expected] of [[1, 10], [1.5, 5], [2, 0]])
                        near([sampleTimeline(compiled, seconds, 0, 0, 10)], [expected]);
                }
                const endpoints = times => ({ isDuration: false, keys: times.map(durationMilliseconds => ({ ease: 1, reverse: false, durationMilliseconds })) });
                require(timeline(endpoints([2147483647, 2147483647])).total === 2147483647, 'Raw sum used for duration validation');
                reject(() => timeline(endpoints([2147483647, 0, 2147483647])));
                reject(() => timeline(endpoints([2147483648])));
                near([sampleTimeline(timeline({ isDuration: true, keys: [
                    { ease: 1, reverse: false, durationMilliseconds: 1000 },
                    { ease: 1, reverse: true, durationMilliseconds: 2000 }] }), 1.5, 0, 0, 10)], [7.5]);
                for (const isDuration of [false, true]) {
                    near([sampleTimeline(timeline({ isDuration, keys: [] }), 1, 0, 2, 10)], [2]);
                    near([sampleTimeline(timeline({ isDuration, keys: track(0).keys }), 1, 0, 2, 10)], [0]);
                }
            });
            check('static geometry and source buffers survive time and camera-only framing', () => {
                const positions = [10, 20, 30, 12, 20, 30, 10, 22, 30];
                renderStudioScene({ parts: [part('static', 'static', { positions, isMoving: true })] });
                advance(1000);
                near(vertex('static'), [10, 20, 30]);
                near(staticGroup.position.toArray(), [0, 0, 0]); near(movingGroup.position.toArray(), [0, 0, 0]);
                near(controls.target.toArray(), [11, 21, 30]);
                near(Array.from(mesh('static').geometry.attributes.normal.array), [0, 0, 1, 0, 0, 1, 0, 0, 1]);
                near(Array.from(mesh('static').geometry.attributes.uv.array), [.1, .2, .3, .4, .5, .6]);
                require(camera.near > 0 && camera.far > camera.position.distanceTo(controls.target), 'Clipping does not contain model');
                setMotionPreview({ isOscillating: true, minAngle: 0, maxAngle: 180 }); setAnimationAxis('x'); tick(1050);
                near(vertex('static'), [10, 20, 30]);
            });
            check('actual target moves, unrelated interleaved static and collision ownership remain stable', () => {
                renderStudioScene({ parts: [part('unrelated', 'other', { positions: [50, 0, 0, 51, 0, 0, 50, 1, 0] }),
                    part(), part('collision', 'child', { isCollision: true })], motions: [motion()] });
                advance(50); near(vertex('mesh'), [6, 0, 0]); near(vertex('collision'), [6, 0, 0]); near(vertex('unrelated'), [50, 0, 0]);
                require(collisionGroup.visible === false, 'Collision should start hidden'); toggleLayer('collision'); toggleLayer('meshes');
                require(collisionGroup.visible && !movingGroup.visible, 'Mesh visibility also hides collision'); toggleLayer('meshes');
            });
            check('nonidentity parent and child rest transforms compose without framing offsets', () => {
                const rest = new THREE.Matrix4().makeTranslation(10, 0, 0).multiply(new THREE.Matrix4().makeRotationZ(Math.PI / 2)).toArray();
                renderStudioScene({ parts: [part('rest', 'child', { positions: [10, 1, 0, 9, 0, 0, 10, 0, 0] })], motions: [motion({
                    parentPath: null, parentRest: new THREE.Matrix4().identity().toArray(), childRest: rest,
                    fields: fields({ translationMin: 2, translationMax: 2, translation: empty(), angleMinDegrees: 90, rotation: empty() }) })] });
                near(vertex('rest'), [9, 2, 0]); near(staticGroup.position.toArray(), [0, 0, 0]);
            });
            check('parent dependencies evaluate before children regardless of payload order', () => {
                const parentRest = new THREE.Matrix4().makeTranslation(10, 0, 0).multiply(new THREE.Matrix4().makeRotationZ(Math.PI / 2));
                const childRest = parentRest.clone().multiply(new THREE.Matrix4().makeTranslation(3, 0, 0));
                renderStudioScene({ parts: [part('chain', 'child', { positions: [10, 4, 0, 9, 3, 0, 10, 3, 0] })], motions: [
                    motion({ parentPath: 'parent', childRest: childRest.toArray(), parentRest: parentRest.toArray(), fields: fields({ translationAxis: 1, translationMin: 4, translation: empty() }) }),
                    motion({ path: 'parent-constraint', childPath: 'parent', childRest: parentRest.toArray(), fields: fields({ translationMin: 2, translation: empty() }) })] });
                near(vertex('chain'), [6, 6, 0]);
            });
            check('independent rotation/translation timing and reversed keys', () => {
                renderStudioScene({ parts: [part()], motions: [motion({ fields: fields({ translationMin: 3, translationMax: 9,
                    translation: track(100, 1, true), angleMaxDegrees: 180, rotation: track(200) }) })] });
                advance(25);
                near(vertex('mesh'), [7.5 + Math.cos(Math.PI / 8), Math.sin(Math.PI / 8), 0]);
            });
            check('empty and zero-duration timelines stay distinct; easing and reversal match explicit samples', () => {
                require(sampleTimeline(timeline(empty()), 5, 0, 3, 9) === 3, 'Empty must use minimum');
                require(sampleTimeline(timeline(track(0)), 5, 0, 3, 9) === 0, 'Zero duration must return zero signal');
                require(sampleTimeline(timeline(track(100, 0, true)), 0, 0, 3, 9) === 9, 'Reverse constant must use maximum');
                [0, .25, .0625, .4375, .125].forEach((expected, ease) => {
                    near([sampleTimeline(timeline(track(100, ease)), .025, 0, 0, 1)], [expected]);
                    near([sampleTimeline(timeline(track(100, ease, true)), .025, 0, 0, 1)], [1 - expected]);
                });
                const zeroFirst = { isDuration: false, keys: [{ ease: 0, reverse: true, durationMilliseconds: 0 }, ...track(100, 0).keys] };
                require(sampleTimeline(timeline(zeroFirst), 0, 0, 3, 9) === 3, 'Zero key was not skipped');
            });
            check('microsecond clock handles exact, phased and neighbouring loop boundaries', () => {
                const linear = timeline(track(100));
                for (const seconds of [.3, .6, .7, 1.001]) {
                    if (seconds !== 1.001) for (const offset of [0, 1]) require(sampleTimeline(linear, seconds, offset, 0, 1) === 0, 'Exact 100ms boundary');
                }
                require(sampleTimeline(linear, .3 - .000001, 0, 0, 1) > .9999, 'One microsecond before wraps early');
                near([sampleTimeline(linear, .3 + .000001, 0, 0, 1)], [.00001]);
                require(sampleTimeline(timeline(track(3)), .0988, .4, 0, 1) === 1 / 3, 'Phased 100ms sample expected one millisecond into 3ms cycle');
                const step = timeline({ isDuration: true, keys: [...track(100, 0).keys, ...track(100, 0, true).keys] });
                require(sampleTimeline(step, .0988, .006, 0, 1) === 1, 'Fractional phase must land exactly at 100ms key boundary');
                require(sampleTimeline(step, .55, .25, 0, 1) === 0 && sampleTimeline(step, .65, .25, 0, 1) === 1, 'Phased half-open keys');
                require(Number.isFinite(sampleTimeline(linear, Number.MAX_VALUE, .4, 0, 1)), 'Huge finite time overflow');
                const micro = timeline(track(3));
                for (const [seconds, expected] of [[.098799, .000999 / .003], [.098801, .001001 / .003], [1e13, 1 / 3], [1e20, 1 / 3], [Number.MAX_VALUE, 2 / 3]])
                    near([sampleTimeline(micro, seconds, seconds < 1 ? .4 : 0, 0, 1)], [expected]);
                require(sampleTimeline(linear, .00000049, 0, 0, 1) === 0, 'Sub-microsecond precision contract');
                near([sampleTimeline(linear, .0000005, 0, 0, 1)], [.00001]);
            });
            check('pause, speed and phase updates preserve authored data and camera', () => {
                renderStudioScene({ parts: [part()], motions: [motion()] });
                setAnimationPlaying(false); advance(50); near(vertex('mesh'), [1, 0, 0]);
                setAnimationPlaying(true); setAnimationSpeed(2); tick(75); near(vertex('mesh'), [6, 0, 0]); setAnimationSpeed(1);
                camera.position.set(21, 22, 23); const target = controls.target.toArray();
                setTypedMotionPreview({ previewPhase01: .25 }); near(vertex('mesh'), [3.5, 0, 0]);
                near(camera.position.toArray(), [21, 22, 23]); near(controls.target.toArray(), target);
            });
            check('legacy kinematic parts are staged in the moving group and animate', () => {
                renderStudioScene({ playing: true, hasTranslationMotion: false, isOscillating: true,
                    minAngle: 0, maxAngle: 90, animationPeriodSeconds: 1,
                    parts: [part('legacy-moving', null, { isMoving: true })] });
                advance(250);
                require(movingGroup.children.length === 1, 'Legacy moving part was staged as static');
                const pose = vertex('legacy-moving');
                require(Math.abs(pose[0] - 1) > 0.01 || Math.abs(pose[1]) > 0.01, 'Legacy moving part did not animate');
            });
            check('invalid modes, keys, matrices, duplicate writers and cycles reject atomically', () => {
                renderStudioScene({ parts: [part()], motions: [motion()] }); const previous = mesh('mesh');
                const bad = [motion({ fields: fields({ translation: null }) }), motion({ fields: fields({ translationAxis: 4 }) }),
                    motion({ fields: fields({ translation: { isDuration: null, keys: [] } }) }), motion({ fields: fields({ rotation: track(100, 5) }) }),
                    motion({ childRest: new Array(16).fill(0) }), motion({ fields: fields({ translation: track(-1) }) })];
                for (const item of bad) reject(() => setTypedMotionPreview({ motions: [item] }));
                reject(() => setTypedMotionPreview({ motions: [motion(), motion({ path: 'second' })] }));
                reject(() => setTypedMotionPreview({ motions: [motion({ parentPath: 'child' })] }));
                const beforeUnresolved = mesh('mesh');
                reject(() => setTypedMotionPreview({ motions: [motion({ parentPath: 'missing-parent' })] }));
                require(mesh('mesh') === beforeUnresolved, 'Unresolved parent replaced live scene');
                reject(() => renderStudioScene({ parts: [part('staged'), part('bad', 'other', { indices: [99, 1, 2] })] }));
                reject(() => renderStudioScene({ parts: [], lights: [{ colorHex: 0 }] }));
                require(mesh('mesh') === previous && previous.parent !== null, 'Rejected update replaced live scene');
            });
            check('all material/LOD mappings filter together; unknown mapping is not invented', () => {
                renderStudioScene({ parts: [part('multi', null, { mappings: [{ materialIndex: 1, lodMask: 2, materialPath: 'solidA/material:1' }, { materialIndex: 3, lodMask: 4, materialPath: 'solidA/material:3' }] }),
                    part('same-index', null, { mappings: [{ materialIndex: 3, lodMask: 4, materialPath: 'solidB/material:3' }] }),
                    part('unknown', null, { mappings: [] }), part('collision', null, { isCollision: true })] });
                setSceneFilter({ materialIndex: 3, lodMask: 4 }); require(mesh('multi').visible && !mesh('unknown').visible, 'Second mapping was lost');
                setSceneFilter({ materialIndex: 1, lodMask: 4 }); require(!mesh('multi').visible, 'Filter combined mismatched mappings');
                require(mesh('collision').visible, 'Visual filter hides unmapped collision');
                setSceneFilter({ materialPath: 'solidB/material:3' }); require(mesh('same-index').visible && !mesh('multi').visible, 'Scoped material filter matched another solid');
                setSceneFilter({ materialIndex: null, lodMask: null }); require(mesh('unknown').visible, 'Unknown mapping hidden without filter');
            });
            check('zero lights, absolute radius geometry and uneditable gizmos', () => {
                renderStudioScene({ parts: [part()], pivots: [{ index: 7, x: 10, y: 20, z: 30 }],
                    lights: [{ index: 4, x: 12, y: 3, z: 4, colorHex: 0, intensity: 0, radius: 0, editable: false }] });
                const container = lightsGroup.children[0], light = container.getObjectByName('realLight'), wire = container.getObjectByName('radiusWire');
                require(light.color.getHex() === 0 && light.intensity === 0 && light.distance === 0 && wire.scale.x === 0, 'Zero light values defaulted');
                for (const radius of [2, 12, 0, 12]) { updateLightRealtime(4, 0, 0, radius, 12, 3, 4); require(wire.geometry.parameters.radius * wire.scale.x === radius && light.distance === radius, 'Radius drift'); }
                selectGizmoFromUI('light', 4); require(transformControls.object === undefined && selectedGizmo === container, 'Uneditable light attached for dragging');
                require(messages.some(m => m[0] === 'OnGizmoSelected' && m[1] === 'light'), 'Readonly light cannot be selected');
                updatePivotRealtime(7, 15, 25, 35); near(pivotsGroup.children[0].position.toArray(), [15, 25, 35]);
                selectGizmoFromUI('pivot', 7); require(transformControls.object === pivotsGroup.children[0], 'Original pivot index lost after filtering');
                reject(() => updateLightRealtime(4, 0xffffff, 3, -1, 9, 9, 9)); near(container.position.toArray(), [12, 3, 4]);
            });
            tick(0);
            return results;
        });
        results.forEach(name => console.log(`PASS: ${name}`));
        if (process.env.STUDIO_SCREENSHOT) await page.screenshot({ path: process.env.STUDIO_SCREENSHOT });
        await page.evaluate(() => { const canvas = renderer.domElement; dispose3DViewer(); dispose3DViewer(); if (canvas.isConnected || renderer !== null || typedMotions.length) throw new Error('Disposal retained viewer state'); });
        assert.deepEqual(errors, []);
        console.log(`PASS: ${results.length} typed-viewer groups and disposal.`);
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
