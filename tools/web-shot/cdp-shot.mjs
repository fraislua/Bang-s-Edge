// Screenshot the web version or the Unity WebGL build in headless Chrome over the DevTools
// protocol, waiting in real time, so the Brain can look at the actual rendering itself.
// (--screenshot with --virtual-time-budget does not wait for a Unity build to download and start;
// it captured a black canvas.)
//
//   node tools/web-shot/cdp-shot.mjs <url> <width> <height> <outDir> <name> [holdMs]
//
// With holdMs above 2500 the board centre stays held that long and <name>-held-center.png is
// taken at the end (for example 25000 to reach the big bang screen under SwiftShader).
//
// Writes <name>-full.png plus magnified crops of the HUD (score, title, and the density bar while
// the board centre is held down, which shows the in-range line). Crops are given in 1920x1080
// board coordinates and converted to window pixels, so the same crop compares the web version
// and the Unity build at any window size. Needs Google Chrome and Node 22+ (global WebSocket).
import { spawn } from 'node:child_process';
import { mkdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

const [url, width, height, outDir, name] = process.argv.slice(2);
if (!name) {
  console.log('usage: node cdp-shot.mjs <url> <width> <height> <outDir> <name>');
  process.exit(2);
}
const W = Number(width), H = Number(height);
const s = Math.min(W / 1920, H / 1080); // viewScale of the board inside the window
const ox = (W - 1920 * s) / 2, oy = (H - 1080 * s) / 2;
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
await send('Page.navigate', { url });

// Ready when the canvas has been sized; a Unity build then needs a few more seconds to start.
const t0 = Date.now();
await sleep(3000);
let isUnity = false;
for (let i = 0; i < 60; i++) {
  const r = await send('Runtime.evaluate', {
    expression: `(() => { const c = document.querySelector('canvas');
      return JSON.stringify({ size: c ? c.width + 'x' + c.height : 'none', unity: typeof createUnityInstance === 'function' }); })()`,
    returnByValue: true,
  });
  const v = JSON.parse(r.result?.result?.value || '{}');
  isUnity = !!v.unity;
  if (v.size && v.size !== 'none' && !v.size.startsWith('300x150') && !v.size.startsWith('0x')) break;
  await sleep(1000);
}
await sleep(isUnity ? 12000 : 1000);
console.log(`${name}: ready after ${((Date.now() - t0) / 1000).toFixed(1)} s (${isUnity ? 'Unity' : 'web'})`);

const shot = async (file, clip) => {
  const r = await send('Page.captureScreenshot', clip ? { format: 'png', clip } : { format: 'png' });
  if (!r.result) { console.log(`${file}: capture failed ${JSON.stringify(r.error)}`); return; }
  writeFileSync(join(outDir, file), Buffer.from(r.result.data, 'base64'));
  console.log(`wrote ${file}`);
};
const region = (x, y, w, h, zoom) => ({ x: ox + x * s, y: oy + y * s, width: w * s, height: h * s, scale: zoom });

await shot(`${name}-full.png`);
await shot(`${name}-score.png`, region(0, 0, 300, 60, 4));
await shot(`${name}-title.png`, region(760, 480, 400, 120, 3));

// Hold the board centre so the in-range line (the Japanese HUD text) appears.
const cx = ox + 960 * s, cy = oy + 540 * s;
await send('Input.dispatchMouseEvent', { type: 'mouseMoved', x: cx, y: cy });
await send('Input.dispatchMouseEvent', { type: 'mousePressed', x: cx, y: cy, button: 'left', clickCount: 1 });
await sleep(2500);
await shot(`${name}-holding-bar.png`, region(700, 0, 520, 100, 4));
// Optional longer hold (6th argument, ms) to reach the big bang, then capture the centre text.
const holdMs = Number(process.argv[7] || 2500);
if (holdMs > 2500) {
  await sleep(holdMs - 2500);
  await shot(`${name}-held-center.png`, region(560, 440, 800, 220, 2));
}
await send('Input.dispatchMouseEvent', { type: 'mouseReleased', x: cx, y: cy, button: 'left', clickCount: 1 });

ws.close();
chrome.kill();
process.exit(0);
