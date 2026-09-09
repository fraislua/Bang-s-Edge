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
  let droneOsc1 = null;         // triangle (基音 130-300Hz)
  let droneOsc2 = null;         // sine (完全5度上 195-450Hz)
  let droneHarmonicGain = null; // 倍音gain (基音の40%)
  let droneFilter = null;       // lowpass (900-5000Hz)
  let droneGain = null;         // gain (0.04-0.18)
  let isDroneActive = false;

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
   * 警告パルス (20〜40ms の短いビープ) の再生
   * @param {number} freq 周波数 (Hz)
   * @param {number} duration 長さ (s)
   */
  function playPulseSound(freq, duration) {
    if (isMutedState || !audioCtx) return;
    try {
      const now = audioCtx.currentTime;
      const osc = audioCtx.createOscillator();
      const gain = audioCtx.createGain();

      osc.type = 'sine';
      osc.frequency.setValueAtTime(freq, now);

      // クリックノイズ防止のためのエンベロープ
      const attack = 0.004;
      gain.gain.setValueAtTime(0.0001, now);
      gain.gain.linearRampToValueAtTime(0.16, now + attack);
      gain.gain.exponentialRampToValueAtTime(0.0001, now + duration);

      osc.connect(gain);
      gain.connect(masterGain);

      osc.start(now);
      osc.stop(now + duration + 0.01);
    } catch (e) {}
  }

  /**
   * 押下中の持続音(ドローン)を開始
   * オシレーター2本 (基音triangle + 完全5度上sine) をローパス経由で masterGain へ
   * 倍音は基音の40%程度に抑える
   * 押した瞬間から鳴るよう初期ゲイン0.04へ素早く立ち上げる
   */
  function startDrone() {
    if (!initAudio()) return;
    if (isDroneActive) {
      stopDrone(true);
    }

    try {
      const now = audioCtx.currentTime;

      // 1. 基音: triangle (130Hz)
      droneOsc1 = audioCtx.createOscillator();
      droneOsc1.type = 'triangle';
      droneOsc1.frequency.setValueAtTime(130, now);

      // 2. 倍音: sine (完全5度上: 130 * 1.5 = 195Hz)
      droneOsc2 = audioCtx.createOscillator();
      droneOsc2.type = 'sine';
      droneOsc2.frequency.setValueAtTime(195, now);

      // 倍音の音量バランス (基音の40%程度)
      droneHarmonicGain = audioCtx.createGain();
      droneHarmonicGain.gain.setValueAtTime(0.4, now);

      // 3. ローパスフィルター (初期 900Hz)
      droneFilter = audioCtx.createBiquadFilter();
      droneFilter.type = 'lowpass';
      droneFilter.frequency.setValueAtTime(900, now);

      // 4. ドローンゲイン (初期 0.04)
      droneGain = audioCtx.createGain();
      droneGain.gain.setValueAtTime(0.0001, now);
      droneGain.gain.linearRampToValueAtTime(0.04, now + 0.015);

      droneOsc1.connect(droneFilter);
      droneOsc2.connect(droneHarmonicGain);
      droneHarmonicGain.connect(droneFilter);
      droneFilter.connect(droneGain);
      droneGain.connect(masterGain);

      droneOsc1.start(now);
      droneOsc2.start(now);
      isDroneActive = true;
    } catch (e) {
      cleanupDroneNodes();
    }
  }

  /**
   * ドローンパラメータの更新 (毎フレーム滑らかに追従)
   * @param {number} dangerRatio 現在の危険度比率 (0.0〜)
   */
  function updateDrone(dangerRatio) {
    if (!isDroneActive || !audioCtx || !droneGain || !droneFilter || !droneOsc1 || !droneOsc2) return;
    try {
      const now = audioCtx.currentTime;
      const ratio = Math.max(0, Math.min(1.0, dangerRatio));

      // 基音の周波数: 130Hz -> 300Hz
      const baseFreq = 130 + ratio * (300 - 130);
      // 倍音(完全5度上: 1.5倍)の周波数: 195Hz -> 450Hz
      const harmonicFreq = baseFreq * 1.5;
      // ローパスのカットオフ: 900Hz -> 5000Hz
      const cutoff = 900 + ratio * (5000 - 900);
      // 音量: 0.04 -> 0.18
      const volume = 0.04 + ratio * (0.18 - 0.04);

      // setTargetAtTime で滑らかに追従 (クリックノイズを防止)
      droneOsc1.frequency.setTargetAtTime(baseFreq, now, 0.035);
      droneOsc2.frequency.setTargetAtTime(harmonicFreq, now, 0.035);
      droneFilter.frequency.setTargetAtTime(cutoff, now, 0.035);
      droneGain.gain.setTargetAtTime(volume, now, 0.035);
    } catch (e) {}
  }

  /**
   * ドローンを停止 (リリース時のフェードアウトまたは即時停止)
   * @param {boolean} [immediate=false] 即時停止フラグ
   */
  function stopDrone(immediate) {
    if (!isDroneActive && !droneGain) return;
    isDroneActive = false;

    if (!audioCtx || !droneGain) {
      cleanupDroneNodes();
      return;
    }

    try {
      const now = audioCtx.currentTime;
      const osc1 = droneOsc1;
      const osc2 = droneOsc2;
      const harmonicGain = droneHarmonicGain;
      const gain = droneGain;
      const filter = droneFilter;

      droneOsc1 = null;
      droneOsc2 = null;
      droneHarmonicGain = null;
      droneGain = null;
      droneFilter = null;

      if (immediate) {
        if (osc1) { osc1.stop(); osc1.disconnect(); }
        if (osc2) { osc2.stop(); osc2.disconnect(); }
        if (harmonicGain) harmonicGain.disconnect();
        if (filter) filter.disconnect();
        if (gain) gain.disconnect();
      } else {
        // リリース時は約50msで滑らかにフェードアウト
        gain.gain.cancelScheduledValues(now);
        gain.gain.setValueAtTime(gain.gain.value, now);
        gain.gain.linearRampToValueAtTime(0.0001, now + 0.05);

        setTimeout(function () {
          try {
            if (osc1) { osc1.stop(); osc1.disconnect(); }
            if (osc2) { osc2.stop(); osc2.disconnect(); }
            if (harmonicGain) harmonicGain.disconnect();
            if (filter) filter.disconnect();
            if (gain) gain.disconnect();
          } catch (err) {}
        }, 70);
      }
    } catch (e) {
      cleanupDroneNodes();
    }
  }

  function cleanupDroneNodes() {
    try {
      if (droneOsc1) { droneOsc1.stop(); droneOsc1.disconnect(); }
      if (droneOsc2) { droneOsc2.stop(); droneOsc2.disconnect(); }
      if (droneHarmonicGain) droneHarmonicGain.disconnect();
      if (droneFilter) droneFilter.disconnect();
      if (droneGain) droneGain.disconnect();
    } catch (e) {}
    droneOsc1 = null;
    droneOsc2 = null;
    droneHarmonicGain = null;
    droneFilter = null;
    droneGain = null;
    isDroneActive = false;
  }

  /**
   * 警告パルスの更新 (毎フレーム呼び出し)
   * dangerRatio が 0.30 未満では鳴らさない。
   * 0.30 → 1.0 に上がるにつれ、間隔 600ms → 80ms、音程 400Hz → 1200Hz。
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

    // 音程: 400Hz → 1200Hz
    const freq = 400 + norm * (1200 - 400);

    // ビープ長: 約 25ms 〜 35ms
    const duration = 0.025 + (1.0 - norm) * 0.010;

    pulseTimer += dt;
    if (pulseTimer >= interval) {
      playPulseSound(freq, duration);
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
   * リリース確定音
   * triangle波による完全5度の和音 (440Hz + 660Hz)
   * 長さ0.35秒、アタック15ms(やわらかく)、その後ゆるやかにディケイ
   */
  function playResolve() {
    stopDrone();
    pulseTimer = 0;
    if (isMutedState || !audioCtx) return;

    try {
      const now = audioCtx.currentTime;
      const duration = 0.35;
      const attack = 0.015;

      const freqs = [440, 660];
      for (let i = 0; i < freqs.length; i++) {
        const freq = freqs[i];
        const osc = audioCtx.createOscillator();
        const gain = audioCtx.createGain();

        osc.type = 'triangle';
        osc.frequency.setValueAtTime(freq, now);

        gain.gain.setValueAtTime(0.0001, now);
        gain.gain.linearRampToValueAtTime(0.18, now + attack);
        gain.gain.exponentialRampToValueAtTime(0.0001, now + duration);

        osc.connect(gain);
        gain.connect(masterGain);

        osc.start(now);
        osc.stop(now + duration + 0.02);
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
