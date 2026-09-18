using System;

namespace BangsEdge.Simulation
{
    /// <summary>
    /// 離した瞬間の状態を複製し、もし離さずにカーソルを固定していたら何ステップ後にビッグバンだったかを予測するプローブ。
    /// 1フレームで全ステップ進めると負荷が高いため、フレームごとに Advance で分割実行可能。
    /// </summary>
    public sealed class MarginProbe
    {
        public const int MAX_STEPS = 600;              // 10秒ぶん (FIXED_DT = 1/60)

        private BangSimulation _sim;
        private double _nowMs;

        public bool IsRunning { get; private set; }    // Begin 後、まだ結果が出ていない
        public bool IsDone { get; private set; }       // 結果確定 (Bang したか、MAX_STEPS まで進めた)
        public int StepsToBang { get; private set; }   // Bang した場合は Begin からのステップ数 (1以上)。しなかった/不明なら -1
        public int StepsAdvanced { get; private set; } // これまでに進めたステップ数
        public double SecondsToBang => StepsToBang < 0 ? -1.0 : StepsToBang * GameConfig.FIXED_DT;

        public MarginProbe()
        {
            Reset();
        }

        /// <summary>
        /// プローブ状態を初期化し、複製シミュレーションを破棄する。
        /// </summary>
        public void Reset()
        {
            IsRunning = false;
            IsDone = false;
            StepsToBang = -1;
            StepsAdvanced = 0;
            _sim = null;
            _nowMs = 0.0;
        }

        /// <summary>
        /// シミュレーションの現在状態を複製してプローブを開始する。
        /// </summary>
        public void Begin(BangSimulation source, IRandomSource random, double nowMs)
        {
            Reset();

            if (source == null) throw new ArgumentNullException(nameof(source));
            if (random == null) throw new ArgumentNullException(nameof(random));

            if (source.State != GameState.Attracting || !source.IsPressing)
            {
                IsDone = true;
                IsRunning = false;
                StepsToBang = -1;
                return;
            }

            _sim = source.CloneForProbe(random);
            _nowMs = nowMs;
            IsRunning = true;
            IsDone = false;
        }

        /// <summary>
        /// 複製シミュレーションを最大 maxSteps ステップ進める。実際に進めたステップ数を返す。
        /// </summary>
        public int Advance(int maxSteps)
        {
            if (!IsRunning || _sim == null || maxSteps <= 0)
            {
                return 0;
            }

            int stepsTaken = 0;
            for (int i = 0; i < maxSteps; i++)
            {
                _nowMs += GameConfig.FIXED_DT * 1000.0;
                _sim.Step(GameConfig.FIXED_DT, _nowMs);
                StepsAdvanced++;
                stepsTaken++;

                if (_sim.State == GameState.Bang)
                {
                    StepsToBang = StepsAdvanced;
                    IsDone = true;
                    IsRunning = false;
                    break;
                }

                if (StepsAdvanced >= MAX_STEPS)
                {
                    StepsToBang = -1;
                    IsDone = true;
                    IsRunning = false;
                    break;
                }
            }

            return stepsTaken;
        }
    }
}
