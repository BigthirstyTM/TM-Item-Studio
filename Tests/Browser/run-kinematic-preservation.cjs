const { spawn } = require('node:child_process');
const fs = require('node:fs');
const http = require('node:http');
const os = require('node:os');
const path = require('node:path');

const root = path.resolve(__dirname, '../..');
const env = { ...process.env, DOTNET_PROCESSOR_COUNT: '2', DOTNET_ROLL_FORWARD: 'Major' };
function run(command, args, extraEnv = {}) {
    return new Promise((resolve, reject) => {
        const child = spawn(command, args, { cwd: root, env: { ...env, ...extraEnv },
            stdio: 'inherit', timeout: 300000 });
        child.once('error', reject);
        child.once('exit', (code, signal) => code === 0 ? resolve() : reject(new Error(`${command} failed (${signal || code})`)));
    });
}

(async () => {
    const work = fs.mkdtempSync(path.join(os.tmpdir(), 'studio-kinematics-'));
    const webRoot = path.join(work, 'app/wwwroot');
    let server;
    try {
        await run('dotnet', ['publish', 'TM-Item-Studio.csproj', '-v:q', '-o', path.join(work, 'app')]);
        await run('dotnet', ['build', 'Tests/Browser/MotionArchive', '-v:q']);
        // Test the exact checkout's parser, not a transitive NuGet dependency.
        if (!fs.readFileSync(path.join(root, 'lib/GBX.NET.dll')).equals(
            fs.readFileSync(path.join(__dirname, 'MotionArchive/bin/Debug/net8.0/GBX.NET.dll'))))
            throw new Error('MotionArchive must use the bundled parser');
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
        for (const script of ['kinematic-preservation.cjs', 'viewer-lifecycle.cjs', 'typed-viewer.cjs'])
            await run(process.execPath, [`Tests/Browser/${script}`],
                { STUDIO_BASE_URL: `http://127.0.0.1:${server.address().port}` });
    } finally {
        if (server?.listening) await new Promise(resolve => server.close(resolve));
        fs.rmSync(work, { recursive: true, force: true });
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
