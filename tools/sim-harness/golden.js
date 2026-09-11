#!/usr/bin/env node
// Runs the real config.js + script.js under Node with Math.random replaced by a seeded
// Mulberry32, and writes golden data for the Unity port's EditMode tests.
//
//   node tools/sim-harness/golden.js
//
// script.js is the reference for the simulation logic (docs/unity-port-plan.md) and is never
// patched. The Unity build deliberately uses a few different config values (§5-6), so those are
// read from GameConfig.cs and applied to config.js in memory; the files on disk stay unchanged,
// and any difference outside INTENTIONAL_DIFFERENCES stops the run.
//
// Doubles are written as their IEEE-754 bit patterns (16 hex digits), not as decimal numbers:
// Unity's JsonUtility does not parse decimal doubles exactly (it came back one ulp off), which
// would make an exact comparison fail on the parser rather than on the port.
'use strict';

const fs = require('fs');
const path = require('path');
const vm = require('vm');
const assert = require('assert');

const ROOT = path.resolve(__dirname, '..', '..');
const OUT_DIR = path.join(ROOT, 'unity', 'Assets', 'Tests', 'EditMode', 'Golden');
const FIXED_DT = 1 / 60; // same as script.js

// Mulberry32. Must stay bit-for-bit identical to unity/Assets/Scripts/Simulation/Mulberry32.cs.
function mulberry32Raw(seed) {
  let a = seed | 0;
  return function () {
    a = (a + 0x6D2B79F5) | 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return (t ^ (t >>> 14)) >>> 0;
  };
}

function mulberry32(seed) {
  const next = mulberry32Raw(seed);
  return () => next() / 4294967296;
}

// Config values where the Unity build intentionally differs from the web version.
const INTENTIONAL_DIFFERENCES = ['WOBBLE_AX', 'WOBBLE_AY', 'TAU_R'];

// Compares every numeric key of config.js with the same constant in GameConfig.cs and returns the
// Unity values that differ. Differences outside INTENTIONAL_DIFFERENCES are treated as mistakes.
function unityConfigOverrides() {
  const cs = fs.readFileSync(path.join(ROOT, 'unity', 'Assets', 'Scripts', 'Simulation', 'GameConfig.cs'), 'utf8');
  const csValues = {};
  for (const m of cs.matchAll(/public const (?:int|double) (\w+) = (-?[0-9.]+);/g)) csValues[m[1]] = Number(m[2]);
  const js = fs.readFileSync(path.join(ROOT, 'config.js'), 'utf8');
  const overrides = {};
  let compared = 0;
  for (const m of js.matchAll(/^\s+(\w+):\s*(-?[0-9.]+),/gm)) {
    const key = m[1];
    assert.ok(key in csValues, `GameConfig.cs has no constant ${key}`);
    compared++;
    if (csValues[key] !== Number(m[2])) overrides[key] = csValues[key];
  }
  // config.js has 29 numeric keys (2026-09-11); fewer means the pattern stopped matching some of them.
  assert.ok(compared >= 29, `only ${compared} config keys were compared; the pattern probably stopped matching`);
  assert.deepStrictEqual(Object.keys(overrides).sort(), [...INTENTIONAL_DIFFERENCES].sort(),
    `config.js and GameConfig.cs differ in unexpected keys: ${JSON.stringify(overrides)}`);
  return overrides;
}

const CONFIG_OVERRIDES = unityConfigOverrides();
const CONFIG_SRC = Object.entries(CONFIG_OVERRIDES).reduce((src, [key, value]) => {
  const re = new RegExp(`^(\\s+${key}:\\s*)-?[0-9.]+,`, 'm');
  assert.ok(re.test(src), `config key ${key} not found for override`);
  return src.replace(re, `$1${value},`);
}, fs.readFileSync(path.join(ROOT, 'config.js'), 'utf8'));

const view = new DataView(new ArrayBuffer(8));
const MASK64 = 0xFFFFFFFFFFFFFFFFn;

function bits(v) {
  view.setFloat64(0, v);
  return view.getBigUint64(0).toString(16).padStart(16, '0');
}

// Order-sensitive checksum over every particle's x, y, vx, vy bit patterns.
// Mirrored by Bits.Checksum in the C# tests.
function stateChecksum(particles) {
  let acc = 0n;
  for (const p of particles) {
    for (const v of [p.x, p.y, p.vx, p.vy]) {
      view.setFloat64(0, v);
      acc = ((acc << 1n) | (acc >> 63n)) & MASK64;
      acc ^= view.getBigUint64(0);
    }
  }
  return acc.toString(16).padStart(16, '0');
}

// Load the game into a fresh context whose Math.random is the seeded generator.
function loadGame(seed) {
  const noop = () => {};
  const ctx2d = new Proxy({}, {
    get: (o, k) => (k in o ? o[k] : noop),
    set: (o, k, v) => { o[k] = v; return true; },
  });
  const canvas = {
    getContext: () => ctx2d,
    addEventListener: noop,
    getBoundingClientRect: () => ({ left: 0, top: 0 }),
    style: {},
    width: 0,
    height: 0,
  };
  const sandbox = {
    console,
    document: { getElementById: () => canvas },
    localStorage: { getItem: () => null, setItem: noop },
    performance: { now: () => 0 },
    requestAnimationFrame: noop,
    addEventListener: noop,
    innerWidth: 1920,
    innerHeight: 1080,
    devicePixelRatio: 1,
    __rng: mulberry32(seed),
  };
  sandbox.window = sandbox;
  vm.createContext(sandbox);

  vm.runInContext('Math.random = __rng;', sandbox);
  assert.strictEqual(vm.runInContext('Math.random === __rng', sandbox), true, 'Math.random override did not take');

  vm.runInContext(CONFIG_SRC, sandbox, { filename: 'config.js' });
  vm.runInContext(fs.readFileSync(path.join(ROOT, 'script.js'), 'utf8'), sandbox, { filename: 'script.js' });
  const run = (code) => vm.runInContext(code, sandbox);
  for (const [key, value] of Object.entries(CONFIG_OVERRIDES)) {
    assert.strictEqual(run(`CONFIG.${key}`), value, `override ${key} did not take`);
  }
  assert.strictEqual(typeof run('update'), 'function', 'script.js did not load');
  return run;
}

const SNAPSHOT = `({
  gameState, dangerStage, currentDensity, currentDangerRatio, currentScore, finalScore,
  graceCounter, reachableCount, bangPossible, unreachableFrames, shakeMagnitude, flashOpacity,
  x: particles.map(p => p.x), y: particles.map(p => p.y),
  vx: particles.map(p => p.vx), vy: particles.map(p => p.vy),
  inMeasure: particles.map(p => p.inMeasure), outOfReach: particles.map(p => p.outOfReach),
})`;

const DOUBLE_FIELDS = ['cursorX', 'cursorY', 'currentDensity', 'currentDangerRatio', 'shakeMagnitude', 'flashOpacity'];
const DOUBLE_ARRAY_FIELDS = ['x', 'y', 'vx', 'vy'];

function encodeFrame(frame) {
  for (const k of DOUBLE_FIELDS) frame[k] = bits(frame[k]);
  for (const k of DOUBLE_ARRAY_FIELDS) frame[k] = Array.from(frame[k], bits);
  frame.inMeasure = Array.from(frame.inMeasure);
  frame.outOfReach = Array.from(frame.outOfReach);
  return frame;
}

// Cursor policies. Each returns [x, y] for a step; step 0 is the press itself.
// The C# tests reproduce these exactly, including the order of random draws (x before y).
const policies = {
  center: () => () => [960, 540],
  sweep: () => (step) => {
    const s = Math.min(step / 120, 1);
    return [300 + (960 - 300) * s, 300 + (540 - 300) * s];
  },
  // A hand that never holds perfectly still: +-3 px around the centre, fresh each step,
  // drawn from its own generator so the simulation's random stream is untouched.
  microJitter: (seed) => {
    const p = mulberry32((seed + 1000000) >>> 0);
    return () => [960 + (p() * 2 - 1) * 3, 540 + (p() * 2 - 1) * 3];
  },
};

// Press at step 0, then advance in fixed steps exactly as loop() does, setting the cursor
// before each update. Records checkpoints, a checksum for every step, the step where BANG
// happens, and `afterBang` steps after it.
function runRound(seed, policyName, { checkpoints = [], maxSteps, afterBang = 30, record = true }) {
  const run = loadGame(seed);
  const cursorAt = policies[policyName](seed);
  const frames = [];
  const checksums = [];
  const recorded = new Set();
  const snap = (step) => {
    if (!record || recorded.has(step)) return;
    recorded.add(step);
    frames.push(encodeFrame(Object.assign({ step, cursorX: run('cursorX'), cursorY: run('cursorY') }, run(SNAPSHOT))));
  };
  const sum = () => { if (record) checksums.push(stateChecksum(run('particles'))); };

  const [x0, y0] = cursorAt(0);
  run(`cursorX = ${x0}; cursorY = ${y0}; startRound(0);`);
  snap(0);
  sum();

  let simNow = 0;
  let bangStep = -1;
  for (let step = 1; step <= maxSteps; step++) {
    const [x, y] = cursorAt(step);
    simNow += FIXED_DT * 1000;
    run(`cursorX = ${x}; cursorY = ${y}; update(${FIXED_DT}, ${simNow});`);
    sum();

    if (bangStep < 0 && run('gameState') === 'BANG') bangStep = step;
    if (checkpoints.includes(step) || step === bangStep) snap(step);
    if (bangStep >= 0 && step >= bangStep + afterBang) {
      snap(step);
      break;
    }
  }
  return { seed, policy: policyName, bangStep, frames, checksums };
}

function write(name, data) {
  fs.mkdirSync(OUT_DIR, { recursive: true });
  const file = path.join(OUT_DIR, name);
  fs.writeFileSync(file, JSON.stringify(data) + '\n');
  console.log(`wrote ${path.relative(ROOT, file)} (${(fs.statSync(file).size / 1024).toFixed(0)} KB)`);
}

function main() {
  // The seeded stream must actually reach script.js: same seed, same layout; other seed, other layout.
  const layout = (seed) => loadGame(seed)('JSON.stringify(particles.slice(0, 5))');
  assert.strictEqual(layout(1), layout(1), 'same seed produced different initial particles');
  assert.notStrictEqual(layout(1), layout(2), 'different seeds produced identical initial particles');

  // Bit encoding must round-trip.
  for (const v of [0, -0, 1 / 3, 1431.889660553494, -1e-300]) {
    view.setBigUint64(0, BigInt('0x' + bits(v)));
    assert.ok(Object.is(view.getFloat64(0), v), `bit encoding does not round-trip ${v}`);
  }

  const rngNext = mulberry32Raw(12345);
  write('rng.json', { seed: 12345, raw: Array.from({ length: 1000 }, rngNext) });

  const run = loadGame(1);
  write('constants.json', {
    measureArea: bits(run('CONFIG.MEASURE_AREA')),
    densityMax: bits(run('CONFIG.DENSITY_MAX')),
    bangThreshold: bits(run('CONFIG.BANG_THRESHOLD')),
    requiredParticles: run('REQUIRED_PARTICLES'),
    effectiveRMax: bits(run('effectiveRMax')),
  });

  const checkpoints = [1, 2, 10, 60, 180, 420];
  write('trajectory-center.json', {
    runs: [1, 42].map((seed) => runRound(seed, 'center', { checkpoints, maxSteps: 1500 })),
  });
  write('trajectory-sweep.json', {
    runs: [7].map((seed) => runRound(seed, 'sweep', { checkpoints: [1, 10, 60, 120, 240], maxSteps: 1500 })),
  });

  const maxSteps = 2400; // 40 s of game time
  const seeds = 200;
  const bangRuns = [];
  for (let seed = 1; seed <= seeds; seed++) {
    const r = runRound(seed, 'microJitter', { maxSteps, record: false });
    bangRuns.push({ seed, bangStep: r.bangStep });
  }
  write('bang-times.json', { policy: 'microJitter', maxSteps, runs: bangRuns });

  const fired = bangRuns.filter((r) => r.bangStep >= 0);
  const secs = fired.map((r) => r.bangStep * FIXED_DT);
  const mean = secs.reduce((a, b) => a + b, 0) / secs.length;
  const sd = Math.sqrt(secs.reduce((a, b) => a + (b - mean) ** 2, 0) / secs.length);
  console.log(`bang: ${fired.length}/${bangRuns.length} rounds, mean ${mean.toFixed(3)} s, sd ${sd.toFixed(3)} s, ` +
    `min ${Math.min(...secs).toFixed(2)} s, max ${Math.max(...secs).toFixed(2)} s`);
  const histogram = new Map();
  for (const r of fired) {
    const bucket = Math.round(r.bangStep / 10) * 10;
    histogram.set(bucket, (histogram.get(bucket) || 0) + 1);
  }
  console.log('bang step histogram (nearest 10): ' +
    [...histogram].sort((a, b) => a[0] - b[0]).map(([k, n]) => `${k}:${n}`).join(' '));
}

main();
