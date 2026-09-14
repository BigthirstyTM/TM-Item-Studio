const { spawnSync } = require('node:child_process');
const fs = require('node:fs');
const http = require('node:http');
const os = require('node:os');
const path = require('node:path');
const checkCollection = require('./collection-export.cjs');

const root = path.resolve(__dirname, '../..');
const env = { ...process.env, DOTNET_ROLL_FORWARD: process.env.DOTNET_ROLL_FORWARD || 'Major' };
function run(args) {
    const result = spawnSync('dotnet', args, { cwd: root, env, stdio: 'inherit', timeout: 300_000 });
    if (result.error) throw result.error;
    if (result.status !== 0) throw new Error(`dotnet ${args[0]} failed (${result.signal || result.status})`);
}

(async () => {
    const work = fs.mkdtempSync(path.join(os.tmpdir(), 'studio-collection-export-'));
    const webRoot = path.join(work, 'app/wwwroot');
    let server;
    try {
        run(['build', 'Tests/Browser/CollectionArchive', '-v:q']);
        run(['publish', 'TM-Item-Studio.csproj', '-v:q', '-o', path.join(work, 'app')]);
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
        await checkCollection(`http://127.0.0.1:${server.address().port}`, work);
    } finally {
        if (server?.listening) await new Promise(resolve => server.close(resolve));
        fs.rmSync(work, { recursive: true, force: true });
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
