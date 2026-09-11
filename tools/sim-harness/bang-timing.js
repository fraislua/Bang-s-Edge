#!/usr/bin/env node
// Is the big bang solvable with a stopwatch? Runs the real config.js + script.js under Node with a
// seeded Math.random (same loader idea as tools/sim-harness/golden.js), for several cursor policies
// and wobble variants, and compares release strategies:
//   - oracle: release on the last step before the bang (upper bound)
//   - fixed time: release T seconds after the press, same T for every round (best T chosen)
//   - meter: release when the danger ratio first reaches theta, after a reaction delay (best theta)
//
//   node tools/sim-harness/bang-timing.js [--seeds N] [--variants a,b] [--policies a,b] [--maxSteps N] [--delay S] [--trace seed:variant:policy]
'use strict';

const fs = require('fs');
const path = require('path');
const vm = require('vm');
const assert = require('assert');

const ROOT = path.resolve(__dirname, '..', '..');
const FIXED_DT = 1 / 60;

function arg(name, def) {
  const i = process.argv.indexOf('--' + name);
  return i >= 0 ? process.argv[i + 1] : def;
}

function mulberry32(seed) {
  let a = seed | 0;
  return function () {
    a = (a + 0x6D2B79F5) | 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

const CONFIG_SRC = fs.readFileSync(path.join(ROOT, 'config.js'), 'utf8');
const SCRIPT_SRC = fs.readFileSync(path.join(ROOT, 'script.js'), 'utf8');

function patchConfig(src, overrides) {
  for (const [key, value] of Object.entries(overrides)) {
    const re = new RegExp('(\\b' + key + ':\\s*)[-0-9.]+');
    assert.ok(re.test(src), `config key ${key} not found`);
    src = src.replace(re, '$1' + value);
  }
  return src;
}

function patchPhase(src) {
  const cosOld = 'Math.cos(2 * Math.PI * CONFIG.WOBBLE_F1 * t)';
  const sinOld = 'Math.sin(2 * Math.PI * CONFIG.WOBBLE_F2 * t)';
  assert.strictEqual(src.split(cosOld).length, 2, 'wobble X expression not found exactly once');
  assert.strictEqual(src.split(sinOld).length, 2, 'wobble Y expression not found exactly once');
  return src
    .replace(cosOld, 'Math.cos(2 * Math.PI * CONFIG.WOBBLE_F1 * t + __phx)')
    .replace(sinOld, 'Math.sin(2 * Math.PI * CONFIG.WOBBLE_F2 * t + __phy)');
}

const VARIANTS = {
  base: { config: {}, randomPhase: false },
  noWobble: { config: { WOBBLE_AX: 0, WOBBLE_AY: 0 }, randomPhase: false },
  randPhase: { config: {}, randomPhase: true },
  smallWobble: { config: { WOBBLE_AX: 4, WOBBLE_AY: 3 }, randomPhase: false },
  measureOnAttract: { config: {}, randomPhase: false, measureOnAttract: true },
};

// Measure density around the wobbling attraction centre instead of the raw cursor.
function patchMeasure(src) {
  const xOld = 'const mdx = cursorX - p.x;';
  const yOld = 'const mdy = cursorY - p.y;';
  assert.strictEqual(src.split(xOld).length, 2, 'measure X expression not found exactly once');
  assert.strictEqual(src.split(yOld).length, 2, 'measure Y expression not found exactly once');
  return src.replace(xOld, 'const mdx = attractX - p.x;').replace(yOld, 'const mdy = attractY - p.y;');
}

// "name" or "name+KEY=VAL+KEY=VAL" (config overrides on top of a named variant).
function parseVariant(spec) {
  const [name, ...sets] = spec.split('+');
  assert.ok(VARIANTS[name], `unknown variant ${name}`);
  const v = VARIANTS[name];
  const config = Object.assign({}, v.config);
  for (const s of sets) {
    const [k, val] = s.split('=');
    config[k] = Number(val);
  }
  return Object.assign({}, v, { config });
}

function loadGame(seed, variantName) {
  const variant = parseVariant(variantName);
  const noop = () => {};
  const ctx2d = new Proxy({}, { get: (o, k) => (k in o ? o[k] : noop), set: (o, k, v) => { o[k] = v; return true; } });
  const canvas = { getContext: () => ctx2d, addEventListener: noop, getBoundingClientRect: () => ({ left: 0, top: 0 }), style: {}, width: 0, height: 0 };
  const phaseRng = mulberry32((seed + 2000000) >>> 0);
  const phx = variant.randomPhase ? phaseRng() * 2 * Math.PI : 0;
  const phy = variant.randomPhase ? phaseRng() * 2 * Math.PI : 0;
  const sandbox = {
    console, document: { getElementById: () => canvas }, localStorage: { getItem: () => null, setItem: noop },
    performance: { now: () => 0 }, requestAnimationFrame: noop, addEventListener: noop,
    innerWidth: 1920, innerHeight: 1080, devicePixelRatio: 1, __rng: mulberry32(seed), __phx: phx, __phy: phy,
  };
  sandbox.window = sandbox;
  vm.createContext(sandbox);
  vm.runInContext('Math.random = __rng;', sandbox);
  assert.strictEqual(vm.runInContext('Math.random === __rng', sandbox), true, 'Math.random override did not take');
  vm.runInContext(patchConfig(CONFIG_SRC, variant.config), sandbox, { filename: 'config.js' });
  let scriptSrc = SCRIPT_SRC;
  if (variant.randomPhase) scriptSrc = patchPhase(scriptSrc);
  if (variant.measureOnAttract) scriptSrc = patchMeasure(scriptSrc);
  vm.runInContext(scriptSrc, sandbox, { filename: 'script.js' });
  for (const [k, v] of Object.entries(variant.config)) {
    assert.strictEqual(vm.runInContext(`CONFIG.${k}`, sandbox), v, `override ${k} did not take`);
  }
  vm.runInContext(`globalThis.__step = function (x, y, dt, now) {
    cursorX = x; cursorY = y; update(dt, now);
    return [gameState === 'BANG' ? 1 : 0, currentScore, currentDangerRatio, currentDensity * CONFIG.MEASURE_AREA];
  };`, sandbox);
  const cfg = vm.runInContext('({ AX: CONFIG.WOBBLE_AX, AY: CONFIG.WOBBLE_AY, F1: CONFIG.WOBBLE_F1, F2: CONFIG.WOBBLE_F2 })', sandbox);
  return { run: (c) => vm.runInContext(c, sandbox), step: sandbox.__step, phx, phy, cfg };
}

function gauss(rng) {
  const u = Math.max(rng(), 1e-12), v = rng();
  return Math.sqrt(-2 * Math.log(u)) * Math.cos(2 * Math.PI * v);
}

// Cursor policies (board coordinates), each with its own generator so the simulation's stream is untouched.
const POLICIES = {
  still: () => () => [960, 540],
  jitter3: (seed) => {
    const p = mulberry32((seed + 1000000) >>> 0); // same as golden.js microJitter
    return () => [960 + (p() * 2 - 1) * 3, 540 + (p() * 2 - 1) * 3];
  },
  // Holding roughly still: slow drift (sd 8 px, time constant 0.6 s) plus +-1.5 px tremor.
  drift: (seed) => {
    const p = mulberry32((seed + 3000000) >>> 0);
    let ox = 0, oy = 0;
    const tau = 0.6, sd = 8, k = Math.exp(-FIXED_DT / tau), s = sd * Math.sqrt(1 - k * k);
    return () => {
      ox = ox * k + s * gauss(p); oy = oy * k + s * gauss(p);
      return [960 + ox + (p() * 2 - 1) * 1.5, 540 + oy + (p() * 2 - 1) * 1.5];
    };
  },
  offCenter: (seed) => {
    const p = mulberry32((seed + 4000000) >>> 0);
    return () => [760 + (p() * 2 - 1) * 3, 440 + (p() * 2 - 1) * 3];
  },
  // Press somewhere 150-350 px from the centre, glide there (time constant 0.4 s), then drift.
  wander: (seed) => {
    const p = mulberry32((seed + 5000000) >>> 0);
    const ang = p() * 2 * Math.PI, rad = 150 + p() * 200;
    let x = 960 + Math.cos(ang) * rad, y = 540 + Math.sin(ang) * rad;
    let ox = 0, oy = 0;
    const kg = 1 - Math.exp(-FIXED_DT / 0.4);
    const tau = 0.6, sd = 8, k = Math.exp(-FIXED_DT / tau), s = sd * Math.sqrt(1 - k * k);
    let first = true;
    return () => {
      if (first) { first = false; return [x, y]; }
      x += (960 - x) * kg; y += (540 - y) * kg;
      ox = ox * k + s * gauss(p); oy = oy * k + s * gauss(p);
      return [x + ox + (p() * 2 - 1) * 1.5, y + oy + (p() * 2 - 1) * 1.5];
    };
  },
};

function runRound(seed, variantName, policyName, maxSteps, trace) {
  const g = loadGame(seed, variantName);
  const cursorAt = POLICIES[policyName](seed);
  const [x0, y0] = cursorAt(0);
  g.run(`cursorX = ${x0}; cursorY = ${y0}; startRound(0);`);
  const scores = [0], ratios = [0], counts = [0], offY = [0], offX = [0];
  let simNow = 0, bangStep = -1;
  for (let step = 1; step <= maxSteps; step++) {
    const [x, y] = cursorAt(step);
    simNow += FIXED_DT * 1000;
    const t = simNow / 1000;
    const r = g.step(x, y, FIXED_DT, simNow);
    if (r[0] === 1) { bangStep = step; break; }
    scores.push(r[1]); ratios.push(r[2]); counts.push(r[3]);
    if (trace) {
      offX.push(g.cfg.AX * Math.cos(2 * Math.PI * g.cfg.F1 * t + g.phx));
      offY.push(g.cfg.AY * Math.sin(2 * Math.PI * g.cfg.F2 * t + g.phy));
    }
  }
  return { seed, bangStep, scores, ratios, counts, offX, offY };
}

const mean = (a) => a.reduce((s, v) => s + v, 0) / (a.length || 1);
const sd = (a) => { const m = mean(a); return Math.sqrt(mean(a.map((v) => (v - m) ** 2))); };

function summarize(runs, maxSteps, delaySteps) {
  const fired = runs.filter((r) => r.bangStep >= 0);
  const secs = fired.map((r) => r.bangStep * FIXED_DT).sort((a, b) => a - b);
  // Largest share of bang times inside any 0.2 s window.
  let lock = 0;
  for (let i = 0, j = 0; i < secs.length; i++) {
    while (secs[i] - secs[j] > 0.2 + 1e-9) j++;
    lock = Math.max(lock, i - j + 1);
  }
  const scoreAt = (r, s) => (s < r.scores.length ? r.scores[s] : 0); // scores[] stops at the step before the bang
  const oracle = mean(runs.map((r) => Math.max(...r.scores)));
  let bestT = 0, bestTScore = -1;
  for (let s = 60; s <= maxSteps; s++) {
    const m = mean(runs.map((r) => scoreAt(r, s)));
    if (m > bestTScore) { bestTScore = m; bestT = s; }
  }
  let bestTheta = 0, bestThetaScore = -1;
  for (let th = 0.3; th <= 1.0001; th += 0.01) {
    const m = mean(runs.map((r) => {
      const i = r.ratios.findIndex((v) => v >= th);
      return i < 0 ? scoreAt(r, r.scores.length - 1) : scoreAt(r, i + delaySteps);
    }));
    if (m > bestThetaScore) { bestThetaScore = m; bestTheta = th; }
  }
  return {
    fired: `${fired.length}/${runs.length}`,
    meanBang: secs.length ? +mean(secs).toFixed(2) : null,
    sdBang: secs.length ? +sd(secs).toFixed(2) : null,
    p10: secs.length ? +secs[Math.floor(secs.length * 0.1)].toFixed(2) : null,
    p90: secs.length ? +secs[Math.floor(secs.length * 0.9)].toFixed(2) : null,
    lock02: secs.length ? +(lock / secs.length).toFixed(2) : null,
    oracle: Math.round(oracle),
    fixedT: +(bestT * FIXED_DT).toFixed(2),
    fixedScore: Math.round(bestTScore),
    fixedPct: Math.round((100 * bestTScore) / oracle),
    theta: +bestTheta.toFixed(2),
    meterScore: Math.round(bestThetaScore),
    meterPct: Math.round((100 * bestThetaScore) / oracle),
  };
}

function main() {
  const traceArg = arg('trace', null);
  const maxSteps = Number(arg('maxSteps', 1800));
  if (traceArg) {
    const [seed, variant, policy] = traceArg.split(':');
    const r = runRound(Number(seed), variant, policy, maxSteps, true);
    console.log(`bangStep ${r.bangStep} (${(r.bangStep * FIXED_DT).toFixed(3)} s)`);
    const from = Number(arg('from', 0)), every = Number(arg('every', 3));
    for (let s = from; s < r.scores.length; s += every) {
      console.log(`${s}\t${(s * FIXED_DT).toFixed(3)}\tn=${r.counts[s].toFixed(0)}\tratio=${r.ratios[s].toFixed(3)}\toffX=${r.offX[s].toFixed(1)}\toffY=${r.offY[s].toFixed(1)}`);
    }
    return;
  }
  const seeds = Number(arg('seeds', 10));
  const delaySteps = Math.round(Number(arg('delay', 0.25)) / FIXED_DT);
  const variants = arg('variants', Object.keys(VARIANTS).join(',')).split(',');
  const policies = arg('policies', Object.keys(POLICIES).join(',')).split(',');
  const out = {};
  for (const v of variants) {
    for (const p of policies) {
      const t0 = Date.now();
      const runs = [];
      for (let seed = 1; seed <= seeds; seed++) runs.push(runRound(seed, v, p, maxSteps, false));
      const s = summarize(runs, maxSteps, delaySteps);
      out[`${v}/${p}`] = s;
      console.log(`${v.padEnd(9)} ${p.padEnd(9)} ${JSON.stringify(s)}  (${((Date.now() - t0) / 1000).toFixed(1)} s)`);
    }
  }
  const outFile = arg('out', null);
  if (outFile) fs.writeFileSync(outFile, JSON.stringify({ seeds, maxSteps, delaySteps, results: out }, null, 1));
}

main();
