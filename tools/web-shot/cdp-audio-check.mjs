// Check how the page drives window.AudioController, without needing to hear anything.
// Wraps every AudioController method with a call counter, then plays the same scripted rounds in
// headless Chrome: a short hold released before the bang, a long hold into the bang, and a mute
// toggle followed by a reload. Run it on the web version and on the Unity WebGL build and compare.
// On the Unity build it also drags the volume slider (which only the Unity build has) and reloads.
//
//   node tools/web-shot/cdp-audio-check.mjs <url> <outDir> <name> [bangHoldMs] [hudScale]
//
// hudScale is GameHud.HudScale for the Unity build (the mute button grows with it); 1 for the web version.
// Prints a JSON summary and writes <name>-mute-before.png / -mute-after.png / -mute-reloaded.png.
import { spawn } from 'node:child_process';
import { mkdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

const [url, outDir, name, bangHold, hudScaleArg] = process.argv.slice(2);
if (!name) {
  console.log('usage: node cdp-audio-check.mjs <url> <outDir> <name> [bangHoldMs] [hudScale]');
  process.exit(2);
}
const W = 1920, H = 1080; // board scale 1: board coordinates are window pixels
const BANG_HOLD_MS = Number(bangHold || 30000);
const HUD_SCALE = Number(hudScaleArg || 1);
// The web version's mute button is 28x28 at (1874, 18); the Unity HUD scales its distance from the right and top edges.
const MUTE_SIZE = 28 * HUD_SCALE;
const MUTE_X = W - (W - 1874) * HUD_SCALE;
const MUTE_Y = 18 * HUD_SCALE;
const MUTE_SHOT = [MUTE_X - 10, MUTE_Y - 10, MUTE_SIZE + 20, MUTE_SIZE + 20, Math.max(2, Math.round(6 / HUD_SCALE))];
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

// 3. Mute toggle on the button, then reload and read the state back.
const mx = MUTE_X + MUTE_SIZE / 2, my = MUTE_Y + MUTE_SIZE / 2;
await shot(`${name}-mute-before.png`, ...MUTE_SHOT);
summary.mutedBefore = await evaluate('window.AudioController.isMuted()');
await mouse('mouseMoved', mx, my);
await mouse('mousePressed', mx, my);
await mouse('mouseReleased', mx, my);
await sleep(1500);
summary.mutedAfterClick = await evaluate('window.AudioController.isMuted()');
summary.storageAfterClick = await evaluate(`localStorage.getItem('bangs_edge_muted')`);
summary.muteClickCalls = await takeCounts();
summary.muteClickStartedRound = (summary.muteClickCalls.startDrone || 0) > 0;
await shot(`${name}-mute-after.png`, ...MUTE_SHOT);

await send('Page.reload');
await loadAndWait();
summary.mutedAfterReload = await evaluate('window.AudioController && window.AudioController.isMuted()');
await shot(`${name}-mute-reloaded.png`, ...MUTE_SHOT);

// 4. Volume slider (Unity build only): press at 25% of the track, drag to 50% and release.
// The drag must set and store the volume, unmute, and not start a round. Then reload and read it back.
if (load.isUnity) {
  await evaluate(INSTALL);
  await takeCounts();
  // Track in the web version's layout: x 1780..1858, centre y 32, scaled from the right and top edges.
  const trackLeft = W - (W - 1780) * HUD_SCALE, trackLen = 78 * HUD_SCALE, trackY = 32 * HUD_SCALE;
  const vx = (r) => trackLeft + trackLen * r;
  const VOLUME_SHOT = [W - (W - 1620) * HUD_SCALE, 0, (W - 1620) * HUD_SCALE, 60 * HUD_SCALE, 2];
  const getVolume = `(window.AudioController.getVolume ? window.AudioController.getVolume() : 'missing')`;
  await shot(`${name}-volume-before.png`, ...VOLUME_SHOT);
  await mouse('mouseMoved', vx(0.25), trackY);
  await mouse('mousePressed', vx(0.25), trackY);
  await sleep(500);
  summary.volumeAfterPress = await evaluate(getVolume);
  for (let i = 1; i <= 5; i++) {
    await mouse('mouseMoved', vx(0.25 + 0.05 * i), trackY);
    await sleep(150);
  }
  await sleep(300);
  await mouse('mouseReleased', vx(0.5), trackY);
  await sleep(1000);
  summary.volumeAfterDrag = await evaluate(getVolume);
  summary.volumeStorage = await evaluate(`localStorage.getItem('bangs_edge_volume')`);
  summary.mutedAfterVolumeDrag = await evaluate('window.AudioController.isMuted()');
  summary.volumeDragCalls = await takeCounts();
  summary.volumeDragStartedRound = (summary.volumeDragCalls.startDrone || 0) > 0;
  await shot(`${name}-volume-after.png`, ...VOLUME_SHOT);

  await send('Page.reload');
  await loadAndWait();
  summary.volumeAfterReload = await evaluate(getVolume);
  await shot(`${name}-volume-reloaded.png`, ...VOLUME_SHOT);
}

console.log(JSON.stringify(summary, null, 2));
ws.close();
chrome.kill();
process.exit(0);
