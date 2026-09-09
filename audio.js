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

        // ホワイトノイズ用バッファの事前生成 (1秒分)
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
   * ホワイトノイズバッファの生成
   */
  function createNoiseBuffer() {
    if (!audioCtx) return;
    try {
      const sampleRate = audioCtx.sampleRate || 44100;
      const bufferSize = sampleRate * 1.0; // 1秒分
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
   * 警告パルスの更新 (毎フレーム呼び出し)
   * dangerRatio が 0.4 未満では鳴らさない。
   * 0.4 → 1.0 に上がるにつれ、間隔 600ms → 80ms、音程 400Hz → 1200Hz。
   * @param {number} dangerRatio 現在の危険度比率 (0.0〜)
   * @param {number} dt 前フレームからの経過時間 (秒)
   */
  function updateWarningPulse(dangerRatio, dt) {
    if (dangerRatio < 0.4) {
      pulseTimer = 0;
      return;
    }

    // 0.4 → 1.0 を 0.0 → 1.0 に正規化 (1.0以上もクランプ)
    const norm = Math.max(0, Math.min(1.0, (dangerRatio - 0.4) / (1.0 - 0.4)));

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
   * ビッグバン音
   * ホワイトノイズのバースト(ローパス下降) + 60Hz前後の低音サイン (300〜800ms)
   */
  function playBigBang() {
    pulseTimer = 0;
    if (isMutedState || !audioCtx) return;

    try {
      const now = audioCtx.currentTime;
      const duration = 0.65; // 約650ms

      // 1. 低音サイン波 (Sub-bass: 70Hz -> 32Hz)
      const subOsc = audioCtx.createOscillator();
      const subGain = audioCtx.createGain();

      subOsc.type = 'sine';
      subOsc.frequency.setValueAtTime(70, now);
      subOsc.frequency.exponentialRampToValueAtTime(32, now + duration);

      subGain.gain.setValueAtTime(0.0001, now);
      subGain.gain.linearRampToValueAtTime(0.55, now + 0.025);
      subGain.gain.exponentialRampToValueAtTime(0.0001, now + duration);

      subOsc.connect(subGain);
      subGain.connect(masterGain);

      subOsc.start(now);
      subOsc.stop(now + duration + 0.02);

      // 2. ホワイトノイズバースト (ローパスフィルター下降)
      if (noiseBuffer) {
        const noiseSource = audioCtx.createBufferSource();
        noiseSource.buffer = noiseBuffer;

        const filter = audioCtx.createBiquadFilter();
        filter.type = 'lowpass';
        filter.frequency.setValueAtTime(2600, now);
        filter.frequency.exponentialRampToValueAtTime(50, now + duration);

        const noiseGain = audioCtx.createGain();
        noiseGain.gain.setValueAtTime(0.0001, now);
        noiseGain.gain.linearRampToValueAtTime(0.5, now + 0.015);
        noiseGain.gain.exponentialRampToValueAtTime(0.0001, now + duration);

        noiseSource.connect(filter);
        filter.connect(noiseGain);
        noiseGain.connect(masterGain);

        noiseSource.start(now);
        noiseSource.stop(now + duration + 0.02);
      }
    } catch (e) {}
  }

  /**
   * リリース確定音
   * 短く軽い上昇音
   */
  function playResolve() {
    pulseTimer = 0;
    if (isMutedState || !audioCtx) return;

    try {
      const now = audioCtx.currentTime;
      const duration = 0.14; // 140ms

      const osc = audioCtx.createOscillator();
      const gain = audioCtx.createGain();

      // 明るく軽快なサイン波で音程を上昇 (520Hz -> 1040Hz: 1オクターブ上昇)
      osc.type = 'sine';
      osc.frequency.setValueAtTime(520, now);
      osc.frequency.exponentialRampToValueAtTime(1040, now + duration * 0.85);

      gain.gain.setValueAtTime(0.0001, now);
      gain.gain.linearRampToValueAtTime(0.22, now + 0.015);
      gain.gain.exponentialRampToValueAtTime(0.0001, now + duration);

      osc.connect(gain);
      gain.connect(masterGain);

      osc.start(now);
      osc.stop(now + duration + 0.01);
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
  };

})(window);
