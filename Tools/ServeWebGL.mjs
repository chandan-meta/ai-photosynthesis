import { createServer } from 'node:http';
import { createReadStream, statSync } from 'node:fs';
import { resolve, sep, extname } from 'node:path';
import { fileURLToPath } from 'node:url';
const root = fileURLToPath(new URL('../Builds/WebGL/', import.meta.url));
const mime = { '.html': 'text/html', '.js': 'application/javascript', '.wasm': 'application/wasm', '.json': 'application/json', '.png': 'image/png', '.css': 'text/css' };
createServer((req, res) => {
  try {
    const path = resolve(root, '.' + decodeURIComponent(new URL(req.url, 'http://localhost').pathname));
    if (path !== resolve(root) && !path.startsWith(resolve(root) + sep)) throw new Error('path');
    const file = statSync(path).isDirectory() ? resolve(path, 'index.html') : path;
    res.writeHead(200, { 'Content-Type': mime[extname(file)] ?? 'application/octet-stream', 'Cache-Control': 'no-store' });
    createReadStream(file).on('error', () => res.destroy()).pipe(res);
  } catch { res.writeHead(404); res.end('Not found'); }
}).listen(8000, '127.0.0.1', () => console.log('WebGL ready: http://localhost:8000'));
