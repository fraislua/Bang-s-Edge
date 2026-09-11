// Check how the page drives window.AudioController, without needing to hear anything.
// Wraps every AudioController method with a call counter, then plays the same scripted rounds in
// headless Chrome: a short hold released before the bang, a long hold into the bang, and a mute
// toggle followed by a reload. Run it on the web version and on the Unity WebGL build and compare.
//
//   node tools/web-shot/cdp-audio-check.mjs <url> <outDir> <name> [bangHoldMs]
//
// Prints a JSON summary and writes <name>-mute-before.png / -mute-after.png / -mute-reloaded.png.
import { spawn } from 'node:child_process';
import { mkdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

const [url, outDir, name, bangHold] = process.argv.slice(2);
if (!name) {
  console.log('usage: node cdp-audio-check.mjs <url> <outDir> <name> [bangHoldMs]');
  process.exit(2);
}
const W = 1920, H = 1080; // board scale 1: board coordinates are window pixels
const BANG_HOLD_MS = Number(bangHold || 30000);
const CHROME = process.env.CHROME || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const PORT = 9300 + Math.floor(Math.random() * 500);
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
mkdirSync(outDir, { recursive: true });

const chrome = spawn(CHROME, [
  '--headless=new', `--user-data-dir=${join(outDir, 'profile-audio-' + name)}`, '--no-first-run',
  `--remote-debugging-port=${PORT}`, `--window-size=${W},${H}`, '--force-device-scale-factor=1',
  '--hide-scrollbars', '--use-angle=swiftshader', '--enable-unsafe-swiftshader',
  '--autoplay-policy=no-user-gesture-required', 'about:blank',
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
const evaluate = async (expression) => {
  const r = await send('Runtime.evaluate', { expression, returnByValue: true });
  return r.result?.result?.value;
};
const shot = async (file, x, y, w, h, zoom) => {
  const r = await send('Page.captureScreenshot', { format: 'png', clip: { x, y, width: w, height: h, scale: zoom } });
  if (r.result) writeFileSync(join(outDir, file), Buffer.from(r.result.data, 'base64'));
};
const mouse = (type, x, y) => send('Input.dispatchMouseEvent', { type, x, y, button: 'left', clickCount: 1 });

async function loadAndWait() {
  const t0 = Date.now();
  await sleep(3000);
  let isUnity = false;
  for (let i = 0; i < 60; i++) {
    const v = JSON.parse(await evaluate(`JSON.stringify({ size: (document.querySelector('canvas') || {}).width || 0,
      unity: typeof createUnityInstance === 'function' })`) || '{}');
    isUnity = !!v.unity;
    if (v.size && v.size !== 300) break;
    await sleep(1000);
  }
  await sleep(isUnity ? 12000 : 1000);
  return { isUnity, readySeconds: (Date.now() - t0) / 1000 };
}

// Wrap each AudioController method with a counter. Callers that look the method up at call time
// (script.js, the jslib bridge, the unlock listener) go through the wrapper.
const INSTALL = `(() => {
  const a = window.AudioController;
  if (!a) return null;
  window.__audioCalls = {};
  for (const k of Object.keys(a)) {
    const f = a[k];
    if (typeof f !== 'function' || f.__counted) continue;
    const wrapped = function (...args) { window.__audioCalls[k] = (window.__audioCalls[k] || 0) + 1; return f.apply(this, args); };
    wrapped.__counted = true;
    a[k] = wrapped;
  }
  return Object.keys(a);
})()`;
const takeCounts = () => evaluate(`(() => { const c = window.__audioCalls || {}; window.__audioCalls = {}; return c; })()`);

await send('Page.enable');
await send('Emulation.setDeviceMetricsOverride', { width: W, height: H, deviceScaleFactor: 1, mobile: false });
await send('Page.navigate', { url });
const load = await loadAndWait();
const summary = { name, url, ...load };
summary.methods = await evaluate(INSTALL);
if (!summary.methods) {
  summary.error = 'window.AudioController is not defined';
  console.log(JSON.stringify(summary, null, 2));
  ws.close(); chrome.kill(); process.exit(1);
}
await takeCounts();

const cx = 960, cy = 540;
await mouse('mouseMoved', cx, cy);

// 1. Short hold, released well before the bang.
await mouse('mousePressed', cx, cy);
await sleep(3000);
await mouse('mouseReleased', cx, cy);
await sleep(1500);
summary.shortHold = await takeCounts();

// 2. Long hold into the big bang.
await mouse('mousePressed', cx, cy);
await sleep(BANG_HOLD_MS);
await mouse('mouseReleased', cx, cy);
await sleep(1500);
summary.bangHold = await takeCounts();

// 3. Mute toggle on the button (28x28 at 1874,18), then reload and read the state back.
const mx = 1874 + 14, my = 18 + 14;
await shot(`${name}-mute-before.png`, 1864, 8, 48, 48, 6);
summary.mutedBefore = await evaluate('window.AudioController.isMuted()');
await mouse('mouseMoved', mx, my);
await mouse('mousePressed', mx, my);
await mouse('mouseReleased', mx, my);
await sleep(1500);
summary.mutedAfterClick = await evaluate('window.AudioController.isMuted()');
summary.storageAfterClick = await evaluate(`localStorage.getItem('bangs_edge_muted')`);
summary.muteClickCalls = await takeCounts();
summary.muteClickStartedRound = (summary.muteClickCalls.startDrone || 0) > 0;
await shot(`${name}-mute-after.png`, 1864, 8, 48, 48, 6);

await send('Page.reload');
await loadAndWait();
summary.mutedAfterReload = await evaluate('window.AudioController && window.AudioController.isMuted()');
await shot(`${name}-mute-reloaded.png`, 1864, 8, 48, 48, 6);

console.log(JSON.stringify(summary, null, 2));
ws.close();
chrome.kill();
process.exit(0);
