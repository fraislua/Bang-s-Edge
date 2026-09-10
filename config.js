/**
 * Bang's-Edge - Game Configuration
 * docs/game-concept.md の「パラメータ初期値」に基づく定数定義
 */

const CONFIG = {
  // 粒子設定
  TOTAL_PARTICLES: 200,      // 粒子総数
  MARGIN: 20,                // 初期配置の画面端マージン (px)

  // 測定・集積半径
  R_MEASURE: 40,             // 密度測定円の半径(固定) (px)
  R_MIN: 30,                 // 集積半径の最小値 (px)
  R_MAX_RATIO: 0.55,         // 集積半径の最大比率 (画面対角線に対する比率)
  TAU_R: 3.0,                // 集積半径の時定数 (s)

  // 引力
  F_MIN: 50,                 // 引力の最小値 (px/s²)
  F_MAX: 800,                // 引力の最大値 (px/s²)
  TAU_F: 2.5,                // 引力の時定数 (s) ※TAU_F < TAU_R

  // 運動・減衰
  DAMPING: 0.975,            // 速度減衰 (毎フレーム)

  // 物理拡張: 粒子間反発・引力上昇・揺動・クリップ
  F_CREEP: 80,              // 引力の際限ない上昇 (px/s^3)。F(t) に + F_CREEP * t として加算。30では1ラウンド22秒かかり学習が遅すぎた
  REPULSION_K: 4000,        // 粒子間反発の係数 (px/s^2)
  REPULSION_D0: 8,          // 粒子間反発の相互作用半径 (px)
  REPULSION_EPS: 0.05,      // 反発の方向が定義できない最小距離 (px)
  ACCEL_CLIP: 8000,         // 1粒子あたりの合成加速度の上限 (px/s^2)
  VELOCITY_CLIP: 720,       // 速度の上限 (px/s)
  WOBBLE_AX: 12,            // 引力中心の揺動振幅 X (px)
  WOBBLE_AY: 9,             // 引力中心の揺動振幅 Y (px)
  WOBBLE_F1: 0.73,          // 揺動周波数 X (Hz)
  WOBBLE_F2: 0.97,          // 揺動周波数 Y (Hz)

  // 初期配置
  SPAWN_CLUSTERS: 5,        // 初期配置のクラスタ数
  SPAWN_SIGMA: 55,          // 各クラスタの標準偏差 (px)
  SPAWN_UNIFORM_FRAC: 0.35, // 一様に撒く粒子の割合

  // ビッグバン判定
  BANG_GRACE_FRAMES: 5,      // 猶予フレーム数 (約83ms)

  // 危険度ゾーン境界 (dangerRatio)
  DANGER_WARM: 0.40,
  DANGER_HOT: 0.70,
  DANGER_CRITICAL: 0.90,
};

// 派生定数の算出
// 測定円の面積: π * R_MEASURE²
CONFIG.MEASURE_AREA = Math.PI * CONFIG.R_MEASURE * CONFIG.R_MEASURE;
// 理論最大密度 (全粒子が測定円内に収まった場合)
CONFIG.DENSITY_MAX = CONFIG.TOTAL_PARTICLES / CONFIG.MEASURE_AREA;
// ビッグバン発生しきい値 (DENSITY_MAX × 0.85)
CONFIG.BANG_THRESHOLD = CONFIG.DENSITY_MAX * 0.85;

// 個別参照の利便性のためのエクスポート（グローバルスコープ）
window.CONFIG = Object.freeze(CONFIG);
