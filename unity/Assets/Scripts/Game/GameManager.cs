using System;
using UnityEngine;
using UnityEngine.InputSystem;
using BangsEdge.Simulation;

namespace BangsEdge.Game
{
    /// <summary>
    /// メインループ、入力受付、シミュレーション進行、およびハイスコア管理を担当するマネージャーコンポーネント。
    /// </summary>
    public sealed class GameManager : MonoBehaviour
    {
        private const string HIGH_SCORE_KEY = "bangs_edge_high_score";

        private BangSimulation _sim;
        private GameRenderer _renderer;
        private LetterboxCamera _letterbox;
        private GameHud _hud;

        private double _lastTimestamp;
        private double _simNow;
        private double _accumulator;
        private double _fpsEstimate = 60.0;
        private int _savedHighScore;
        private bool _isInitialized;

        /// <summary>
        /// 指数移動平均によるFPS推定値 (P5のHUD表示等で使用)。
        /// </summary>
        public double FpsEstimate => _fpsEstimate;

        /// <summary>
        /// 実行中のシミュレーションインスタンス。
        /// </summary>
        public BangSimulation Simulation => _sim;

        public void Initialize(GameRenderer renderer, LetterboxCamera letterbox, GameHud hud)
        {
            _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
            _letterbox = letterbox ?? throw new ArgumentNullException(nameof(letterbox));
            _hud = hud ?? throw new ArgumentNullException(nameof(hud));

            // ハイスコア読み込み (PlayerPrefs)
            _savedHighScore = PlayerPrefs.GetInt(HIGH_SCORE_KEY, 0);

            // シミュレーション乱数生成器の初期化 (起動時刻に基づくシード)
            uint seed = unchecked((uint)(DateTime.UtcNow.Ticks ^ 0x5DEECE66DL));
            if (seed == 0) seed = 1;
            var rng = new Mulberry32(seed);

            // シミュレーション初期化 (WebAudioEvents 経由で WebGL 音響イベントを中継)
            _sim = new BangSimulation(rng, new WebAudioEvents());
            _sim.HighScore = _savedHighScore;

            _lastTimestamp = 0.0;
            _simNow = 0.0;
            _accumulator = 0.0;
            _fpsEstimate = 60.0;
            _isInitialized = true;
        }

        private void Update()
        {
            if (!_isInitialized) return;

            // 1. 入力処理 (メインループの前)
            ProcessInput();

            // 2. メインループ (script.js の loop と同じ挙動・順序・定数)
            double timestamp = Time.realtimeSinceStartupAsDouble * 1000.0;

            if (_lastTimestamp <= 0.0)
            {
                _lastTimestamp = timestamp;
                _simNow = timestamp;
            }

            double rawDt = (timestamp - _lastTimestamp) / 1000.0;
            _lastTimestamp = timestamp;

            if (rawDt > 0.0)
            {
                _fpsEstimate += (1.0 / rawDt - _fpsEstimate) * 0.1;
            }

            // タブ復帰などで大きく飛んだ分は捨てる
            _accumulator += Math.Min(rawDt, 0.25);

            int steps = 0;
            while (_accumulator >= GameConfig.FIXED_DT && steps < GameConfig.MAX_SUBSTEPS)
            {
                _simNow += GameConfig.FIXED_DT * 1000.0;
                _sim.Step(GameConfig.FIXED_DT, _simNow);
                _accumulator -= GameConfig.FIXED_DT;
                steps++;
            }

            // 上限まで進めても追いつけない場合は残りを捨てる (スローモーションにはなるが破綻はしない)
            if (_accumulator >= GameConfig.FIXED_DT)
            {
                _accumulator = 0.0;
                _simNow = timestamp;
            }

            // 3. 描画更新
            _renderer.Render(_sim, timestamp);
            _hud.Render(_sim, timestamp, _fpsEstimate);
        }

        private void ProcessInput()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            Vector2 screenPos = mouse.position.ReadValue();
            Vector2 logicalPos = _letterbox.ScreenToLogical(screenPos);
            double lx = logicalPos.x;
            double ly = logicalPos.y;

            // 毎フレーム、マウス位置を論理座標に変換してカーソルに設定する
            _sim.CursorX = lx;
            _sim.CursorY = ly;

            // 左ボタンを押した瞬間
            if (mouse.leftButton.wasPressedThisFrame)
            {
                // ミュートボタンの矩形判定 (右上: size 28, marginRight 18, marginTop 18)
                // rect.x = 1920 - 18 - 28 = 1874, rect.y = 18
                bool inMuteBtn = lx >= (GameConfig.LOGICAL_WIDTH - 18 - 28) &&
                                 lx <= (GameConfig.LOGICAL_WIDTH - 18) &&
                                 ly >= 18 &&
                                 ly <= (18 + 28);

                if (!inMuteBtn)
                {
                    // JS版では performance.now() (実時間ミリ秒) を渡す
                    double realNowMs = Time.realtimeSinceStartupAsDouble * 1000.0;
                    _sim.StartRound(realNowMs);
                }
                else
                {
                    AudioMuteManager.ToggleMute();
                }
            }

            // 左ボタンを離した瞬間
            if (mouse.leftButton.wasReleasedThisFrame)
            {
                _sim.ConfirmRound();
                CheckSaveHighScore();
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            // フォーカス喪失時 (JSの pointercancel 相当)
            if (!hasFocus && _sim != null)
            {
                _sim.ConfirmRound();
                CheckSaveHighScore();
            }
        }

        private void CheckSaveHighScore()
        {
            if (_sim != null && _sim.HighScore > _savedHighScore)
            {
                _savedHighScore = _sim.HighScore;
                PlayerPrefs.SetInt(HIGH_SCORE_KEY, _savedHighScore);
                PlayerPrefs.Save();
            }
        }
    }
}
