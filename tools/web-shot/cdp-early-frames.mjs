// Capture small viewport screenshots every 500 ms from navigation, to see whether the Unity
// splash screen appears during startup. Prints each frame's file size (black, splash and game
// frames compress to clearly different sizes) so the interesting frames can be picked out.
//   node tools/web-shot/cdp-early-frames.mjs <url> <outDir> <name>
import { spawn } from 'node:child_process';
import { mkdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

const [url, outDir, name] = process.argv.slice(2);
const W = 1920, H = 1080;
const CHROME = process.env.CHROME || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const PORT = 9300 + Math.floor(Math.random() * 500);
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
mkdirSync(outDir, { recursive: true });

const chrome = spawn(CHROME, [
  '--headless=new', `--user-data-dir=${join(outDir, 'profile-' + name)}`, '--no-first-run',
  `--remote-debugging-port=${PORT}`, `--window-size=${W},${H}`, '--force-device-scale-factor=1',
  '--hide-scrollbars', '--use-angle=swiftshader', '--enable-unsafe-swiftshader', 'about:blank',
], { stdio: 'ignore' });

let target;
for (let i = 0; i < 50 && !target; i++) {
  await sleep(200);
  try {
    const list = await (await fetch(`http://127.0.0.1:${PORT}/json/list`)).json();
    target = list.find((t) => t.type === 'page');
  } catch {}
}
if (!target) { console.log('no page target'); chrome.kill(); process.exit(1); }

const ws = new WebSocket(target.webSocketDebuggerUrl);
await new Promise((r) => ws.addEventListener('open', r, { once: true }));
let nextId = 1;
const pending = new Map();
ws.addEventListener('message', (ev) => {
  const msg = JSON.parse(ev.data);
  if (msg.id && pending.has(msg.id)) { pending.get(msg.id)(msg); pending.delete(msg.id); }
});
const send = (method, params = {}) => new Promise((resolve) => {
  const id = nextId++;
  pending.set(id, resolve);
  ws.send(JSON.stringify({ id, method, params }));
});

await send('Page.enable');
await send('Emulation.setDeviceMetricsOverride', { width: W, height: H, deviceScaleFactor: 1, mobile: false });
const t0 = Date.now();
await send('Page.navigate', { url });
for (let i = 0; i < 40; i++) {
  const due = t0 + i * 500;
  if (Date.now() < due) await sleep(due - Date.now());
  const r = await send('Page.captureScreenshot', { format: 'png', clip: { x: 0, y: 0, width: W, height: H, scale: 0.25 } });
  if (!r.result) continue;
  const buf = Buffer.from(r.result.data, 'base64');
  const file = `${name}-${String(i).padStart(2, '0')}.png`;
  writeFileSync(join(outDir, file), buf);
  console.log(`${((Date.now() - t0) / 1000).toFixed(1)}s ${file} ${buf.length}`);
}
ws.close();
chrome.kill();
process.exit(0);
