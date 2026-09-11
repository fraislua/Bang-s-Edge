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
        private const string BEST_BANG_TIME_KEY = "bangs_edge_best_bang_time";
        private const float TOUCH_CURSOR_OFFSET_CSS_PX = 60f;
        private const float TOUCH_MIN_HIT_CSS_PX = 44f;

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
        private bool _isDraggingVolume;
        private float _lastSetVolume = -1f;
        private GameState _observedState = GameState.Ready;

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
            _sim.BestBangSeconds = PlayerPrefs.GetFloat(BEST_BANG_TIME_KEY, 0f);
            UnityroomRanking.Initialize();

            _lastTimestamp = 0.0;
            _simNow = 0.0;
            _accumulator = 0.0;
            _fpsEstimate = 60.0;
            _isInitialized = true;

            // HUD案内文を端末の主入力方式に合わせて初期化
            _hud.SetTouchLabels(WebDisplay.IsCoarsePointer);
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

            HandleRoundTransitions();

            // 3. 描画更新
            _renderer.Render(_sim, timestamp);
            _hud.Render(_sim, timestamp, _fpsEstimate);
        }

        private void ProcessInput()
        {
            var pointer = Pointer.current;
            if (pointer == null) return;

            // 縦向き時はプレイ不可のため、引き寄せ中なら終了処理を行って早期リターン
            bool portraitScreen = Screen.height > Screen.width;
            if (portraitScreen)
            {
                _isDraggingVolume = false;
                if (_sim != null && _sim.State == GameState.Attracting)
                {
                    _sim.ConfirmRound();
                    CheckSaveHighScore();
                    HandleRoundTransitions();
                }
                return;
            }

            bool touchInput = pointer is Touchscreen;

            Vector2 screenPos = pointer.position.ReadValue();
            Vector2 logicalPos = _letterbox.ScreenToLogical(screenPos);
            double lx = logicalPos.x;
            double ly = logicalPos.y;

            // カーソル位置の更新:
            // マウス時は毎フレーム追従するが、タッチ時は指を離している間の座標が不正確なため、
            // 押下中または押下瞬間のフレームのみ更新し、指で隠れないよう上方向(y引く)へずらす
            if (!touchInput)
            {
                _sim.CursorX = lx;
                _sim.CursorY = ly;
            }
            else if (pointer.press.isPressed || pointer.press.wasPressedThisFrame)
            {
                _sim.CursorX = lx;
                _sim.CursorY = ly - (double)TOUCH_CURSOR_OFFSET_CSS_PX * (double)WebDisplay.ScreenPixelsPerCssPixel / _letterbox.ViewScale;
            }

            // 押した瞬間 (StartRoundの前にカーソルが設定される順序を維持)
            if (pointer.press.wasPressedThisFrame)
            {
                // 直前の入力種別に合わせてHUDの案内文を切り替え
                _hud.SetTouchLabels(touchInput);

                // ミュートボタンの矩形判定。タッチ時は指の太さを考慮して上下のみ当たり判定を広げる
                Rect muteRect = GameHud.MuteButtonRect;
                float touchMuteHitPad = touchInput
                    ? Mathf.Max(0f, (TOUCH_MIN_HIT_CSS_PX * WebDisplay.ScreenPixelsPerCssPixel / (float)_letterbox.ViewScale - muteRect.height) * 0.5f)
                    : 0f;
                bool inMuteBtn = lx >= muteRect.xMin &&
                                 lx <= muteRect.xMax &&
                                 ly >= muteRect.yMin - touchMuteHitPad &&
                                 ly <= muteRect.yMax + touchMuteHitPad;

                Rect sliderRect = GameHud.VolumeSliderRect;
                float touchSliderHitPad = touchInput
                    ? Mathf.Max(0f, (TOUCH_MIN_HIT_CSS_PX * WebDisplay.ScreenPixelsPerCssPixel / (float)_letterbox.ViewScale - sliderRect.height) * 0.5f)
                    : 0f;
                bool inSlider = lx >= sliderRect.xMin &&
                                lx <= sliderRect.xMax &&
                                ly >= sliderRect.yMin - touchSliderHitPad &&
                                ly <= sliderRect.yMax + touchSliderHitPad;

                if (inMuteBtn)
                {
                    AudioMuteManager.ToggleMute();
                }
                else if (inSlider)
                {
                    _isDraggingVolume = true;
                    if (AudioMuteManager.IsMuted)
                    {
                        AudioMuteManager.SetMuted(false);
                    }
                    float v = GameHud.VolumeFromLogicalX(lx);
                    AudioMuteManager.SetVolume(v);
                    _lastSetVolume = v;
                }
                else
                {
                    // JS版では performance.now() (実時間ミリ秒) を渡す
                    double realNowMs = Time.realtimeSinceStartupAsDouble * 1000.0;
                    _sim.StartRound(realNowMs);
                }
            }
            else if (_isDraggingVolume && pointer.press.isPressed)
            {
                float v = GameHud.VolumeFromLogicalX(lx);
                if (Mathf.Abs(v - _lastSetVolume) >= 0.001f)
                {
                    AudioMuteManager.SetVolume(v);
                    _lastSetVolume = v;
                }
            }

            // 離した瞬間
            if (pointer.press.wasReleasedThisFrame)
            {
                if (_isDraggingVolume)
                {
                    _isDraggingVolume = false;
                }
                else
                {
                    _sim.ConfirmRound();
                    CheckSaveHighScore();
                    HandleRoundTransitions();
                }
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            // フォーカス喪失時 (JSの pointercancel 相当)
            if (!hasFocus)
            {
                _isDraggingVolume = false;
                if (_sim != null)
                {
                    _sim.ConfirmRound();
                    CheckSaveHighScore();
                    HandleRoundTransitions();
                }
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

        private void HandleRoundTransitions()
        {
            if (_sim == null) return;

            GameState currentState = _sim.State;
            if (currentState != _observedState)
            {
                if (_observedState == GameState.Attracting && currentState == GameState.Resolved)
                {
                    UnityroomRanking.SendScore(_sim.FinalScore);
                }
                else if (_observedState == GameState.Attracting && currentState == GameState.Bang)
                {
                    UnityroomRanking.SendBangTime(_sim.LastBangSeconds);
                    if (_sim.LastBangWasBest)
                    {
                        PlayerPrefs.SetFloat(BEST_BANG_TIME_KEY, (float)_sim.BestBangSeconds);
                        PlayerPrefs.Save();
                    }
                }

                _observedState = currentState;
            }
        }
    }
}
