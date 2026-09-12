const assert = require('node:assert/strict');
const { spawn } = require('node:child_process');
const crypto = require('node:crypto');
const fs = require('node:fs');
const http = require('node:http');
const os = require('node:os');
const path = require('node:path');

const root = path.resolve(__dirname, '../..');
const env = { ...process.env, DOTNET_ROLL_FORWARD: process.env.DOTNET_ROLL_FORWARD || 'Major' };
function run(command, args, extraEnv = {}) {
    return new Promise((resolve, reject) => {
        const child = spawn(command, args, { cwd: root, env: { ...env, ...extraEnv }, stdio: 'inherit', timeout: 300_000 });
        child.on('error', reject);
        child.on('exit', (code, signal) => code === 0 ? resolve() : reject(new Error(`${command} failed (${signal || code})`)));
    });
}

(async () => {
    const work = fs.mkdtempSync(path.join(os.tmpdir(), 'studio-animation-upload-'));
    const webRoot = path.join(work, 'app', 'wwwroot');
    const fixtures = path.join(work, 'fixtures');
    let server;
    try {
        await run('dotnet', ['publish', 'TM-Item-Studio.csproj', '-v:q', '-o', path.join(work, 'app')]);
        await run('dotnet', ['run', '--project', 'Tests/Browser/FixtureGenerator', '--', fixtures]);

        // Serve only this run's output, on an OS-assigned port. A pre-existing
        // developer server or STUDIO_BASE_URL must not influence this check.
        const types = { '.html': 'text/html', '.js': 'text/javascript', '.json': 'application/json',
            '.wasm': 'application/wasm', '.css': 'text/css', '.svg': 'image/svg+xml' };
        server = http.createServer((request, response) => {
            try {
                const url = new URL(request.url, 'http://localhost');
                const file = path.resolve(webRoot, '.' + decodeURIComponent(url.pathname === '/' ? '/index.html' : url.pathname));
                const relative = path.relative(webRoot, file);
                if (relative.startsWith('..') || path.isAbsolute(relative) || !fs.existsSync(file) || !fs.statSync(file).isFile()) {
                    response.writeHead(404).end();
                    return;
                }
                response.writeHead(200, { 'Content-Type': types[path.extname(file)] || 'application/octet-stream', 'Cache-Control': 'no-store' });
                fs.createReadStream(file).on('error', () => response.destroy()).pipe(response);
            } catch { response.writeHead(400).end(); }
        });
        await new Promise((resolve, reject) => {
            server.once('error', reject);
            server.listen(0, '127.0.0.1', resolve);
        });
        const address = server.address();
        assert.ok(address && typeof address === 'object');
        const baseUrl = `http://127.0.0.1:${address.port}`;
        for (const asset of ['_framework/TM-Item-Studio.wasm', 'js/meshViewer.js']) {
            console.log(`Published ${asset} SHA256: ${crypto.createHash('sha256').update(fs.readFileSync(path.join(webRoot, asset))).digest('hex')}`);
        }
        for (const order of ['first', 'last']) {
            console.log(`Testing static-${order} through ${baseUrl}`);
            await run(process.execPath, ['Tests/Browser/animation-upload.cjs', path.join(fixtures, `animation-static-${order}.Item.Gbx`)],
                { STUDIO_BASE_URL: baseUrl });
        }
    } finally {
        if (server?.listening) await new Promise(resolve => server.close(resolve));
        fs.rmSync(work, { recursive: true, force: true });
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
