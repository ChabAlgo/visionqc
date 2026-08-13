import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { exec } from 'node:child_process';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const root = path.join(__dirname, 'dist');
const host = '127.0.0.1';
const startPort = 3021;
const version = '4.4.10';

const mime = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.png': 'image/png',
  '.jpg': 'image/jpeg',
  '.jpeg': 'image/jpeg',
  '.gif': 'image/gif',
  '.svg': 'image/svg+xml; charset=utf-8',
  '.ico': 'image/x-icon',
  '.webp': 'image/webp',
  '.txt': 'text/plain; charset=utf-8'
};

function safeJoin(base, requestPath) {
  const decoded = decodeURIComponent(requestPath.split('?')[0]);
  const clean = decoded === '/' ? '/index.html' : decoded;
  const target = path.normalize(path.join(base, clean));
  if (!target.startsWith(base)) return null;
  return target;
}

function createServer() {
  return http.createServer((req, res) => {
    let filePath = safeJoin(root, req.url || '/');
    if (!filePath) {
      res.writeHead(403);
      res.end('Forbidden');
      return;
    }

    fs.stat(filePath, (statErr, stat) => {
      if (!statErr && stat.isDirectory()) filePath = path.join(filePath, 'index.html');
      fs.readFile(filePath, (err, data) => {
        if (err) {
          // SPA fallback
          fs.readFile(path.join(root, 'index.html'), (fallbackErr, fallbackData) => {
            if (fallbackErr) {
              res.writeHead(404);
              res.end('Not Found');
              return;
            }
            res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8', 'Cache-Control': 'no-store' });
            res.end(fallbackData);
          });
          return;
        }
        const ext = path.extname(filePath).toLowerCase();
        res.writeHead(200, {
          'Content-Type': mime[ext] || 'application/octet-stream',
          'Cache-Control': 'no-store'
        });
        res.end(data);
      });
    });
  });
}

function listenFixedPort(port) {
  return new Promise((resolve, reject) => {
    const server = createServer();
    server.once('error', reject);
    server.listen(port, host, () => resolve({ server, port }));
  });
}

if (!fs.existsSync(path.join(root, 'index.html'))) {
  console.error('[ERROR] dist/index.html not found. Please keep the dist folder next to server_static.mjs.');
  process.exit(1);
}

try {
  const { port } = await listenFixedPort(startPort);
  const url = `http://${host}:${port}/?v=${version}`;
  console.log('============================================================');
  console.log(` VisionQC DirectExport ${version} STATIC SERVER`);
  console.log(` RUNNING FOLDER: ${__dirname}`);
  console.log(` URL: ${url}`);
  console.log('============================================================');
  console.log('[OK] No npm install, no Vite, no Rollup required.');
  console.log('[INFO] Keep this black CMD window open while using the web app.');
  console.log('[INFO] Close this CMD window when finished.');
  console.log('');

  if (process.platform === 'win32') {
    exec(`start "" "${url}"`);
  } else if (process.platform === 'darwin') {
    exec(`open "${url}"`);
  } else {
    exec(`xdg-open "${url}"`);
  }
} catch (err) {
  console.error('[ERROR] Failed to start server on fixed port 3021.');
  if (err?.code === 'EADDRINUSE') {
    console.error('[INFO] Port 3021 is already in use. Close the existing VisionQC/CMD window and run again.');
  } else {
    console.error(err);
  }
  process.exit(1);
}
