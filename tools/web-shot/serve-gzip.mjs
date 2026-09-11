// Serve a Unity WebGL build locally the way a real host serves a Gzip-compressed build, so the
// build that goes to unityroom can be checked before uploading. Unity's Gzip output names files
// *.js.gz / *.wasm.gz / *.data.gz and, without "Decompression Fallback", expects the server to send
// them with Content-Encoding: gzip and the content type of the uncompressed file.
// python -m http.server does not do this, so a Gzip build stays on the loading screen there.
//
//   node tools/web-shot/serve-gzip.mjs <buildDir> [port]
import { createServer } from 'node:http';
import { createReadStream, statSync } from 'node:fs';
import { extname, join, normalize, resolve } from 'node:path';

const [dirArg, portArg] = process.argv.slice(2);
if (!dirArg) {
  console.log('usage: node serve-gzip.mjs <buildDir> [port]');
  process.exit(2);
}
const ROOT = resolve(dirArg);
const PORT = Number(portArg || 8767);

const TYPES = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'application/javascript',
  '.wasm': 'application/wasm',
  '.data': 'application/octet-stream',
  '.json': 'application/json',
  '.css': 'text/css',
  '.png': 'image/png',
  '.ico': 'image/x-icon',
};

createServer((req, res) => {
  const urlPath = decodeURIComponent(new URL(req.url, 'http://localhost').pathname);
  let file = normalize(join(ROOT, urlPath));
  if (!file.startsWith(ROOT)) {
    res.writeHead(403).end();
    return;
  }
  try {
    if (statSync(file).isDirectory()) file = join(file, 'index.html');
    statSync(file);
  } catch {
    res.writeHead(404).end('not found');
    return;
  }
  const headers = {};
  let ext = extname(file);
  if (ext === '.gz') {
    headers['Content-Encoding'] = 'gzip';
    ext = extname(file.slice(0, -3));
  }
  headers['Content-Type'] = TYPES[ext] || 'application/octet-stream';
  headers['Cache-Control'] = 'no-store';
  res.writeHead(200, headers);
  createReadStream(file).pipe(res);
}).listen(PORT, '127.0.0.1', () => console.log(`serving ${ROOT} on http://127.0.0.1:${PORT}/ (gzip-aware)`));
