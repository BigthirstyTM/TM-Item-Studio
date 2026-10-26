const { spawn } = require('node:child_process');
const crypto = require('node:crypto');
const fs = require('node:fs');
const http = require('node:http');
const os = require('node:os');
const path = require('node:path');
const checkWavingFlags = require('./waving-flag-converter.cjs');

const root = path.resolve(__dirname, '../..');
const env = { ...process.env, DOTNET_ROLL_FORWARD: process.env.DOTNET_ROLL_FORWARD || 'Major' };
function run(command, args, extraEnv = {}) {
    return new Promise((resolve, reject) => {
        const child = spawn(command, args, { cwd: root, env: { ...env, ...extraEnv }, stdio: 'inherit', timeout: 300000 });
        child.once('error', reject);
        child.once('exit', (code, signal) => code === 0 ? resolve() : reject(new Error(`${command} failed (${signal || code})`)));
    });
}

(async () => {
    const tempRoot = fs.realpathSync(os.tmpdir());
    const work = fs.mkdtempSync(path.join(tempRoot, 'studio-waving-workflow-'));
    const webRoot = path.join(work, 'app/wwwroot');
    const evidence = process.env.STUDIO_WAVING_EVIDENCE || path.join(work, 'evidence');
    fs.mkdirSync(evidence, { recursive: true });
    let server;
    try {
        await run('dotnet', ['run', '--project', 'Tests/WavingFlagExport']);
        await run('dotnet', ['run', '--project', 'Tests/WavingFlagMapPackaging', '--', '--fixtures', path.join(work, 'map-fixtures')]);
        await run('dotnet', ['publish', 'TM-Item-Studio.csproj', '-v:q', '-o', path.join(work, 'app')]);
        const types = { '.html': 'text/html', '.js': 'text/javascript', '.json': 'application/json',
            '.wasm': 'application/wasm', '.css': 'text/css', '.svg': 'image/svg+xml' };
        server = http.createServer((request, response) => {
            try {
                const url = new URL(request.url, 'http://localhost');
                const file = path.resolve(webRoot, '.' + decodeURIComponent(url.pathname === '/' ? '/index.html' : url.pathname));
                const relative = path.relative(webRoot, file);
                if (relative.startsWith('..') || path.isAbsolute(relative) || !fs.existsSync(file) || !fs.statSync(file).isFile()) {
                    response.writeHead(404).end(); return;
                }
                response.writeHead(200, { 'Content-Type': types[path.extname(file)] || 'application/octet-stream', 'Cache-Control': 'no-store' });
                fs.createReadStream(file).on('error', () => response.destroy()).pipe(response);
            } catch { response.writeHead(400).end(); }
        });
        await new Promise((resolve, reject) => { server.once('error', reject); server.listen(0, '127.0.0.1', resolve); });
        const baseUrl = `http://127.0.0.1:${server.address().port}`;
        const app = path.join(webRoot, '_framework/TM-Item-Studio.wasm');
        const applicationSha256 = crypto.createHash('sha256').update(fs.readFileSync(app)).digest('hex').toUpperCase();
        console.log(`Published application SHA256: ${applicationSha256}`);
        process.env.STUDIO_WAVING_MAP_FIXTURES = path.join(work, 'map-fixtures');
        await checkWavingFlags(baseUrl, evidence);
        await run(process.execPath, ['Tests/Browser/workflow-regressions.cjs'], { STUDIO_BASE_URL: baseUrl });
        fs.writeFileSync(path.join(evidence, 'published-workflow-validation.json'), JSON.stringify({
            scope: 'Offline published headless-browser checks only; no native game operation',
            applicationSha256, wavingFlagWorkflowPassed: true, legacyWorkflowRegressionsPassed: true,
            samePublishedApplication: true
        }, null, 2) + '\n');
        console.log('PASS: waving flag workflow and legacy selection/classification workflow use the same published application.');
    } finally {
        if (server?.listening) await new Promise(resolve => server.close(resolve));
        // Only this exact newly-created temporary directory may be removed.
        const resolved = fs.realpathSync(work);
        if (path.dirname(resolved) !== tempRoot || !path.basename(resolved).startsWith('studio-waving-workflow-'))
            throw new Error(`Unexpected temporary cleanup target ${resolved}`);
        fs.rmSync(resolved, { recursive: true, force: true });
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
