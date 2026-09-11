using System;

namespace BangsEdge.Simulation
{
    /// <summary>
    /// Bang's-Edge - Game Configuration
    /// docs/game-concept.md の「パラメータ初期値」および config.js / script.js に基づく定数定義
    /// </summary>
    public static class GameConfig
    {
        // 論理盤面 (ウィンドウサイズによらずゲームはこの座標系で動く)
        public const int LOGICAL_WIDTH = 1920;
        public const int LOGICAL_HEIGHT = 1080;

        // 粒子設定
        public const int TOTAL_PARTICLES = 200;      // 粒子総数
        public const int MARGIN = 20;                // 初期配置の画面端マージン (px)

        // 測定・集積半径
        public const int R_MEASURE = 40;             // 密度測定円の半径(固定) (px)
        public const int R_MIN = 30;                 // 集積半径の最小値 (px)
        public const double R_MAX_RATIO = 0.65;      // 集積半径の最大比率 (画面対角線に対する比率)
        public const double TAU_R = 3.0;             // 集積半径の時定数 (s)

        // 引力
        public const int F_MIN = 50;                 // 引力の最小値 (px/s²)
        public const int F_MAX = 800;                // 引力の最大値 (px/s²)
        public const double TAU_F = 2.5;             // 引力の時定数 (s) ※TAU_F < TAU_R

        // 運動・減衰
        public const double DAMPING = 0.975;         // 速度減衰 (毎フレーム)

        // 物理拡張: 粒子間反発・引力上昇・揺動・クリップ
        public const int F_CREEP = 80;               // 引力の際限ない上昇 (px/s^3)
        public const int REPULSION_K = 4000;         // 粒子間反発の係数 (px/s^2)
        public const int REPULSION_D0 = 8;           // 粒子間反発の相互作用半径 (px)
        public const double REPULSION_EPS = 0.05;    // 反発の方向が定義できない最小距離 (px)
        public const int ACCEL_CLIP = 8000;          // 1粒子あたりの合成加速度の上限 (px/s^2)
        public const int VELOCITY_CLIP = 720;        // 速度の上限 (px/s)
        public const int WOBBLE_AX = 12;             // 引力中心の揺動振幅 X (px)
        public const int WOBBLE_AY = 9;              // 引力中心の揺動振幅 Y (px)
        public const double WOBBLE_F1 = 0.73;        // 揺動周波数 X (Hz)
        public const double WOBBLE_F2 = 0.97;        // 揺動周波数 Y (Hz)

        // 初期配置
        public const int SPAWN_CLUSTERS = 5;         // 初期配置のクラスタ数
        public const int SPAWN_SIGMA = 55;           // 各クラスタの標準偏差 (px)
        public const double SPAWN_UNIFORM_FRAC = 0.35; // 一様に撒く粒子の割合

        // ビッグバン判定
        public const int BANG_GRACE_FRAMES = 5;      // 猶予フレーム数 (約83ms)

        // 危険度ゾーン境界 (dangerRatio)
        public const double DANGER_WARM = 0.40;
        public const double DANGER_HOT = 0.70;
        public const double DANGER_CRITICAL = 0.90;

        // 派生定数の算出 (JSと同じ式・同じ演算順序で算出)
        // 測定円の面積: π * R_MEASURE²
        public static readonly double MEASURE_AREA = Math.PI * R_MEASURE * R_MEASURE;
        // 理論最大密度 (全粒子が測定円内に収まった場合)
        public static readonly double DENSITY_MAX = TOTAL_PARTICLES / MEASURE_AREA;
        // ビッグバン発生しきい値 (DENSITY_MAX × 0.85)
        public static readonly double BANG_THRESHOLD = DENSITY_MAX * 0.85;

        // script.js 側の定数
        public const int UNREACHABLE_WARN_FRAMES = 45;            // 警告を出すまでの猶予 (約0.75秒)
        public static readonly int REQUIRED_PARTICLES = (int)Math.Ceiling(BANG_THRESHOLD * MEASURE_AREA);  // 170
        public const int JITTER_FORCE = 35;                       // 引力圏外粒子の微弱ランダム揺動の加速度 (px/s²)
        public const double FIXED_DT = 1.0 / 60.0;                // 物理固定ステップ (秒)
        public const int MAX_SUBSTEPS = 5;                        // 1フレームで進める上限
    }
}
