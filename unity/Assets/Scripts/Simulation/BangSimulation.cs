using System;

namespace BangsEdge.Simulation
{
    /// <summary>
    /// ゲームの主状態。JS版の 'READY' / 'ATTRACTING' / 'RESOLVED' / 'BANG' と対応。
    /// </summary>
    public enum GameState
    {
        Ready,
        Attracting,
        Resolved,
        Bang
    }

    /// <summary>
    /// 危険度ゾーン。JS版の 'SAFE' / 'WARM' / 'HOT' / 'CRITICAL' と対応。
    /// </summary>
    public enum DangerStage
    {
        Safe,
        Warm,
        Hot,
        Critical
    }

    /// <summary>
    /// 音響イベント通知インターフェース。JS版の AudioController のメソッドと対応。
    /// </summary>
    public interface IAudioEvents
    {
        void StartDrone();
        void StopDrone();
        void UpdateWarning(double dangerRatio, double dt);
        void PlayResolve();
        void PlayBigBang();
    }

    /// <summary>
    /// Bang's-Edge の純粋C#シミュレーション核。
    /// UnityEngine に依存せず、JS版 (script.js) の物理・ゲーム状態を完全な決定論性をもって再現する。
    /// </summary>
    public sealed class BangSimulation
    {
        private readonly IRandomSource _random;
        private readonly IAudioEvents _audio;

        // 粒子データ配列 (長さ TOTAL_PARTICLES)
        public readonly double[] X;
        public readonly double[] Y;
        public readonly double[] VX;
        public readonly double[] VY;
        public readonly bool[] InMeasure;
        public readonly bool[] OutOfReach;

        // 計算用内部バッファ (Step 内の GC Alloc をゼロにするために事前確保)
        private readonly double[] _repAx;
        private readonly double[] _repAy;
        private readonly double[] _clusterX;
        private readonly double[] _clusterY;

        // 論理盤面と射程 (JSの変数と1対1)
        public double Width { get; }
        public double Height { get; }
        public double EffectiveRMax { get; }

        // カーソル座標 (外から設定可能。初期値は論理盤面の中央)
        public double CursorX { get; set; }
        public double CursorY { get; set; }

        // ラウンド・状態
        public GameState State { get; private set; }
        public bool IsPressing { get; private set; }
        public double PressStartTime { get; private set; }

        // 密度・危険度・スコア
        public double CurrentDensity { get; private set; }
        public double CurrentDangerRatio { get; private set; }
        public int CurrentScore { get; private set; }
        public int FinalScore { get; private set; }
        public int HighScore { get; set; }

        // 猶予カウンター・危険度段階
        public int GraceCounter { get; private set; }
        public DangerStage Stage { get; private set; }

        // 演出値
        public double ShakeMagnitude { get; private set; }
        public double FlashOpacity { get; private set; }

        // 射程判定状態
        public int ReachableCount { get; private set; }
        public bool BangPossible { get; private set; }
        public int UnreachableFrames { get; private set; }

        public BangSimulation(IRandomSource random, IAudioEvents audio = null)
        {
            _random = random ?? throw new ArgumentNullException(nameof(random));
            _audio = audio;

            X = new double[GameConfig.TOTAL_PARTICLES];
            Y = new double[GameConfig.TOTAL_PARTICLES];
            VX = new double[GameConfig.TOTAL_PARTICLES];
            VY = new double[GameConfig.TOTAL_PARTICLES];
            InMeasure = new bool[GameConfig.TOTAL_PARTICLES];
            OutOfReach = new bool[GameConfig.TOTAL_PARTICLES];

            _repAx = new double[GameConfig.TOTAL_PARTICLES];
            _repAy = new double[GameConfig.TOTAL_PARTICLES];
            _clusterX = new double[GameConfig.SPAWN_CLUSTERS];
            _clusterY = new double[GameConfig.SPAWN_CLUSTERS];

            Width = GameConfig.LOGICAL_WIDTH;
            Height = GameConfig.LOGICAL_HEIGHT;
            EffectiveRMax = Math.Sqrt(Width * Width + Height * Height) * GameConfig.R_MAX_RATIO;

            CursorX = Width / 2.0;
            CursorY = Height / 2.0;

            State = GameState.Ready;
            Stage = DangerStage.Safe;
            ReachableCount = GameConfig.TOTAL_PARTICLES;
            BangPossible = true;

            // コンストラクタの最後で InitParticles() を1回呼ぶ (script.js の初期読み込み時と同じ乱数消費)
            InitParticles();
        }

        public void InitParticles()
        {
            double margin = GameConfig.MARGIN;
            double spawnW = Math.Max(Width - 2.0 * margin, 20.0);
            double spawnH = Math.Max(Height - 2.0 * margin, 20.0);
            double minX = margin;
            double maxX = Math.Max(margin, Width - margin);
            double minY = margin;
            double maxY = Math.Max(margin, Height - margin);

            int total = GameConfig.TOTAL_PARTICLES;
            int uniformCount = (int)Math.Floor(total * GameConfig.SPAWN_UNIFORM_FRAC + 0.5);
            int clusterTotal = total - uniformCount;

            // 1. 一様ランダム配置 (粒子ごとに x → y)
            for (int i = 0; i < uniformCount; i++)
            {
                X[i] = margin + _random.NextDouble() * spawnW;
                Y[i] = margin + _random.NextDouble() * spawnH;
                VX[i] = 0.0;
                VY[i] = 0.0;
                InMeasure[i] = false;
                OutOfReach[i] = false;
            }

            // 2. クラスタ中心の生成 (クラスタごとに x → y)
            for (int c = 0; c < GameConfig.SPAWN_CLUSTERS; c++)
            {
                _clusterX[c] = margin + _random.NextDouble() * spawnW;
                _clusterY[c] = margin + _random.NextDouble() * spawnH;
            }

            // 3. クラスタ配置 (残りの粒子を各クラスタに均等に振り分け、2次元正規分布)
            for (int i = 0; i < clusterTotal; i++)
            {
                int pIndex = uniformCount + i;
                int cIndex = i % GameConfig.SPAWN_CLUSTERS;
                double clusterCenterX = _clusterX[cIndex];
                double clusterCenterY = _clusterY[cIndex];
                double rawX = clusterCenterX + GaussRandom() * GameConfig.SPAWN_SIGMA;
                double rawY = clusterCenterY + GaussRandom() * GameConfig.SPAWN_SIGMA;
                double clampedX = Math.Min(Math.Max(rawX, minX), maxX);
                double clampedY = Math.Min(Math.Max(rawY, minY), maxY);

                X[pIndex] = clampedX;
                Y[pIndex] = clampedY;
                VX[pIndex] = 0.0;
                VY[pIndex] = 0.0;
                InMeasure[pIndex] = false;
                OutOfReach[pIndex] = false;
            }
        }

        private double GaussRandom()
        {
            double u = 0.0;
            double v = 0.0;
            while (u == 0.0)
            {
                u = _random.NextDouble();
            }
            while (v == 0.0)
            {
                v = _random.NextDouble();
            }
            return Math.Sqrt(-2.0 * Math.Log(u)) * Math.Cos(2.0 * Math.PI * v);
        }

        public void StartRound(double nowMs)
        {
            InitParticles();
            State = GameState.Attracting;
            IsPressing = true;
            PressStartTime = nowMs;
            CurrentDensity = 0.0;
            CurrentDangerRatio = 0.0;
            CurrentScore = 0;
            FinalScore = 0;
            GraceCounter = 0;
            Stage = DangerStage.Safe;
            ShakeMagnitude = 0.0;
            FlashOpacity = 0.0;
            ReachableCount = GameConfig.TOTAL_PARTICLES;
            BangPossible = true;
            UnreachableFrames = 0;
            for (int i = 0; i < GameConfig.TOTAL_PARTICLES; i++)
            {
                OutOfReach[i] = false;
            }

            _audio?.StartDrone();
        }

        public void ConfirmRound()
        {
            if (!IsPressing) return;
            IsPressing = false;
            ReachableCount = GameConfig.TOTAL_PARTICLES;
            BangPossible = true;
            UnreachableFrames = 0;
            for (int i = 0; i < GameConfig.TOTAL_PARTICLES; i++)
            {
                OutOfReach[i] = false;
            }

            // ドローン停止 & 警告パルス停止
            _audio?.StopDrone();
            _audio?.UpdateWarning(0.0, 0.0);

            if (State == GameState.Attracting)
            {
                // 正常リリースで確定
                State = GameState.Resolved;
                FinalScore = CurrentScore;
                _audio?.PlayResolve();
                if (FinalScore > HighScore)
                {
                    HighScore = FinalScore;
                }
            }
        }

        public void TriggerBigBang()
        {
            State = GameState.Bang;
            IsPressing = false;
            CurrentScore = 0;
            FinalScore = 0; // 仕様: そのラウンドのスコアは0
            GraceCounter = 0;
            FlashOpacity = 0.95;
            ShakeMagnitude = 22.0;
            ReachableCount = GameConfig.TOTAL_PARTICLES;
            BangPossible = true;
            UnreachableFrames = 0;
            for (int i = 0; i < GameConfig.TOTAL_PARTICLES; i++)
            {
                OutOfReach[i] = false;
            }

            // ドローン停止 & 警告パルス停止 & ビッグバン爆発音再生
            _audio?.StopDrone();
            _audio?.UpdateWarning(0.0, 0.0);
            _audio?.PlayBigBang();

            // 全粒子をカーソル中心から外側へ放射状に爆発飛散させる
            for (int i = 0; i < GameConfig.TOTAL_PARTICLES; i++)
            {
                double dx = X[i] - CursorX;
                double dy = Y[i] - CursorY;
                // JS版では未使用だが dist を計算している (const dist = Math.hypot(dx, dy) || 1;)
                double dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist == 0.0) dist = 1.0;
                _ = dist;

                double angle = Math.Atan2(dy, dx) + (_random.NextDouble() - 0.5) * 0.6;
                double blastSpeed = 450.0 + _random.NextDouble() * 850.0;
                VX[i] = Math.Cos(angle) * blastSpeed;
                VY[i] = Math.Sin(angle) * blastSpeed;
            }
        }

        public void Step(double dt, double nowMs)
        {
            // 演出値の減衰
            ShakeMagnitude = Math.Max(0.0, ShakeMagnitude - dt * 25.0);
            FlashOpacity = Math.Max(0.0, FlashOpacity - dt * 2.2);

            // 減衰をフレーム単位で掛けると高リフレッシュレート環境で効きすぎるため、60fps基準の時間指数にする
            double dampFactor = Math.Pow(GameConfig.DAMPING, dt * 60.0);

            if (State == GameState.Attracting)
            {
                double t = Math.Max(0.0, (nowMs - PressStartTime) / 1000.0);

                // 指数飽和曲線による集積半径 r(t) と引力 F(t) の計算 (引力に F_CREEP を加算)
                double currentR = GameConfig.R_MIN + (EffectiveRMax - GameConfig.R_MIN) * (1.0 - Math.Exp(-t / GameConfig.TAU_R));
                double currentF = GameConfig.F_MIN + (GameConfig.F_MAX - GameConfig.F_MIN) * (1.0 - Math.Exp(-t / GameConfig.TAU_F)) + GameConfig.F_CREEP * t;

                // 引力の中心をカーソルから微小に揺動
                double attractX = CursorX + GameConfig.WOBBLE_AX * Math.Cos(2.0 * Math.PI * GameConfig.WOBBLE_F1 * t);
                double attractY = CursorY + GameConfig.WOBBLE_AY * Math.Sin(2.0 * Math.PI * GameConfig.WOBBLE_F2 * t);

                // 粒子間反発の加速度を集計 (事前確保バッファ再利用)
                Array.Clear(_repAx, 0, _repAx.Length);
                Array.Clear(_repAy, 0, _repAy.Length);
                double d0 = GameConfig.REPULSION_D0;
                double d0Sq = d0 * d0;
                double eps = GameConfig.REPULSION_EPS;
                double k = GameConfig.REPULSION_K;

                for (int i = 0; i < GameConfig.TOTAL_PARTICLES; i++)
                {
                    double pix = X[i];
                    double piy = Y[i];
                    for (int j = i + 1; j < GameConfig.TOTAL_PARTICLES; j++)
                    {
                        double dx = X[j] - pix;
                        if (dx > d0 || dx < -d0) continue;
                        double dy = Y[j] - piy;
                        if (dy > d0 || dy < -d0) continue;
                        double d2 = dx * dx + dy * dy;
                        if (d2 >= d0Sq) continue;

                        double d = Math.Sqrt(d2);
                        double mag = k * (1.0 - Math.Max(d, eps) / d0);
                        double ux, uy;
                        if (d >= eps)
                        {
                            ux = dx / d;
                            uy = dy / d;
                        }
                        else
                        {
                            // 粒子が完全に重なっている: 添字から決まる固定方向を使う (Math.randomは使わない)
                            unchecked
                            {
                                uint h = (uint)((int)((long)i * 73856093L) ^ (int)((long)j * 19349663L));
                                double ang = (h % 62832) / 10000.0;
                                ux = Math.Cos(ang);
                                uy = Math.Sin(ang);
                            }
                        }

                        _repAx[i] -= ux * mag;
                        _repAy[i] -= uy * mag;
                        _repAx[j] += ux * mag;
                        _repAy[j] += uy * mag;
                    }
                }

                double rMeasureSq = (double)GameConfig.R_MEASURE * GameConfig.R_MEASURE;
                double currentRSq = currentR * currentR;
                double reachSq = EffectiveRMax * EffectiveRMax;
                int nMeasure = 0;
                int nReach = 0;

                for (int i = 0; i < GameConfig.TOTAL_PARTICLES; i++)
                {
                    double px = X[i];
                    double py = Y[i];

                    // 固定測定円 R_MEASURE 内の判定 (※重要: 生の CursorX / CursorY を中心に行う)
                    double mdx = CursorX - px;
                    double mdy = CursorY - py;
                    double mDistSq = mdx * mdx + mdy * mdy;

                    if (mDistSq <= rMeasureSq)
                    {
                        InMeasure[i] = true;
                        nMeasure++;
                    }
                    else
                    {
                        InMeasure[i] = false;
                    }

                    if (mDistSq <= reachSq)
                    {
                        OutOfReach[i] = false;
                        nReach++;
                    }
                    else
                    {
                        OutOfReach[i] = true;
                    }

                    // 粒子の合成加速度の計算
                    double accX = 0.0;
                    double accY = 0.0;

                    // 引力中心 (attractX, attractY) からの距離と集積判定
                    double adx = attractX - px;
                    double ady = attractY - py;
                    double aDistSq = adx * adx + ady * ady;

                    if (aDistSq <= currentRSq)
                    {
                        // 引力圏内: 引力中心方向への加速 (距離の下限クリップ 1.0 は従来どおり)
                        double aDist = Math.Sqrt(aDistSq);
                        double effectiveDist = Math.Max(aDist, 1.0);
                        double dirX = adx / effectiveDist;
                        double dirY = ady / effectiveDist;

                        accX += dirX * currentF;
                        accY += dirY * currentF;
                    }
                    else
                    {
                        // 引力圏外: 微弱なランダム揺動 (x → y の順で乱数を呼ぶ)
                        accX += (_random.NextDouble() - 0.5) * 2.0 * GameConfig.JITTER_FORCE;
                        accY += (_random.NextDouble() - 0.5) * 2.0 * GameConfig.JITTER_FORCE;
                    }

                    // 粒子間反発加速度を加算
                    accX += _repAx[i];
                    accY += _repAy[i];

                    // 合成加速度のクリッピング (ACCEL_CLIP)
                    double accMag = Math.Sqrt(accX * accX + accY * accY);
                    if (accMag > GameConfig.ACCEL_CLIP)
                    {
                        double accScale = GameConfig.ACCEL_CLIP / accMag;
                        accX *= accScale;
                        accY *= accScale;
                    }

                    // 速度更新と減衰 (減衰の掛かる順序: (v + a*dt) * dampFactor)
                    VX[i] = (VX[i] + accX * dt) * dampFactor;
                    VY[i] = (VY[i] + accY * dt) * dampFactor;

                    // 速度上限クリッピング (VELOCITY_CLIP)
                    double vMag = Math.Sqrt(VX[i] * VX[i] + VY[i] * VY[i]);
                    if (vMag > GameConfig.VELOCITY_CLIP)
                    {
                        double vScale = GameConfig.VELOCITY_CLIP / vMag;
                        VX[i] *= vScale;
                        VY[i] *= vScale;
                    }

                    // 位置更新
                    X[i] += VX[i] * dt;
                    Y[i] += VY[i] * dt;

                    // 画面端で反射
                    HandleBoundaryBounce(i);
                }

                ReachableCount = nReach;
                // 粒子が動く過程で一時的に下回ることがあるため、持続して初めて警告する
                // (下回った状態が続いたときだけ警告し、回復したら即座に解除する非対称な判定)
                if (nReach >= GameConfig.REQUIRED_PARTICLES)
                {
                    UnreachableFrames = 0;
                    BangPossible = true;
                }
                else
                {
                    UnreachableFrames++;
                    if (UnreachableFrames >= GameConfig.UNREACHABLE_WARN_FRAMES)
                    {
                        BangPossible = false;
                    }
                }

                // 密度計算 (※厳守: 分母は集積半径 r(t) ではなく固定の R_MEASURE)
                CurrentDensity = nMeasure / GameConfig.MEASURE_AREA;
                CurrentDangerRatio = CurrentDensity / GameConfig.BANG_THRESHOLD;
                CurrentScore = (int)Math.Floor((CurrentDensity / GameConfig.DENSITY_MAX) * 9999.0);

                // 警告パルスの更新 (毎フレーム currentDangerRatio を渡す)
                _audio?.UpdateWarning(CurrentDangerRatio, dt);

                // 危険度4段階の判定
                if (CurrentDangerRatio < GameConfig.DANGER_WARM)
                {
                    Stage = DangerStage.Safe;
                    ShakeMagnitude = 0.0;
                }
                else if (CurrentDangerRatio < GameConfig.DANGER_HOT)
                {
                    Stage = DangerStage.Warm;
                    ShakeMagnitude = 0.8;
                }
                else if (CurrentDangerRatio < GameConfig.DANGER_CRITICAL)
                {
                    Stage = DangerStage.Hot;
                    ShakeMagnitude = 2.4;
                }
                else
                {
                    Stage = DangerStage.Critical;
                    ShakeMagnitude = 5.5 + _random.NextDouble() * 2.0;
                }

                // ビッグバン判定 (BANG_GRACE_FRAMES連続でしきい値超過したら発火)
                if (CurrentDensity >= GameConfig.BANG_THRESHOLD)
                {
                    GraceCounter++;
                    if (GraceCounter >= GameConfig.BANG_GRACE_FRAMES)
                    {
                        TriggerBigBang();
                    }
                }
                else
                {
                    GraceCounter = 0;
                }
            }
            else
            {
                // 待機中 / 確定後 / ビッグバン後 (警告パルス停止)
                _audio?.UpdateWarning(0.0, dt);

                for (int i = 0; i < GameConfig.TOTAL_PARTICLES; i++)
                {
                    InMeasure[i] = false;

                    // 待機中または確定後は微弱な揺動でゆっくり漂う (vx → vy の順で乱数を呼ぶ)
                    if (State != GameState.Bang)
                    {
                        VX[i] += (_random.NextDouble() - 0.5) * 2.0 * (GameConfig.JITTER_FORCE * 0.4) * dt;
                        VY[i] += (_random.NextDouble() - 0.5) * 2.0 * (GameConfig.JITTER_FORCE * 0.4) * dt;
                    }

                    // 速度減衰
                    VX[i] *= dampFactor;
                    VY[i] *= dampFactor;

                    // 位置更新
                    X[i] += VX[i] * dt;
                    Y[i] += VY[i] * dt;

                    // 画面端で反射
                    HandleBoundaryBounce(i);
                }
            }
        }

        private void HandleBoundaryBounce(int i)
        {
            const double r = 2.5;
            if (X[i] < r)
            {
                X[i] = r;
                VX[i] = -VX[i] * 0.8;
            }
            else if (X[i] > Width - r)
            {
                X[i] = Width - r;
                VX[i] = -VX[i] * 0.8;
            }

            if (Y[i] < r)
            {
                Y[i] = r;
                VY[i] = -VY[i] * 0.8;
            }
            else if (Y[i] > Height - r)
            {
                Y[i] = Height - r;
                VY[i] = -VY[i] * 0.8;
            }
        }
    }
}
