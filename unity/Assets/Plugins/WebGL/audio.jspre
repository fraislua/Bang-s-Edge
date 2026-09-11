/**
 * Bang's-Edge - Audio Controller (Web Audio API)
 * docs/game-concept.md の「音」節に基づく実装
 * 外部音声ファイルを使用せず、すべて Web Audio API でリアルタイム合成
 */

(function (window) {
  'use strict';

  // ミュート永続化キー
  const STORAGE_KEY_MUTED = 'bangs_edge_muted';

  // 内部状態
  let audioCtx = null;
  let masterGain = null;
  let noiseBuffer = null;
  let isMutedState = false;

  // 警告パルス管理
  let pulseTimer = 0;

  // ドローン(持続音)管理
  let isDroneActive = false;
  let droneOscA = null;         // Drone A: sine 110Hz固定
  let droneGainA = null;        // Drone A gain: 0.32固定
  let droneOscB = null;         // Drone B: sine (110 + beat Hz)
  let droneGainB = null;        // Drone B gain: 0.26固定
  let droneOsc5th = null;       // 5度: sine 165Hz固定
  let droneGain5th = null;      // 5度 gain: 0.10 * (1 - 0.5 * p)
  let droneOscInharm = null;    // 非調和: sine 300.3Hz固定
  let droneGainInharm = null;   // 非調和 gain: 0.02 + 0.16 * p
  let droneNoiseSource = null;  // ノイズ BufferSource (loop)
  let droneNoiseFilter = null;  // ノイズ bandpass (700 + 1300*p, Q: 1.4 + 4.0*p)
  let droneNoiseGain = null;    // ノイズ gain: 0.018 + 0.08 * p
  let droneFilter = null;       // 音色ローパス (1400Hz固定, Q: 0.8 + 2.8*p)
  let droneAmGain = null;       // AM適用gainノード
  let droneLfoOsc = null;       // AM LFO: sine (1.6 + 9.4*p Hz)
  let droneLfoGain = null;      // AM depth gain: 0.07 + 0.32 * p
  let droneMasterGain = null;   // ドローン全体のマスターgain: 0.22固定

  // ミュート状態の復元
  try {
    const saved = localStorage.getItem(STORAGE_KEY_MUTED);
    if (saved !== null) {
      isMutedState = (saved === 'true');
    }
  } catch (e) {
    // localStorage非対応環境対策
    isMutedState = false;
  }

  /**
   * AudioContextの遅延生成 & 状態確認 (自動再生ポリシー対応)
   */
  function initAudio() {
    try {
      const AudioCtxClass = window.AudioContext || window.webkitAudioContext;
      if (!AudioCtxClass) return false;

      if (!audioCtx) {
        audioCtx = new AudioCtxClass();

        // マスターゲインノード
        masterGain = audioCtx.createGain();
        masterGain.gain.setValueAtTime(isMutedState ? 0 : 1, audioCtx.currentTime);
        masterGain.connect(audioCtx.destination);

        // ホワイトノイズ用バッファの事前生成 (2.5秒分)
        createNoiseBuffer();
      }

      if (audioCtx.state === 'suspended') {
        audioCtx.resume().catch(function () {});
      }
      return true;
    } catch (e) {
      // AudioContext未サポートまたは初期化エラー時の防御
      return false;
    }
  }

  /**
   * ホワイトノイズバッファの生成 (2.5秒分)
   */
  function createNoiseBuffer() {
    if (!audioCtx) return;
    try {
      const sampleRate = audioCtx.sampleRate || 44100;
      const bufferSize = Math.floor(sampleRate * 2.5); // 2.5秒分 (ビッグバン1.8秒に対応)
      noiseBuffer = audioCtx.createBuffer(1, bufferSize, sampleRate);
      const data = noiseBuffer.getChannelData(0);
      for (let i = 0; i < bufferSize; i++) {
        data[i] = Math.random() * 2 - 1;
      }
    } catch (e) {
      noiseBuffer = null;
    }
  }

  /**
   * ミュート切り替え
   * @returns {boolean} 切り替え後のミュート状態
   */
  function toggleMute() {
    isMutedState = !isMutedState;
    try {
      localStorage.setItem(STORAGE_KEY_MUTED, isMutedState ? 'true' : 'false');
    } catch (e) {}

    if (audioCtx && masterGain) {
      try {
        const now = audioCtx.currentTime;
        masterGain.gain.cancelScheduledValues(now);
        masterGain.gain.setValueAtTime(isMutedState ? 0 : 1, now);
      } catch (e) {}
    }

    return isMutedState;
  }

  /**
   * 現在のミュート状態を取得
   * @returns {boolean}
   */
  function isMuted() {
    return isMutedState;
  }

  /**
   * 警告パルス (短く鋭いビープ粒) の再生
   * @param {number} freq 周波数 (Hz)
   */
  function playPulseSound(freq) {
    if (isMutedState || !audioCtx) return;
    try {
      const now = audioCtx.currentTime;
      const osc = audioCtx.createOscillator();
      const gain = audioCtx.createGain();

      osc.type = 'sine';
      osc.frequency.setValueAtTime(freq, now);

      // 各粒: sine、gain 0.18、tau 0.035s、実質40〜60ms
      const attack = 0.003;
      gain.gain.setValueAtTime(0.0001, now);
      gain.gain.linearRampToValueAtTime(0.18, now + attack);
      gain.gain.setTargetAtTime(0, now + attack, 0.035);

      osc.connect(gain);
      gain.connect(masterGain);

      osc.start(now);
      osc.stop(now + 0.08);
    } catch (e) {}
  }

  /**
   * 押下中の持続音(ドローン)を開始
   * ピッチスイープを行わず、うなり・振幅変調(AM)・非調和成分・帯域制限ノイズで緊張を表現
   * 基準周波数は110Hz(失敗音ボディと同じ)に統一
   */
  function startDrone() {
    if (!initAudio()) return;
    if (isDroneActive) {
      stopDrone(true);
    }

    try {
      if (!noiseBuffer) {
        createNoiseBuffer();
      }

      const now = audioCtx.currentTime;

      // 1. Drone A: sine 110Hz固定、gain 0.32固定
      droneOscA = audioCtx.createOscillator();
      droneOscA.type = 'sine';
      droneOscA.frequency.setValueAtTime(110, now);
      droneGainA = audioCtx.createGain();
      droneGainA.gain.setValueAtTime(0.32, now);
      droneOscA.connect(droneGainA);

      // 2. Drone B: sine 周波数 = 110 + beat Hz、gain 0.26固定 (初期 beat = 0.5Hz)
      droneOscB = audioCtx.createOscillator();
      droneOscB.type = 'sine';
      droneOscB.frequency.setValueAtTime(110.5, now);
      droneGainB = audioCtx.createGain();
      droneGainB.gain.setValueAtTime(0.26, now);
      droneOscB.connect(droneGainB);

      // 3. 5度: sine 165Hz固定、gain = 0.10 * (1 - 0.5 * p) (初期 0.10)
      droneOsc5th = audioCtx.createOscillator();
      droneOsc5th.type = 'sine';
      droneOsc5th.frequency.setValueAtTime(165, now);
      droneGain5th = audioCtx.createGain();
      droneGain5th.gain.setValueAtTime(0.10, now);
      droneOsc5th.connect(droneGain5th);

      // 4. 非調和: sine 300.3Hz固定 (110 * 2.73)、gain = 0.02 + 0.16 * p (初期 0.02)
      droneOscInharm = audioCtx.createOscillator();
      droneOscInharm.type = 'sine';
      droneOscInharm.frequency.setValueAtTime(300.3, now);
      droneGainInharm = audioCtx.createGain();
      droneGainInharm.gain.setValueAtTime(0.02, now);
      droneOscInharm.connect(droneGainInharm);

      // 5. 音色フィルタ: lowpass (カットオフ 1400Hz固定、Q = 0.8 + 2.8 * p、初期 0.8)
      droneFilter = audioCtx.createBiquadFilter();
      droneFilter.type = 'lowpass';
      droneFilter.frequency.setValueAtTime(1400, now);
      droneFilter.Q.setValueAtTime(0.8, now);

      droneGainA.connect(droneFilter);
      droneGainB.connect(droneFilter);
      droneGain5th.connect(droneFilter);
      droneGainInharm.connect(droneFilter);

      // 6. ノイズ: loop再生 → bandpass(周波数 = 700 + 1300*p, Q = 1.4 + 4.0*p)、gain = 0.018 + 0.08*p
      if (noiseBuffer) {
        droneNoiseSource = audioCtx.createBufferSource();
        droneNoiseSource.buffer = noiseBuffer;
        droneNoiseSource.loop = true;

        droneNoiseFilter = audioCtx.createBiquadFilter();
        droneNoiseFilter.type = 'bandpass';
        droneNoiseFilter.frequency.setValueAtTime(700, now);
        droneNoiseFilter.Q.setValueAtTime(1.4, now);

        droneNoiseGain = audioCtx.createGain();
        droneNoiseGain.gain.setValueAtTime(0.018, now);

        droneNoiseSource.connect(droneNoiseFilter);
        droneNoiseFilter.connect(droneNoiseGain);
        droneNoiseGain.connect(droneFilter);

        droneNoiseSource.start(now);
      }

      // 7. AM(振幅変調): LFO sine(1.6 + 9.4*p Hz) → gainノード(depth 0.07 + 0.32*p) → droneAmGain.gain
      droneAmGain = audioCtx.createGain();
      droneAmGain.gain.setValueAtTime(1.0, now);

      droneLfoOsc = audioCtx.createOscillator();
      droneLfoOsc.type = 'sine';
      droneLfoOsc.frequency.setValueAtTime(1.6, now);

      droneLfoGain = audioCtx.createGain();
      droneLfoGain.gain.setValueAtTime(0.07, now);

      droneLfoOsc.connect(droneLfoGain);
      droneLfoGain.connect(droneAmGain.gain);

      droneFilter.connect(droneAmGain);

      // 8. ドローン全体のマスターgain: 0.22固定 (pで大きくしない、立ち上がりクリック防止で15msランプ)
      droneMasterGain = audioCtx.createGain();
      droneMasterGain.gain.setValueAtTime(0.0001, now);
      droneMasterGain.gain.linearRampToValueAtTime(0.22, now + 0.015);

      droneAmGain.connect(droneMasterGain);
      droneMasterGain.connect(masterGain);

      // 発振開始
      droneOscA.start(now);
      droneOscB.start(now);
      droneOsc5th.start(now);
      droneOscInharm.start(now);
      droneLfoOsc.start(now);

      isDroneActive = true;
    } catch (e) {
      cleanupDroneNodes();
    }
  }

  /**
   * ドローンパラメータの更新 (毎フレーム滑らかに追従)
   * ※ droneのfrequencyとローパスのカットオフは絶対にスイープしない
   * @param {number} dangerRatio 現在の危険度比率 (0.0〜)
   */
  function updateDrone(dangerRatio) {
    if (!isDroneActive || !audioCtx) return;
    try {
      const now = audioCtx.currentTime;
      const p = Math.max(0, Math.min(1.0, dangerRatio));

      // Drone B: beat = p <= 0.7 のとき 0.5 + (7.5 * p / 0.7)、p > 0.7 のとき 8 + (p - 0.7) / 0.3 * 10
      const beat = (p <= 0.7)
        ? (0.5 + (7.5 * p / 0.7))
        : (8.0 + ((p - 0.7) / 0.3) * 10.0);
      if (droneOscB) {
        droneOscB.frequency.setTargetAtTime(110.0 + beat, now, 0.035);
      }

      // 5度: gain = 0.10 * (1 - 0.5 * p)
      if (droneGain5th) {
        droneGain5th.gain.setTargetAtTime(0.10 * (1.0 - 0.5 * p), now, 0.035);
      }

      // 非調和: gain = 0.02 + 0.16 * p
      if (droneGainInharm) {
        droneGainInharm.gain.setTargetAtTime(0.02 + 0.16 * p, now, 0.035);
      }

      // ノイズ: bandpass(周波数 = 700 + 1300*p, Q = 1.4 + 4.0*p)、gain = 0.018 + 0.08*p
      if (droneNoiseFilter) {
        droneNoiseFilter.frequency.setTargetAtTime(700.0 + 1300.0 * p, now, 0.035);
        droneNoiseFilter.Q.setTargetAtTime(1.4 + 4.0 * p, now, 0.035);
      }
      if (droneNoiseGain) {
        droneNoiseGain.gain.setTargetAtTime(0.018 + 0.08 * p, now, 0.035);
      }

      // AM: LFO周波数 = 1.6 + 9.4*p、depth = 0.07 + 0.32*p
      if (droneLfoOsc) {
        droneLfoOsc.frequency.setTargetAtTime(1.6 + 9.4 * p, now, 0.035);
      }
      if (droneLfoGain) {
        droneLfoGain.gain.setTargetAtTime(0.07 + 0.32 * p, now, 0.035);
      }

      // 音色フィルタ: カットオフ1400Hz固定、Q = 0.8 + 2.8*p のみpに連動
      if (droneFilter) {
        droneFilter.Q.setTargetAtTime(0.8 + 2.8 * p, now, 0.035);
      }
    } catch (e) {}
  }

  /**
   * 現在のドローンノードのスナップショットを取得
   */
  function snapshotDroneNodes() {
    return {
      droneOscA: droneOscA,
      droneGainA: droneGainA,
      droneOscB: droneOscB,
      droneGainB: droneGainB,
      droneOsc5th: droneOsc5th,
      droneGain5th: droneGain5th,
      droneOscInharm: droneOscInharm,
      droneGainInharm: droneGainInharm,
      droneNoiseSource: droneNoiseSource,
      droneNoiseFilter: droneNoiseFilter,
      droneNoiseGain: droneNoiseGain,
      droneFilter: droneFilter,
      droneAmGain: droneAmGain,
      droneLfoOsc: droneLfoOsc,
      droneLfoGain: droneLfoGain,
      droneMasterGain: droneMasterGain,
    };
  }

  /**
   * ドローンのノード変数をクリア
   */
  function clearDroneReferences() {
    droneOscA = null;
    droneGainA = null;
    droneOscB = null;
    droneGainB = null;
    droneOsc5th = null;
    droneGain5th = null;
    droneOscInharm = null;
    droneGainInharm = null;
    droneNoiseSource = null;
    droneNoiseFilter = null;
    droneNoiseGain = null;
    droneFilter = null;
    droneAmGain = null;
    droneLfoOsc = null;
    droneLfoGain = null;
    droneMasterGain = null;
  }

  /**
   * スナップショットされたドローンノードを安全に停止・切断
   */
  function disposeDroneNodes(nodes) {
    if (!nodes) return;
    try {
      if (nodes.droneOscA) { nodes.droneOscA.stop(); nodes.droneOscA.disconnect(); }
      if (nodes.droneGainA) { nodes.droneGainA.disconnect(); }
      if (nodes.droneOscB) { nodes.droneOscB.stop(); nodes.droneOscB.disconnect(); }
      if (nodes.droneGainB) { nodes.droneGainB.disconnect(); }
      if (nodes.droneOsc5th) { nodes.droneOsc5th.stop(); nodes.droneOsc5th.disconnect(); }
      if (nodes.droneGain5th) { nodes.droneGain5th.disconnect(); }
      if (nodes.droneOscInharm) { nodes.droneOscInharm.stop(); nodes.droneOscInharm.disconnect(); }
      if (nodes.droneGainInharm) { nodes.droneGainInharm.disconnect(); }
      if (nodes.droneNoiseSource) { nodes.droneNoiseSource.stop(); nodes.droneNoiseSource.disconnect(); }
      if (nodes.droneNoiseFilter) { nodes.droneNoiseFilter.disconnect(); }
      if (nodes.droneNoiseGain) { nodes.droneNoiseGain.disconnect(); }
      if (nodes.droneFilter) { nodes.droneFilter.disconnect(); }
      if (nodes.droneLfoOsc) { nodes.droneLfoOsc.stop(); nodes.droneLfoOsc.disconnect(); }
      if (nodes.droneLfoGain) { nodes.droneLfoGain.disconnect(); }
      if (nodes.droneAmGain) { nodes.droneAmGain.disconnect(); }
      if (nodes.droneMasterGain) { nodes.droneMasterGain.disconnect(); }
    } catch (e) {}
  }

  /**
   * ドローンを停止 (リリース時のフェードアウトまたは即時停止)
   * @param {boolean} [immediate=false] 即時停止フラグ
   */
  function stopDrone(immediate) {
    if (!isDroneActive && !droneMasterGain) return;
    isDroneActive = false;

    if (!audioCtx || !droneMasterGain) {
      cleanupDroneNodes();
      return;
    }

    try {
      const now = audioCtx.currentTime;
      const nodes = snapshotDroneNodes();
      clearDroneReferences();

      if (immediate) {
        disposeDroneNodes(nodes);
      } else {
        // リリース時は約50msで滑らかにフェードアウト
        if (nodes.droneMasterGain) {
          nodes.droneMasterGain.gain.cancelScheduledValues(now);
          nodes.droneMasterGain.gain.setValueAtTime(nodes.droneMasterGain.gain.value, now);
          nodes.droneMasterGain.gain.linearRampToValueAtTime(0.0001, now + 0.05);
        }

        setTimeout(function () {
          disposeDroneNodes(nodes);
        }, 70);
      }
    } catch (e) {
      cleanupDroneNodes();
    }
  }

  function cleanupDroneNodes() {
    isDroneActive = false;
    const nodes = snapshotDroneNodes();
    clearDroneReferences();
    disposeDroneNodes(nodes);
  }

  /**
   * 警告パルスの更新 (毎フレーム呼び出し)
   * dangerRatio が 0.30 未満では鳴らさない。
   * 0.30 → 1.0 に上がるにつれ、間隔 600ms → 80ms、音程 330Hz → 660Hz。
   * @param {number} dangerRatio 現在の危険度比率 (0.0〜)
   * @param {number} dt 前フレームからの経過時間 (秒)
   */
  function updateWarningPulse(dangerRatio, dt) {
    // ドローンのパラメータも毎フレーム滑らかに更新
    updateDrone(dangerRatio);

    if (dangerRatio < 0.30) {
      pulseTimer = 0;
      return;
    }

    // 0.30 → 1.0 を 0.0 → 1.0 に正規化 (1.0以上もクランプ)
    const norm = Math.max(0, Math.min(1.0, (dangerRatio - 0.30) / (1.0 - 0.30)));

    // 間隔: 600ms (0.60s) → 80ms (0.08s)
    const interval = 0.60 - norm * (0.60 - 0.08);

    // 音程: 330Hz → 660Hz
    const freq = 330 + norm * (660 - 330);

    pulseTimer += dt;
    if (pulseTimer >= interval) {
      playPulseSound(freq);
      pulseTimer = 0;
    }
  }

  /**
   * ビッグバン音 (全体約1.8秒の3層構成)
   * 1. アタック層: ノイズ急降下 (5000Hz -> 200Hz, 0.12s, gain 0.20 -> 0.001)
   * 2. ボディ層: サイン下降 (110Hz -> 28Hz, 0.9s, gain 0.95 ディケイ 1.4s)
   * 3. テイル層: ノイズ余韻 (0.05s遅れ, 800Hz -> 60Hz, 1.8s, gain 0.55 -> 0.001)
   */
  function playBigBang() {
    stopDrone(true);
    pulseTimer = 0;
    if (isMutedState || !audioCtx) return;

    try {
      const now = audioCtx.currentTime;

      // 1. アタック層 (立ち上がりの縁取り: ノイズ + 急速下降ローパス、音量を抑えて「パン」感を軽減)
      if (noiseBuffer) {
        const attackSource = audioCtx.createBufferSource();
        attackSource.buffer = noiseBuffer;
        attackSource.loop = true;

        const attackFilter = audioCtx.createBiquadFilter();
        attackFilter.type = 'lowpass';
        attackFilter.frequency.setValueAtTime(5000, now);
        attackFilter.frequency.exponentialRampToValueAtTime(200, now + 0.12);

        const attackGain = audioCtx.createGain();
        attackGain.gain.setValueAtTime(0.20, now);
        attackGain.gain.exponentialRampToValueAtTime(0.001, now + 0.12);

        attackSource.connect(attackFilter);
        attackFilter.connect(attackGain);
        attackGain.connect(masterGain);

        attackSource.start(now);
        attackSource.stop(now + 0.14);
      }

      // 2. ボディ層 (主役の「ドゥーン」: sineオシレーター 110Hz -> 28Hz, ディケイ 1.4秒)
      const bodyOsc = audioCtx.createOscillator();
      const bodyGain = audioCtx.createGain();

      bodyOsc.type = 'sine';
      bodyOsc.frequency.setValueAtTime(110, now);
      bodyOsc.frequency.exponentialRampToValueAtTime(28, now + 0.9);

      bodyGain.gain.setValueAtTime(0.0001, now);
      bodyGain.gain.linearRampToValueAtTime(0.95, now + 0.005);
      bodyGain.gain.exponentialRampToValueAtTime(0.0001, now + 1.4);

      bodyOsc.connect(bodyGain);
      bodyGain.connect(masterGain);

      bodyOsc.start(now);
      bodyOsc.stop(now + 1.45);

      // 3. テイル層 (余韻の重低音: 0.05秒遅れで開始、ノイズ 800Hz -> 60Hz, 1.8秒)
      if (noiseBuffer) {
        const tailStartTime = now + 0.05;
        const tailDuration = 1.8;

        const tailSource = audioCtx.createBufferSource();
        tailSource.buffer = noiseBuffer;
        tailSource.loop = true;

        const tailFilter = audioCtx.createBiquadFilter();
        tailFilter.type = 'lowpass';
        tailFilter.frequency.setValueAtTime(800, tailStartTime);
        tailFilter.frequency.exponentialRampToValueAtTime(60, tailStartTime + tailDuration);

        const tailGain = audioCtx.createGain();
        tailGain.gain.setValueAtTime(0.0001, now);
        tailGain.gain.setValueAtTime(0.55, tailStartTime);
        tailGain.gain.exponentialRampToValueAtTime(0.001, tailStartTime + tailDuration);

        tailSource.connect(tailFilter);
        tailFilter.connect(tailGain);
        tailGain.connect(masterGain);

        tailSource.start(tailStartTime);
        tailSource.stop(tailStartTime + tailDuration + 0.05);
      }
    } catch (e) {}
  }

  /**
   * リリース確定音 (全体0.60秒、すべて指数減衰 setTargetAtTime)
   * 失敗音と同じ素材で「110が残り、165が残る」という対比を構築
   * 層1 アタック: ノイズ → highpass 600Hz(Q 0.7)、gain 0.16 → 0、tau 0.040s
   * 層2 ボディ:
   *   - sine 110Hz: gain 0.42、tau 0.20s
   *   - sine 165Hz: 開始0.015秒後にgain 0→0.30へ0.05秒でランプ、その後 tau 0.28s (確定の核)
   *   - sine 55Hz: gain 0.10、tau 0.12s
   *   - sine 220Hz: 開始0.03秒後から、gain 0.06、tau 0.18s
   *   - ボディ全体にローパス 1600Hz固定、Q 0.9(スイープしない)
   * 層3 余韻: ノイズ → bandpass 500Hz(Q 3.5)、gain 0.045、tau 0.32s
   * ※ ピッチのグリッサンドは一切しない
   */
  function playResolve() {
    stopDrone();
    pulseTimer = 0;
    if (isMutedState || !audioCtx) return;

    try {
      if (!noiseBuffer) {
        createNoiseBuffer();
      }

      const now = audioCtx.currentTime;
      const duration = 0.60;
      const stopTime = now + duration + 0.05;

      // === 層1: アタック (ノイズ → highpass 600Hz, Q 0.7, gain 0.16 → 0, tau 0.040s) ===
      if (noiseBuffer) {
        const attackSource = audioCtx.createBufferSource();
        attackSource.buffer = noiseBuffer;
        attackSource.loop = true;

        const attackFilter = audioCtx.createBiquadFilter();
        attackFilter.type = 'highpass';
        attackFilter.frequency.setValueAtTime(600, now);
        attackFilter.Q.setValueAtTime(0.7, now);

        const attackGain = audioCtx.createGain();
        attackGain.gain.setValueAtTime(0.16, now);
        attackGain.gain.setTargetAtTime(0, now, 0.040);

        attackSource.connect(attackFilter);
        attackFilter.connect(attackGain);
        attackGain.connect(masterGain);

        attackSource.start(now);
        attackSource.stop(stopTime);
      }

      // === 層2: ボディ (ローパス 1600Hz固定, Q 0.9) ===
      const bodyFilter = audioCtx.createBiquadFilter();
      bodyFilter.type = 'lowpass';
      bodyFilter.frequency.setValueAtTime(1600, now);
      bodyFilter.Q.setValueAtTime(0.9, now);
      bodyFilter.connect(masterGain);

      // 2-1. sine 110Hz: gain 0.42, tau 0.20s
      const osc110 = audioCtx.createOscillator();
      const gain110 = audioCtx.createGain();
      osc110.type = 'sine';
      osc110.frequency.setValueAtTime(110, now);
      gain110.gain.setValueAtTime(0.0001, now);
      gain110.gain.linearRampToValueAtTime(0.42, now + 0.003);
      gain110.gain.setTargetAtTime(0, now + 0.003, 0.20);
      osc110.connect(gain110);
      gain110.connect(bodyFilter);
      osc110.start(now);
      osc110.stop(stopTime);

      // 2-2. sine 165Hz: 開始0.015秒後にgain 0→0.30へ0.05秒でランプ、その後 tau 0.28s (確定の核)
      const osc165 = audioCtx.createOscillator();
      const gain165 = audioCtx.createGain();
      osc165.type = 'sine';
      osc165.frequency.setValueAtTime(165, now);
      gain165.gain.setValueAtTime(0.0001, now);
      gain165.gain.setValueAtTime(0.0001, now + 0.015);
      gain165.gain.linearRampToValueAtTime(0.30, now + 0.065);
      gain165.gain.setTargetAtTime(0, now + 0.065, 0.28);
      osc165.connect(gain165);
      gain165.connect(bodyFilter);
      osc165.start(now);
      osc165.stop(stopTime);

      // 2-3. sine 55Hz: gain 0.10, tau 0.12s
      const osc55 = audioCtx.createOscillator();
      const gain55 = audioCtx.createGain();
      osc55.type = 'sine';
      osc55.frequency.setValueAtTime(55, now);
      gain55.gain.setValueAtTime(0.0001, now);
      gain55.gain.linearRampToValueAtTime(0.10, now + 0.003);
      gain55.gain.setTargetAtTime(0, now + 0.003, 0.12);
      osc55.connect(gain55);
      gain55.connect(bodyFilter);
      osc55.start(now);
      osc55.stop(stopTime);

      // 2-4. sine 220Hz: 開始0.03秒後から、gain 0.06, tau 0.18s
      const osc220 = audioCtx.createOscillator();
      const gain220 = audioCtx.createGain();
      osc220.type = 'sine';
      osc220.frequency.setValueAtTime(220, now);
      gain220.gain.setValueAtTime(0.0001, now);
      gain220.gain.setValueAtTime(0.0001, now + 0.03);
      gain220.gain.linearRampToValueAtTime(0.06, now + 0.033);
      gain220.gain.setTargetAtTime(0, now + 0.033, 0.18);
      osc220.connect(gain220);
      gain220.connect(bodyFilter);
      osc220.start(now);
      osc220.stop(stopTime);

      // === 層3: 余韻 (ノイズ → bandpass 500Hz, Q 3.5, gain 0.045, tau 0.32s) ===
      if (noiseBuffer) {
        const tailSource = audioCtx.createBufferSource();
        tailSource.buffer = noiseBuffer;
        tailSource.loop = true;

        const tailFilter = audioCtx.createBiquadFilter();
        tailFilter.type = 'bandpass';
        tailFilter.frequency.setValueAtTime(500, now);
        tailFilter.Q.setValueAtTime(3.5, now);

        const tailGain = audioCtx.createGain();
        tailGain.gain.setValueAtTime(0.0001, now);
        tailGain.gain.linearRampToValueAtTime(0.045, now + 0.003);
        tailGain.gain.setTargetAtTime(0, now + 0.003, 0.32);

        tailSource.connect(tailFilter);
        tailFilter.connect(tailGain);
        tailGain.connect(masterGain);

        tailSource.start(now);
        tailSource.stop(stopTime);
      }
    } catch (e) {}
  }

  // グローバルエクスポート
  window.AudioController = {
    init: initAudio,
    resume: initAudio,
    updateWarning: updateWarningPulse,
    playBigBang: playBigBang,
    playResolve: playResolve,
    toggleMute: toggleMute,
    isMuted: isMuted,
    startDrone: startDrone,
    stopDrone: stopDrone,
    updateDrone: updateDrone,
  };

})(window);
