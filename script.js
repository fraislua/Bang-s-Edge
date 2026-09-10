/**
 * Bang's-Edge — ゲーム本体実装
 * docs/game-concept.md の「実装仕様」に厳密に準拠
 */

// --- 1. Canvas初期化とリサイズ・DPR対応 ---
const canvas = document.getElementById('gameCanvas');
const ctx = canvas.getContext('2d');

let width = CONFIG.LOGICAL_WIDTH;
let height = CONFIG.LOGICAL_HEIGHT;
let dpr = window.devicePixelRatio || 1;
let effectiveRMax = 0;

// 実際のウィンドウサイズと、論理盤面をそこへ収めるための変換
let viewportW = 0;
let viewportH = 0;
let viewScale = 1;
let viewOffsetX = 0;
let viewOffsetY = 0;

// 射程可視化用の状態
let reachableCount = CONFIG.TOTAL_PARTICLES;  // 集積半径の上限内にある粒子数
let bangPossible = true;                       // 現在位置から到達可能か
let unreachableFrames = 0;                     // 射程内が必要数を下回り続けたフレーム数
const UNREACHABLE_WARN_FRAMES = 45;            // 警告を出すまでの猶予 (約0.75秒)
const REQUIRED_PARTICLES = Math.ceil(CONFIG.BANG_THRESHOLD * CONFIG.MEASURE_AREA);  // 170

function resize() {
  dpr = window.devicePixelRatio || 1;
  viewportW = window.innerWidth;
  viewportH = window.innerHeight;

  // 論理盤面のサイズは固定
  width = CONFIG.LOGICAL_WIDTH;
  height = CONFIG.LOGICAL_HEIGHT;

  // 論理盤面がはみ出さない最大倍率と、中央寄せのための余白
  viewScale = Math.min(viewportW / width, viewportH / height);
  viewOffsetX = (viewportW - width * viewScale) / 2;
  viewOffsetY = (viewportH - height * viewScale) / 2;

  // 集積半径の上限は論理盤面の対角線から決まるので、以後は定数になる
  effectiveRMax = Math.hypot(width, height) * CONFIG.R_MAX_RATIO;

  canvas.width = Math.floor(viewportW * dpr);
  canvas.height = Math.floor(viewportH * dpr);
}

window.addEventListener('resize', resize);
resize();

// --- 2. ゲーム状態の管理 ---
// 状態: 'READY' (待機) | 'ATTRACTING' (長押し吸引中) | 'RESOLVED' (リリース確定) | 'BANG' (ビッグバン失敗)
let gameState = 'READY';

// ハイスコア (localStorage対応)
let highScore = 0;
try {
  const saved = localStorage.getItem('bangs_edge_high_score');
  if (saved !== null) {
    highScore = parseInt(saved, 10) || 0;
  }
} catch (e) {
  // localStorage が使えない環境への安全対策
}

// ラウンド管理
let isPressing = false;
let pressStartTime = 0;
let cursorX = width / 2;
let cursorY = height / 2;

// 密度・スコア・危険度
let currentDensity = 0;
let currentDangerRatio = 0;
let currentScore = 0;
let finalScore = 0;
let graceCounter = 0;
let dangerStage = 'SAFE'; // 'SAFE' | 'WARM' | 'HOT' | 'CRITICAL'

// 演出
let shakeMagnitude = 0;
let flashOpacity = 0;

// 引力圏外粒子の微弱ランダム揺動の加速度 (px/s²)
const JITTER_FORCE = 35;

// --- 3. 粒子プール (TOTAL_PARTICLES個) と反発計算バッファ ---
const particles = [];
const repAx = new Float64Array(CONFIG.TOTAL_PARTICLES);
const repAy = new Float64Array(CONFIG.TOTAL_PARTICLES);

// Box-Muller法による標準正規乱数
function gaussRandom() {
  let u = 0, v = 0;
  while (u === 0) u = Math.random();
  while (v === 0) v = Math.random();
  return Math.sqrt(-2 * Math.log(u)) * Math.cos(2 * Math.PI * v);
}

function initParticles() {
  particles.length = 0;
  const margin = CONFIG.MARGIN;
  const spawnW = Math.max(width - 2 * margin, 20);
  const spawnH = Math.max(height - 2 * margin, 20);
  const minX = margin;
  const maxX = Math.max(margin, width - margin);
  const minY = margin;
  const maxY = Math.max(margin, height - margin);

  const total = CONFIG.TOTAL_PARTICLES;
  const uniformCount = Math.round(total * CONFIG.SPAWN_UNIFORM_FRAC);
  const clusterTotal = total - uniformCount;

  // 1. 一様ランダム配置
  for (let i = 0; i < uniformCount; i++) {
    particles.push({
      x: margin + Math.random() * spawnW,
      y: margin + Math.random() * spawnH,
      vx: 0, // 初速 0
      vy: 0,
      inMeasure: false,
      outOfReach: false,
    });
  }

  // 2. クラスタ中心の生成 (画面内 MARGIN を除いた範囲の一様ランダム)
  const clusters = [];
  for (let c = 0; c < CONFIG.SPAWN_CLUSTERS; c++) {
    clusters.push({
      x: margin + Math.random() * spawnW,
      y: margin + Math.random() * spawnH,
    });
  }

  // 3. クラスタ配置 (残りの粒子を各クラスタに均等に振り分け、2次元正規分布)
  for (let i = 0; i < clusterTotal; i++) {
    const cluster = clusters[i % CONFIG.SPAWN_CLUSTERS];
    const rawX = cluster.x + gaussRandom() * CONFIG.SPAWN_SIGMA;
    const rawY = cluster.y + gaussRandom() * CONFIG.SPAWN_SIGMA;
    const clampedX = Math.min(Math.max(rawX, minX), maxX);
    const clampedY = Math.min(Math.max(rawY, minY), maxY);

    particles.push({
      x: clampedX,
      y: clampedY,
      vx: 0,
      vy: 0,
      inMeasure: false,
      outOfReach: false,
    });
  }
}

// 初期配置
initParticles();

// ミュートボタン領域定義 (右上に配置)
const MUTE_BTN = {
  size: 28,
  marginRight: 18,
  marginTop: 18,
  getRect() {
    return {
      x: width - this.marginRight - this.size,
      y: this.marginTop,
      w: this.size,
      h: this.size,
    };
  },
  contains(px, py) {
    const r = this.getRect();
    return px >= r.x && px <= r.x + r.w && py >= r.y && py <= r.y + r.h;
  }
};

// --- 4. マウス / ポインター入力 ---
function getCanvasCoords(e) {
  const rect = canvas.getBoundingClientRect();
  return {
    x: (e.clientX - rect.left - viewOffsetX) / viewScale,
    y: (e.clientY - rect.top - viewOffsetY) / viewScale,
  };
}

function handlePointerDown(e) {
  // 主ボタン(左クリック / タッチ)のみ反応
  if (e.button !== undefined && e.button !== 0) return;

  const pos = getCanvasCoords(e);

  // Web Audio API の初期化 / resume (自動再生ポリシー対策)
  if (window.AudioController) {
    AudioController.init();
  }

  // ミュートボタン上のクリック判定 (ラウンド開始として扱わず早期リターン)
  if (MUTE_BTN.contains(pos.x, pos.y)) {
    if (window.AudioController) {
      AudioController.toggleMute();
    }
    return;
  }

  cursorX = pos.x;
  cursorY = pos.y;

  // ラウンド開始 (粒子を再ランダム配置して新ラウンド)
  startRound(performance.now());
}

function handlePointerMove(e) {
  const pos = getCanvasCoords(e);
  cursorX = pos.x;
  cursorY = pos.y;

  // ミュートボタン上ではカーソルをポインターにする
  if (MUTE_BTN.contains(pos.x, pos.y)) {
    canvas.style.cursor = 'pointer';
  } else {
    canvas.style.cursor = 'default';
  }
}

function handlePointerUp(e) {
  if (e.button !== undefined && e.button !== 0) return;
  confirmRound();
}

function startRound(now) {
  initParticles();
  gameState = 'ATTRACTING';
  isPressing = true;
  pressStartTime = now;
  currentDensity = 0;
  currentDangerRatio = 0;
  currentScore = 0;
  finalScore = 0;
  graceCounter = 0;
  dangerStage = 'SAFE';
  shakeMagnitude = 0;
  flashOpacity = 0;
  reachableCount = CONFIG.TOTAL_PARTICLES;
  bangPossible = true;
  unreachableFrames = 0;
  for (let i = 0; i < particles.length; i++) {
    particles[i].outOfReach = false;
  }

  // 押下中の持続音(ドローン)を開始 (押した瞬間から鳴る)
  if (window.AudioController && AudioController.startDrone) {
    AudioController.startDrone();
  }
}

function confirmRound() {
  if (!isPressing) return;
  isPressing = false;
  reachableCount = CONFIG.TOTAL_PARTICLES;
  bangPossible = true;
  unreachableFrames = 0;
  for (let i = 0; i < particles.length; i++) {
    particles[i].outOfReach = false;
  }

  // ドローン停止 & 警告パルス停止
  if (window.AudioController) {
    if (AudioController.stopDrone) {
      AudioController.stopDrone();
    }
    AudioController.updateWarning(0, 0);
  }

  if (gameState === 'ATTRACTING') {
    // 正常リリースで確定
    gameState = 'RESOLVED';
    finalScore = currentScore;
    if (window.AudioController) {
      AudioController.playResolve();
    }
    if (finalScore > highScore) {
      highScore = finalScore;
      try {
        localStorage.setItem('bangs_edge_high_score', highScore.toString());
      } catch (e) {}
    }
  }
}

// イベントリスナー登録
canvas.addEventListener('pointerdown', handlePointerDown);
window.addEventListener('pointermove', handlePointerMove);
window.addEventListener('pointerup', handlePointerUp);
window.addEventListener('pointercancel', confirmRound);

// --- 5. ビッグバン発生処理 ---
function triggerBigBang() {
  gameState = 'BANG';
  isPressing = false;
  currentScore = 0;
  finalScore = 0; // 仕様: そのラウンドのスコアは0
  graceCounter = 0;
  flashOpacity = 0.95;
  shakeMagnitude = 22;
  reachableCount = CONFIG.TOTAL_PARTICLES;
  bangPossible = true;
  unreachableFrames = 0;
  for (let i = 0; i < particles.length; i++) {
    particles[i].outOfReach = false;
  }

  // ドローン停止 & 警告パルス停止 & ビッグバン爆発音再生
  if (window.AudioController) {
    if (AudioController.stopDrone) {
      AudioController.stopDrone();
    }
    AudioController.updateWarning(0, 0);
    AudioController.playBigBang();
  }

  // 全粒子をカーソル中心から外側へ放射状に爆発飛散させる
  for (let i = 0; i < particles.length; i++) {
    const p = particles[i];
    const dx = p.x - cursorX;
    const dy = p.y - cursorY;
    const dist = Math.hypot(dx, dy) || 1;
    const angle = Math.atan2(dy, dx) + (Math.random() - 0.5) * 0.6;
    const blastSpeed = 450 + Math.random() * 850;
    p.vx = Math.cos(angle) * blastSpeed;
    p.vy = Math.sin(angle) * blastSpeed;
  }
}

// --- 6. 物理運動・密度・危険度の更新 ---
function update(dt, now) {
  // 演出値の減衰
  shakeMagnitude = Math.max(0, shakeMagnitude - dt * 25);
  flashOpacity = Math.max(0, flashOpacity - dt * 2.2);

  // 減衰をフレーム単位で掛けると高リフレッシュレート環境で効きすぎるため、60fps基準の時間指数にする
  const dampFactor = Math.pow(CONFIG.DAMPING, dt * 60);

  if (gameState === 'ATTRACTING') {
    const t = Math.max(0, (now - pressStartTime) / 1000);

    // 指数飽和曲線による集積半径 r(t) と引力 F(t) の計算 (引力に F_CREEP を加算)
    const currentR = CONFIG.R_MIN + (effectiveRMax - CONFIG.R_MIN) * (1 - Math.exp(-t / CONFIG.TAU_R));
    const currentF = CONFIG.F_MIN + (CONFIG.F_MAX - CONFIG.F_MIN) * (1 - Math.exp(-t / CONFIG.TAU_F)) + CONFIG.F_CREEP * t;

    // 引力の中心をカーソルから微小に揺動
    const attractX = cursorX + CONFIG.WOBBLE_AX * Math.cos(2 * Math.PI * CONFIG.WOBBLE_F1 * t);
    const attractY = cursorY + CONFIG.WOBBLE_AY * Math.sin(2 * Math.PI * CONFIG.WOBBLE_F2 * t);

    // 粒子間反発の加速度を集計 (Float64Arrayバッファ再利用)
    repAx.fill(0);
    repAy.fill(0);
    const d0 = CONFIG.REPULSION_D0;
    const d0Sq = d0 * d0;
    const eps = CONFIG.REPULSION_EPS;
    const k = CONFIG.REPULSION_K;

    for (let i = 0; i < particles.length; i++) {
      const pi = particles[i];
      for (let j = i + 1; j < particles.length; j++) {
        const pj = particles[j];
        const dx = pj.x - pi.x;
        if (dx > d0 || dx < -d0) continue;
        const dy = pj.y - pi.y;
        if (dy > d0 || dy < -d0) continue;
        const d2 = dx * dx + dy * dy;
        if (d2 >= d0Sq) continue;

        const d = Math.sqrt(d2);
        const mag = k * (1 - Math.max(d, eps) / d0);
        let ux, uy;
        if (d >= eps) {
          ux = dx / d;
          uy = dy / d;
        } else {
          // 粒子が完全に重なっている: 添字から決まる固定方向を使う (Math.randomは使わない)
          const h = ((i * 73856093) ^ (j * 19349663)) >>> 0;
          const ang = (h % 62832) / 10000;
          ux = Math.cos(ang);
          uy = Math.sin(ang);
        }

        repAx[i] -= ux * mag;
        repAy[i] -= uy * mag;
        repAx[j] += ux * mag;
        repAy[j] += uy * mag;
      }
    }

    const rMeasureSq = CONFIG.R_MEASURE * CONFIG.R_MEASURE;
    const currentRSq = currentR * currentR;
    const reachSq = effectiveRMax * effectiveRMax;
    let nMeasure = 0;
    let nReach = 0;

    for (let i = 0; i < particles.length; i++) {
      const p = particles[i];

      // 固定測定円 R_MEASURE 内の判定 (※重要: 生の cursorX / cursorY を中心に行う)
      const mdx = cursorX - p.x;
      const mdy = cursorY - p.y;
      const mDistSq = mdx * mdx + mdy * mdy;

      if (mDistSq <= rMeasureSq) {
        p.inMeasure = true;
        nMeasure++;
      } else {
        p.inMeasure = false;
      }

      if (mDistSq <= reachSq) {
        p.outOfReach = false;
        nReach++;
      } else {
        p.outOfReach = true;
      }

      // 粒子の合成加速度の計算
      let accX = 0;
      let accY = 0;

      // 引力中心 (attractX, attractY) からの距離と集積判定
      const adx = attractX - p.x;
      const ady = attractY - p.y;
      const aDistSq = adx * adx + ady * ady;

      if (aDistSq <= currentRSq) {
        // 引力圏内: 引力中心方向への加速 (距離の下限クリップ 1.0 は従来どおり)
        const aDist = Math.sqrt(aDistSq);
        const effectiveDist = Math.max(aDist, 1.0);
        const dirX = adx / effectiveDist;
        const dirY = ady / effectiveDist;

        accX += dirX * currentF;
        accY += dirY * currentF;
      } else {
        // 引力圏外: 微弱なランダム揺動 (Math.random() の加速度)
        accX += (Math.random() - 0.5) * 2 * JITTER_FORCE;
        accY += (Math.random() - 0.5) * 2 * JITTER_FORCE;
      }

      // 粒子間反発加速度を加算
      accX += repAx[i];
      accY += repAy[i];

      // 合成加速度のクリッピング (ACCEL_CLIP)
      const accMag = Math.hypot(accX, accY);
      if (accMag > CONFIG.ACCEL_CLIP) {
        const accScale = CONFIG.ACCEL_CLIP / accMag;
        accX *= accScale;
        accY *= accScale;
      }

      // 速度更新と減衰 (減衰の掛かる順序は現行と同じ: (v + a*dt) * dampFactor)
      p.vx = (p.vx + accX * dt) * dampFactor;
      p.vy = (p.vy + accY * dt) * dampFactor;

      // 速度上限クリッピング (VELOCITY_CLIP)
      const vMag = Math.hypot(p.vx, p.vy);
      if (vMag > CONFIG.VELOCITY_CLIP) {
        const vScale = CONFIG.VELOCITY_CLIP / vMag;
        p.vx *= vScale;
        p.vy *= vScale;
      }

      // 位置更新
      p.x += p.vx * dt;
      p.y += p.vy * dt;

      // 画面端で反射
      handleBoundaryBounce(p);
    }

    reachableCount = nReach;
    // 粒子が動く過程で一時的に下回ることがあるため、持続して初めて警告する
    // (下回った状態が続いたときだけ警告し、回復したら即座に解除する非対称な判定)
    if (nReach >= REQUIRED_PARTICLES) {
      unreachableFrames = 0;
      bangPossible = true;
    } else {
      unreachableFrames++;
      if (unreachableFrames >= UNREACHABLE_WARN_FRAMES) bangPossible = false;
    }

    // 密度計算 (※厳守: 分母は集積半径 r(t) ではなく固定の R_MEASURE)
    currentDensity = nMeasure / CONFIG.MEASURE_AREA;
    currentDangerRatio = currentDensity / CONFIG.BANG_THRESHOLD;
    currentScore = Math.floor((currentDensity / CONFIG.DENSITY_MAX) * 9999);

    // 警告パルスの更新 (毎フレーム currentDangerRatio を渡す)
    if (window.AudioController) {
      AudioController.updateWarning(currentDangerRatio, dt);
    }

    // 危険度4段階の判定
    if (currentDangerRatio < CONFIG.DANGER_WARM) {
      dangerStage = 'SAFE';
      shakeMagnitude = 0;
    } else if (currentDangerRatio < CONFIG.DANGER_HOT) {
      dangerStage = 'WARM';
      shakeMagnitude = 0.8;
    } else if (currentDangerRatio < CONFIG.DANGER_CRITICAL) {
      dangerStage = 'HOT';
      shakeMagnitude = 2.4;
    } else {
      dangerStage = 'CRITICAL';
      shakeMagnitude = 5.5 + Math.random() * 2.0;
    }

    // ビッグバン判定 (BANG_GRACE_FRAMES連続でしきい値超過したら発火)
    if (currentDensity >= CONFIG.BANG_THRESHOLD) {
      graceCounter++;
      if (graceCounter >= CONFIG.BANG_GRACE_FRAMES) {
        triggerBigBang();
      }
    } else {
      graceCounter = 0;
    }

  } else {
    // 待機中 / 確定後 / ビッグバン後 (警告パルス停止)
    if (window.AudioController) {
      AudioController.updateWarning(0, dt);
    }
    for (let i = 0; i < particles.length; i++) {
      const p = particles[i];
      p.inMeasure = false;

      // 待機中または確定後は微弱な揺動でゆっくり漂う
      if (gameState !== 'BANG') {
        p.vx += (Math.random() - 0.5) * 2 * (JITTER_FORCE * 0.4) * dt;
        p.vy += (Math.random() - 0.5) * 2 * (JITTER_FORCE * 0.4) * dt;
      }

      // 速度減衰
      p.vx *= dampFactor;
      p.vy *= dampFactor;

      // 位置更新
      p.x += p.vx * dt;
      p.y += p.vy * dt;

      // 画面端で反射
      handleBoundaryBounce(p);
    }
  }
}

// 画面端反射処理
function handleBoundaryBounce(p) {
  const r = 2.5;
  if (p.x < r) {
    p.x = r;
    p.vx = -p.vx * 0.8;
  } else if (p.x > width - r) {
    p.x = width - r;
    p.vx = -p.vx * 0.8;
  }

  if (p.y < r) {
    p.y = r;
    p.vy = -p.vy * 0.8;
  } else if (p.y > height - r) {
    p.y = height - r;
    p.vy = -p.vy * 0.8;
  }
}

// --- 7. 描画処理 ---

// 角丸矩形描画ヘルパー
function drawRoundedRect(targetCtx, x, y, w, h, radius) {
  targetCtx.beginPath();
  targetCtx.moveTo(x + radius, y);
  targetCtx.lineTo(x + w - radius, y);
  targetCtx.quadraticCurveTo(x + w, y, x + w, y + radius);
  targetCtx.lineTo(x + w, y + h - radius);
  targetCtx.quadraticCurveTo(x + w, y + h, x + w - radius, y + h);
  targetCtx.lineTo(x + radius, y + h);
  targetCtx.quadraticCurveTo(x, y + h, x, y + h - radius);
  targetCtx.lineTo(x, y + radius);
  targetCtx.quadraticCurveTo(x, y, x + radius, y);
  targetCtx.closePath();
}

function draw(now) {
  // 1. キャンバス全体を実ピクセルでクリア (レターボックスの帯)
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  ctx.fillStyle = '#000000';
  ctx.fillRect(0, 0, viewportW, viewportH);

  // 2. 以降はすべて論理座標で描く
  ctx.setTransform(dpr * viewScale, 0, 0, dpr * viewScale, dpr * viewOffsetX, dpr * viewOffsetY);

  // 3. 盤面の背景
  ctx.fillStyle = '#05060a';
  ctx.fillRect(0, 0, width, height);

  // --- ワールド描画 (画面振動適用) ---
  ctx.save();
  if (shakeMagnitude > 0) {
    const sx = (Math.random() * 2 - 1) * shakeMagnitude;
    const sy = (Math.random() * 2 - 1) * shakeMagnitude;
    ctx.translate(sx, sy);
  }

  // 集積円 & 測定円の描画 (吸引中のみ)
  if (gameState === 'ATTRACTING') {
    drawFields(now);
  }

  // 粒子の描画 (危険度4段階に応じた色変化)
  drawParticles();

  // フラッシュ演出
  if (flashOpacity > 0) {
    ctx.fillStyle = `rgba(255, 255, 255, ${Math.min(1, flashOpacity)})`;
    ctx.fillRect(0, 0, width, height);
  }

  ctx.restore(); // 画面振動終了

  // --- HUD描画 (画面上部に固定表示) ---
  drawHUD(now);

  // 盤面の境界線 (レターボックスとの境目を示す)
  ctx.strokeStyle = 'rgba(255, 255, 255, 0.12)';
  ctx.lineWidth = 2;
  ctx.strokeRect(1, 1, width - 2, height - 2);
}

// 集積円と固定測定円の描画
function drawFields(now) {
  const t = Math.max(0, (now - pressStartTime) / 1000);
  const currentR = CONFIG.R_MIN + (effectiveRMax - CONFIG.R_MIN) * (1 - Math.exp(-t / CONFIG.TAU_R));

  // 1. 集積円 r(t) の描画
  let circleColor = 'rgba(255, 255, 255, 0.4)';
  let circleWidth = 1.5;
  let glowColor = 'transparent';
  let glowBlur = 0;

  if (dangerStage === 'WARM') {
    circleColor = 'rgba(255, 234, 0, 0.7)';
    circleWidth = 2.5;
    glowColor = 'rgba(255, 234, 0, 0.4)';
    glowBlur = 6;
  } else if (dangerStage === 'HOT') {
    circleColor = 'rgba(255, 59, 48, 0.85)';
    circleWidth = 3.5;
    glowColor = 'rgba(255, 59, 48, 0.6)';
    glowBlur = 12;
  } else if (dangerStage === 'CRITICAL') {
    // 激しく脈動・発光
    const pulse = 0.8 + 0.2 * Math.sin(now * 0.025);
    circleColor = `rgba(255, 255, 255, ${pulse})`;
    circleWidth = 5.0;
    glowColor = 'rgba(255, 34, 0, 0.9)';
    glowBlur = 20;
  }

  // 集積フィールドの薄い塗りと輪郭
  ctx.save();
  ctx.beginPath();
  ctx.arc(cursorX, cursorY, currentR, 0, Math.PI * 2);
  ctx.fillStyle = dangerStage === 'CRITICAL'
    ? 'rgba(255, 50, 0, 0.08)'
    : 'rgba(255, 255, 255, 0.03)';
  ctx.fill();

  ctx.strokeStyle = circleColor;
  ctx.lineWidth = circleWidth;
  if (glowBlur > 0) {
    ctx.shadowColor = glowColor;
    ctx.shadowBlur = glowBlur;
  }
  ctx.stroke();
  ctx.restore();

  // 2. 固定測定円 R_MEASURE の描画 (点線ターゲット)
  ctx.save();
  ctx.setLineDash([4, 4]);
  ctx.strokeStyle = 'rgba(0, 229, 255, 0.7)';
  ctx.lineWidth = 1.5;
  ctx.beginPath();
  ctx.arc(cursorX, cursorY, CONFIG.R_MEASURE, 0, Math.PI * 2);
  ctx.stroke();

  // 中心十字レティクル
  ctx.setLineDash([]);
  ctx.strokeStyle = 'rgba(0, 229, 255, 0.5)';
  ctx.lineWidth = 1;
  ctx.beginPath();
  ctx.moveTo(cursorX - 5, cursorY);
  ctx.lineTo(cursorX + 5, cursorY);
  ctx.moveTo(cursorX, cursorY - 5);
  ctx.lineTo(cursorX, cursorY + 5);
  ctx.stroke();
  ctx.restore();
}

// 粒子の描画 (4段階の色変化: 白→黄→赤→白熱)
function drawParticles() {
  let outerColor = '#cbd5e1'; // 白系
  let innerColor = '#ffffff';
  let glowBlur = 0;
  let glowColor = 'transparent';

  if (gameState === 'BANG') {
    outerColor = '#ff3300';
    innerColor = '#fff5cc';
    glowBlur = 8;
    glowColor = '#ff6600';
  } else if (dangerStage === 'WARM') {
    outerColor = '#ffea00';
    innerColor = '#fff9a6';
  } else if (dangerStage === 'HOT') {
    outerColor = '#ff3b30';
    innerColor = '#ffa39e';
    glowBlur = 4;
    glowColor = '#ff3b30';
  } else if (dangerStage === 'CRITICAL') {
    outerColor = '#ff3300';
    innerColor = '#ffffff';
    glowBlur = 10;
    glowColor = '#ff0033';
  }

  // 第1パス: 射程外粒子 (暗く表示、影なし)
  ctx.save();
  ctx.globalAlpha = 0.25;
  ctx.fillStyle = '#64748b';
  ctx.beginPath();
  for (let i = 0; i < particles.length; i++) {
    const p = particles[i];
    if (p.outOfReach) {
      ctx.moveTo(p.x + 2.2, p.y);
      ctx.arc(p.x, p.y, 2.2, 0, Math.PI * 2);
    }
  }
  ctx.fill();
  ctx.globalAlpha = 1.0;
  ctx.restore();

  // 第2パス: 測定円外かつ射程内の粒子 (通常)
  ctx.save();
  if (glowBlur > 0) {
    ctx.shadowColor = glowColor;
    ctx.shadowBlur = glowBlur;
  }
  ctx.fillStyle = outerColor;
  ctx.beginPath();
  for (let i = 0; i < particles.length; i++) {
    const p = particles[i];
    if (!p.inMeasure && !p.outOfReach) {
      ctx.moveTo(p.x + 2.2, p.y);
      ctx.arc(p.x, p.y, 2.2, 0, Math.PI * 2);
    }
  }
  ctx.fill();

  // 第3パス: 測定円内粒子 (ハイライト)
  ctx.fillStyle = innerColor;
  ctx.beginPath();
  for (let i = 0; i < particles.length; i++) {
    const p = particles[i];
    if (p.inMeasure) {
      ctx.moveTo(p.x + 3.2, p.y);
      ctx.arc(p.x, p.y, 3.2, 0, Math.PI * 2);
    }
  }
  ctx.fill();
  ctx.restore();
}

// HUD描画
function drawHUD(now) {
  // 1. 上部密度バー (BANG THRESHOLD = 85%)
  const barW = Math.min(420, Math.max(260, width - 40));
  const barH = 14;
  const barX = (width - barW) / 2;
  const barY = 24;

  // バー背景
  ctx.fillStyle = 'rgba(12, 14, 24, 0.85)';
  drawRoundedRect(ctx, barX, barY, barW, barH, 4);
  ctx.fill();
  ctx.strokeStyle = 'rgba(255, 255, 255, 0.2)';
  ctx.lineWidth = 1;
  ctx.stroke();

  // バーのゲージ塗り
  // density / DENSITY_MAX (0.0 〜 1.0)
  const fillRatio = Math.max(0, Math.min(1.0, currentDensity / CONFIG.DENSITY_MAX));
  const fillW = barW * fillRatio;

  if (fillW > 0) {
    ctx.save();
    let barFillColor = '#00e5ff'; // SAFE: シアン
    if (dangerStage === 'WARM') barFillColor = '#ffea00'; // WARM: 黄
    else if (dangerStage === 'HOT') barFillColor = '#ff5500'; // HOT: 橙赤
    else if (dangerStage === 'CRITICAL') {
      // 白熱点滅
      barFillColor = (Math.floor(now / 80) % 2 === 0) ? '#ffffff' : '#ff0033';
    }

    ctx.fillStyle = barFillColor;
    drawRoundedRect(ctx, barX, barY, fillW, barH, 4);
    ctx.fill();
    ctx.restore();
  }

  // 85% の BANG_THRESHOLD マーク線
  const limitX = barX + barW * 0.85;
  ctx.save();
  ctx.strokeStyle = '#ff3333';
  ctx.lineWidth = 2;
  ctx.beginPath();
  ctx.moveTo(limitX, barY - 3);
  ctx.lineTo(limitX, barY + barH + 3);
  ctx.stroke();

  // BANG LIMIT ラベル
  ctx.fillStyle = '#ff4444';
  ctx.font = 'bold 10px monospace';
  ctx.textAlign = 'center';
  ctx.fillText('BANG LIMIT (85%)', limitX, barY + barH + 15);
  ctx.restore();

  // 危険度ステージ表示
  ctx.save();
  let stageText = `DANGER: ${dangerStage}`;
  let stageColor = '#94a3b8';
  if (dangerStage === 'WARM') stageColor = '#ffea00';
  else if (dangerStage === 'HOT') stageColor = '#ff5500';
  else if (dangerStage === 'CRITICAL') {
    stageText = `CRITICAL WARNING (${graceCounter}/${CONFIG.BANG_GRACE_FRAMES})`;
    stageColor = (Math.floor(now / 100) % 2 === 0) ? '#ff0033' : '#ffffff';
  }

  ctx.fillStyle = stageColor;
  ctx.font = 'bold 12px monospace';
  ctx.textAlign = 'center';
  ctx.fillText(stageText, width / 2, barY - 8);
  ctx.restore();

  // 射程可視化表示 (ATTRACTING時のみ)
  if (gameState === 'ATTRACTING') {
    ctx.save();
    ctx.textAlign = 'center';

    // 1行目: 射程内粒子数
    ctx.font = 'bold 11px monospace';
    ctx.fillStyle = bangPossible ? 'rgba(148, 163, 184, 0.9)' : '#ff4444';
    ctx.fillText(`射程内 ${reachableCount} / ${CONFIG.TOTAL_PARTICLES}  (必要 ${REQUIRED_PARTICLES})`, width / 2, barY + barH + 32);

    // 2行目: 到達不能警告 (bangPossible === false のときのみ)
    if (!bangPossible) {
      ctx.font = 'bold 12px monospace';
      ctx.fillStyle = (Math.floor(now / 400) % 2 === 0) ? '#ff4444' : '#ff9999';
      ctx.fillText('この位置では到達不能 — 画面の中央へ', width / 2, barY + barH + 48);
    }

    ctx.restore();
  }

  // 描画性能の実測表示 (物理は固定ステップなので fps が落ちても挙動は変わらない)
  ctx.save();
  ctx.font = '10px monospace';
  ctx.textAlign = 'left';
  ctx.fillStyle = fpsEstimate < 50 ? 'rgba(255, 153, 153, 0.85)' : 'rgba(148, 163, 184, 0.55)';
  ctx.fillText(`${Math.round(fpsEstimate)} fps`, 16, height - 16);
  ctx.restore();

  // 2. スコア・ハイスコア表示
  ctx.save();
  ctx.font = 'bold 16px monospace';
  ctx.fillStyle = '#ffffff';

  // 左上: 現在スコア / 確定スコア
  const displayScore = (gameState === 'RESOLVED') ? finalScore : currentScore;
  ctx.textAlign = 'left';
  ctx.fillText(`SCORE: ${displayScore.toString().padStart(4, '0')}`, 20, 36);

  // 右上: ハイスコア (ミュートボタンの左側に適度な余白を空けて配置)
  ctx.textAlign = 'right';
  ctx.fillStyle = '#ffd700';
  ctx.fillText(`HIGH: ${highScore.toString().padStart(4, '0')}`, width - MUTE_BTN.marginRight - MUTE_BTN.size - 14, 36);
  ctx.restore();

  // 3. 画面中央 / ガイドテキスト
  ctx.save();
  ctx.textAlign = 'center';

  if (gameState === 'READY') {
    ctx.font = 'bold 22px sans-serif';
    ctx.fillStyle = '#ffffff';
    ctx.fillText("BANG'S-EDGE", width / 2, height / 2 - 20);
    ctx.font = '14px sans-serif';
    ctx.fillStyle = '#94a3b8';
    ctx.fillText('CLICK & HOLD TO ACCUMULATE PARTICLES', width / 2, height / 2 + 15);
    ctx.fillText('RELEASE BEFORE BIG BANG TO LOCK SCORE', width / 2, height / 2 + 38);
  } else if (gameState === 'RESOLVED') {
    ctx.font = 'bold 24px sans-serif';
    ctx.fillStyle = '#00e5ff';
    ctx.fillText('ROUND RESOLVED!', width / 2, height / 2 - 25);
    ctx.font = 'bold 36px monospace';
    ctx.fillStyle = '#ffffff';
    ctx.fillText(`SCORE: ${finalScore}`, width / 2, height / 2 + 18);
    if (finalScore === highScore && finalScore > 0) {
      ctx.font = 'bold 14px monospace';
      ctx.fillStyle = '#ffd700';
      ctx.fillText('★ NEW HIGH SCORE! ★', width / 2, height / 2 + 45);
    }
    ctx.font = '14px sans-serif';
    ctx.fillStyle = '#94a3b8';
    ctx.fillText('CLICK TO START NEXT ROUND', width / 2, height / 2 + 75);
  } else if (gameState === 'BANG') {
    ctx.font = 'bold 32px sans-serif';
    ctx.fillStyle = '#ff2200';
    ctx.shadowColor = '#ff0000';
    ctx.shadowBlur = 15;
    ctx.fillText('💥 BIG BANG DETECTED! 💥', width / 2, height / 2 - 20);
    ctx.shadowBlur = 0;
    ctx.font = 'bold 18px monospace';
    ctx.fillStyle = '#ffa39e';
    ctx.fillText('ROUND FAILED — SCORE: 0', width / 2, height / 2 + 20);
    ctx.font = '14px sans-serif';
    ctx.fillStyle = '#94a3b8';
    ctx.fillText('CLICK TO RETRY', width / 2, height / 2 + 55);
  }
  ctx.restore();

  // 4. ミュート切替ボタン
  drawMuteButton();
}

// ミュート切替ボタンのCanvas描画 (スピーカーのオン/オフ)
function drawMuteButton() {
  const rect = MUTE_BTN.getRect();
  const isMuted = window.AudioController ? AudioController.isMuted() : false;

  ctx.save();
  // ボタン枠・半透明背景
  ctx.fillStyle = 'rgba(15, 23, 42, 0.7)';
  drawRoundedRect(ctx, rect.x, rect.y, rect.w, rect.h, 6);
  ctx.fill();
  ctx.strokeStyle = isMuted ? 'rgba(148, 163, 184, 0.4)' : 'rgba(0, 229, 255, 0.4)';
  ctx.lineWidth = 1;
  ctx.stroke();

  // スピーカーアイコン
  const cx = rect.x + rect.w / 2;
  const cy = rect.y + rect.h / 2;
  const iconColor = isMuted ? '#94a3b8' : '#00e5ff';

  ctx.fillStyle = iconColor;
  ctx.strokeStyle = iconColor;
  ctx.lineWidth = 1.6;
  ctx.lineCap = 'round';
  ctx.lineJoin = 'round';

  // スピーカーコーン部
  ctx.beginPath();
  const sx = cx - 6.5;
  const sy = cy - 3.5;
  ctx.moveTo(sx, sy);
  ctx.lineTo(sx + 3, sy);
  ctx.lineTo(sx + 6.5, cy - 6.5);
  ctx.lineTo(sx + 6.5, cy + 6.5);
  ctx.lineTo(sx + 3, cy + 3.5);
  ctx.lineTo(sx, cy + 3.5);
  ctx.closePath();
  ctx.fill();

  if (isMuted) {
    // ミュート時: × マーク
    ctx.beginPath();
    ctx.moveTo(cx + 2.5, cy - 3.5);
    ctx.lineTo(cx + 7.5, cy + 3.5);
    ctx.moveTo(cx + 7.5, cy - 3.5);
    ctx.lineTo(cx + 2.5, cy + 3.5);
    ctx.stroke();
  } else {
    // オン時: 音波アーク
    ctx.beginPath();
    ctx.arc(cx + 2, cy, 4, -Math.PI / 3, Math.PI / 3, false);
    ctx.stroke();
    ctx.beginPath();
    ctx.arc(cx + 2, cy, 7.5, -Math.PI / 3.2, Math.PI / 3.2, false);
    ctx.stroke();
  }

  ctx.restore();
}

// --- 8. メインループ ---
let lastTimestamp = 0;

// 物理は常にこの固定ステップで進める。
// 反発力(REPULSION_K)は硬く、明示的オイラー法では dt が 1/60 を超えると解けなくなる。
// 実測では 40fps 相当の dt で塊が詰まらなくなり、ビッグバンが 0/10 で起きなくなった。
// 描画のフレームレートと物理を切り離すことで、どの環境でも同じゲームになる。
const FIXED_DT = 1 / 60;
const MAX_SUBSTEPS = 5;   // 1フレームで進める上限 (遅い環境でのデススパイラル防止)
let accumulator = 0;
let simNow = 0;           // 物理が到達している時刻 (ms)
let fpsEstimate = 60;     // 表示用の指数移動平均

function loop(timestamp) {
  if (!lastTimestamp) {
    lastTimestamp = timestamp;
    simNow = timestamp;
  }
  const rawDt = (timestamp - lastTimestamp) / 1000;
  lastTimestamp = timestamp;

  if (rawDt > 0) fpsEstimate += (1 / rawDt - fpsEstimate) * 0.1;

  // タブ復帰などで大きく飛んだ分は捨てる
  accumulator += Math.min(rawDt, 0.25);

  let steps = 0;
  while (accumulator >= FIXED_DT && steps < MAX_SUBSTEPS) {
    simNow += FIXED_DT * 1000;
    update(FIXED_DT, simNow);
    accumulator -= FIXED_DT;
    steps++;
  }
  // 上限まで進めても追いつけない場合は残りを捨てる (スローモーションにはなるが破綻はしない)
  if (accumulator >= FIXED_DT) {
    accumulator = 0;
    simNow = timestamp;
  }

  draw(timestamp);

  requestAnimationFrame(loop);
}

requestAnimationFrame(loop);
