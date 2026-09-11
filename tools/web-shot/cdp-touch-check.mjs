// Play the Unity WebGL build with touch input in headless Chrome over the DevTools protocol, the way
// a phone held sideways would, so the Brain can check the touch path before the user tries a device.
//
//   node tools/web-shot/cdp-touch-check.mjs <url> <outDir> <name> [width=915] [height=412] [dpr=2]
//
// Width and height are CSS pixels of a landscape phone (the defaults are a common Android size).
// It prints what the page reports (coarse pointer, canvas pixels per CSS pixel, the touch guard CSS,
// which events started the audio, mute state) and writes screenshots of each step:
//   <name>-1-ready.png     before any touch: the wording should say TOUCH & HOLD
//   <name>-2-holding.png   board centre held: the gathering point should sit above the finger (red cross)
//   <name>-3-released.png  after lifting: the round result and TAP wording
//   <name>-4-mute.png      after a tap just below the mute button: the icon should show muted
//   <name>-5-cancel.png    a hold ended by touchCancel: should resolve like a release
//   <name>-6-portrait.png  the same phone upright: the board stays playable, letterboxed (no rotate notice;
//                          unityroom keeps the canvas wide even on an upright phone, so none is shown)
import { spawn } from 'node:child_process';
import { mkdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

const [url, outDir, name] = process.argv.slice(2);
if (!name) {
  console.log('usage: node cdp-touch-check.mjs <url> <outDir> <name> [width] [height] [dpr]');
  process.exit(2);
}
const W = Number(process.argv[5] || 915), H = Number(process.argv[6] || 412), DPR = Number(process.argv[7] || 2);
const CHROME = process.env.CHROME || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const PORT = 9300 + Math.floor(Math.random() * 500);
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
mkdirSync(outDir, { recursive: true });

const chrome = spawn(CHROME, [
  '--headless=new', `--user-data-dir=${join(outDir, 'profile-' + name)}`, '--no-first-run',
  `--remote-debugging-port=${PORT}`, `--window-size=${W},${H}`,
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
  if (msg.method === 'Runtime.exceptionThrown') console.log('page exception:', msg.params.exceptionDetails.text);
  if (msg.method === 'Runtime.consoleAPICalled' && msg.params.type === 'error') {
    console.log('console error:', msg.params.args.map((a) => a.value ?? a.description).join(' '));
  }
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

const setViewport = (w, h) => send('Emulation.setDeviceMetricsOverride', {
  width: w, height: h, deviceScaleFactor: DPR, mobile: true,
  screenOrientation: w > h ? { type: 'landscapePrimary', angle: 90 } : { type: 'portraitPrimary', angle: 0 },
});
await send('Page.enable');
await send('Runtime.enable');
await setViewport(W, H);
await send('Emulation.setTouchEmulationEnabled', { enabled: true, maxTouchPoints: 5 });
await send('Page.navigate', { url });

// Ready when the canvas has been sized; the Unity build then needs a few more seconds to start.
const t0 = Date.now();
await sleep(3000);
for (let i = 0; i < 60; i++) {
  const size = await evaluate(`(() => { const c = document.querySelector('canvas'); return c ? c.width + 'x' + c.height : 'none'; })()`);
  if (size && size !== 'none' && !size.startsWith('300x150') && !size.startsWith('0x')) break;
  await sleep(1000);
}
await sleep(12000);
console.log(`${name}: ready after ${((Date.now() - t0) / 1000).toFixed(1)} s, viewport ${W}x${H} CSS px at dpr ${DPR}`);

// Record which DOM event was being handled each time audio-unlock.jspre starts the audio.
const hooked = await evaluate(`(() => { const a = window.AudioController; if (!a) return false;
  const init = a.init; window.__initEvents = [];
  a.init = function () { window.__initEvents.push(window.event ? window.event.type : 'none'); return init.apply(this, arguments); };
  return true; })()`);
const page = await evaluate(`(() => { const c = document.querySelector('canvas');
  return JSON.stringify({ coarsePointer: matchMedia('(pointer: coarse)').matches,
    canvas: c.width + 'x' + c.height, screenPxPerCssPx: +(c.width / c.getBoundingClientRect().width).toFixed(3),
    touchAction: c.style.touchAction, touchCallout: c.style.webkitTouchCallout || '(unset)' }); })()`);
console.log(`page: ${page}; audio init hooked: ${hooked}`);

const shot = async (file) => {
  const r = await send('Page.captureScreenshot', { format: 'png' });
  if (!r.result) { console.log(`${file}: capture failed ${JSON.stringify(r.error)}`); return; }
  writeFileSync(join(outDir, file), Buffer.from(r.result.data, 'base64'));
  console.log(`wrote ${file}`);
};
// Board coordinates (1920x1080) to CSS pixels of the current viewport, as LetterboxCamera places the board.
const toCss = (bx, by, w = W, h = H) => {
  const s = Math.min(w / 1920, h / 1080);
  return { x: (w - 1920 * s) / 2 + bx * s, y: (h - 1080 * s) / 2 + by * s, s };
};
const touch = (type, p) => send('Input.dispatchTouchEvent', {
  type, touchPoints: type === 'touchEnd' || type === 'touchCancel' ? [] : [{ x: p.x, y: p.y, id: 1, radiusX: 12, radiusY: 12 }],
});
// Marks the finger on the screenshot, outside the canvas so the game never sees it.
const markFinger = (p) => evaluate(`(() => { let m = document.getElementById('finger-mark');
  if (!m) { m = document.createElement('div'); m.id = 'finger-mark'; document.body.appendChild(m); }
  m.style.cssText = 'position:fixed;pointer-events:none;z-index:9;width:14px;height:14px;margin:-7px 0 0 -7px;'
    + 'left:${p.x}px;top:${p.y}px;border:2px solid red;border-radius:50%;box-sizing:border-box';
  return true; })()`);
const clearFinger = () => evaluate(`(() => { const m = document.getElementById('finger-mark'); if (m) m.remove(); return true; })()`);

await shot(`${name}-1-ready.png`);

// Hold the board centre. The gathering point should appear TOUCH_CURSOR_OFFSET_CSS_PX (60) above the finger.
const centre = toCss(960, 540);
await touch('touchStart', centre);
await markFinger(centre);
await sleep(3000);
await shot(`${name}-2-holding.png`);
console.log(`finger at CSS (${centre.x.toFixed(1)}, ${centre.y.toFixed(1)}); expected gathering point at CSS y ${(centre.y - 60).toFixed(1)} (board y ${(540 - 60 / centre.s).toFixed(0)})`);
await touch('touchEnd', centre);
await clearFinger();
await sleep(1200);
await shot(`${name}-3-released.png`);
console.log(`audio init events so far: ${JSON.stringify(await evaluate('window.__initEvents'))}`);

// Tap 20 board px below the drawn mute button (board rect x 1828..1884, y 36..92). A mouse there would
// miss; with touch the hit area is stretched to at least 44 CSS px tall, so it should toggle mute.
const mutedBefore = await evaluate('window.AudioController && window.AudioController.isMuted()');
const muteTap = toCss(1856, 112);
await touch('touchStart', muteTap);
await sleep(120);
await touch('touchEnd', muteTap);
await sleep(800);
const mutedAfter = await evaluate('window.AudioController && window.AudioController.isMuted()');
await shot(`${name}-4-mute.png`);
console.log(`mute before ${mutedBefore}, after tap below the button ${mutedAfter}`);

// A hold ended by touchCancel (for example a system gesture) should resolve like a release.
await touch('touchStart', centre);
await sleep(2000);
await touch('touchCancel', centre);
await sleep(1200);
await shot(`${name}-5-cancel.png`);

// Turn the phone upright.
await setViewport(H, W);
await sleep(2000);
await shot(`${name}-6-portrait.png`);

ws.close();
chrome.kill();
process.exit(0);
