// Local, read-only Studio server for browser/native compatibility checks.
const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');
const repo = path.resolve(__dirname, '../..');
const roots = [path.join(repo, 'bin/Debug/net8.0/wwwroot'), path.join(repo, 'wwwroot')];
const types = { '.html': 'text/html', '.js': 'text/javascript', '.json': 'application/json',
    '.wasm': 'application/wasm', '.css': 'text/css', '.svg': 'image/svg+xml', '.png': 'image/png', '.webp': 'image/webp' };
http.createServer((request, response) => {
    const relative = decodeURIComponent(new URL(request.url, 'http://localhost').pathname).replace(/^\/+/, '') || 'index.html';
    const file = roots.map(root => path.resolve(root, relative))
        .find((candidate, index) => candidate.startsWith(roots[index] + path.sep)
            && fs.existsSync(candidate) && fs.statSync(candidate).isFile());
    if (!file) { response.writeHead(404); response.end(); return; }
    response.setHeader('Content-Type', types[path.extname(file)] || 'application/octet-stream');
    response.setHeader('Cache-Control', 'no-store');
    fs.createReadStream(file).pipe(response);
}).listen(0, '127.0.0.1', function () { process.stdout.write(`http://127.0.0.1:${this.address().port}/\n`); });
