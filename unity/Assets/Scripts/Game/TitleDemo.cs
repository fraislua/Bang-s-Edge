using System;
using UnityEngine;
using BangsEdge.Simulation;

namespace BangsEdge.Game
{
    /// <summary>
    /// タイトル画面の自動デモコンポーネント。
    /// 画面右下の小窓で、AIが「押して集め、縁を越えた直後に離す」1ラウンドを繰り返し実演する。
    /// 本編の粒子やシミュレーションには一切影響せず、独立したシミュレーションインスタンスを持つ。
    /// </summary>
    public sealed class TitleDemo : MonoBehaviour
    {
        private enum DemoState
        {
            Idle,
            Holding,
            Released
        }

        private GameRenderer _renderer;
        private GameManager _manager;
        private BangSimulation _demoSim;

        // ルートオブジェクトおよび共有スプライト
        private GameObject _rootObject;
        private Sprite _circleSprite;
        private Sprite _solidSprite;

        // 小窓内描画部品
        private SpriteRenderer _barFillSr;
        private Transform _barFillTransform;
        private LineRenderer _attractLine;
        private LineRenderer _measureLine;
        private LineRenderer _frameLine;
        private Transform[] _particleTransforms;
        private SpriteRenderer[] _particleSrs;
        private SpriteRenderer _cursorSr;
        private SpriteRenderer _releaseRingSr;
        private Transform _releaseRingTransform;
        private TextMesh _titleTextMesh;
        private TextMesh _statusTextMesh;

        // 集積円事前計算バッファ
        private const int ATTRACT_SEGMENTS = 64;
        private float[] _attractCos;
        private float[] _attractSin;
        private Vector3[] _attractPositions;

        // GC Alloc 削減用キャッシュ定数
        private readonly Vector3 _particleScaleNormal = new Vector3(10f, 10f, 1f);
        private readonly Vector3 _particleScaleMeasure = new Vector3(14f, 14f, 1f);
        private readonly Color _colorOutOfReach = new Color(100f / 255f, 116f / 255f, 139f / 255f, 0.25f);

        // 台本・時間管理変数
        private DemoState _scriptState = DemoState.Idle;
        private double _stateTimer;
        private double _demoNowMs;
        private double _accumulator;
        private DangerStage _cachedStage = (DangerStage)(-1);
        private bool _isInitialized;

        public void Initialize(GameRenderer renderer, GameManager manager)
        {
            _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));

            // 独立したシミュレーション核 (乱数は demoSim の Mulberry32 のみを使用)
            _demoSim = new BangSimulation(new Mulberry32(20260918u), null);

            // 共有スプライトの動的生成
            _circleSprite = GameRenderer.CreateCircleSprite(64, 29.5f);
            _solidSprite = GameRenderer.CreateSolidSprite();

            // 小窓ルートの構築 (画面振動する WorldContainer の外、renderer の子)
            _rootObject = new GameObject("TitleDemo");
            _rootObject.transform.SetParent(_renderer.transform, false);
            _rootObject.transform.localPosition = new Vector3(1420f, -790f, 0f);
            _rootObject.transform.localScale = new Vector3(0.25f, 0.25f, 1f);

            // 集積円用三角関数バッファの初期化
            _attractCos = new float[ATTRACT_SEGMENTS];
            _attractSin = new float[ATTRACT_SEGMENTS];
            _attractPositions = new Vector3[ATTRACT_SEGMENTS];
            for (int i = 0; i < ATTRACT_SEGMENTS; i++)
            {
                float rad = i * (Mathf.PI * 2f / ATTRACT_SEGMENTS);
                _attractCos[i] = Mathf.Cos(rad);
                _attractSin[i] = Mathf.Sin(rad);
            }

            // 各表示部品の構築
            BuildBackground();
            BuildFrame();
            BuildDensityBar();
            BuildFields();
            BuildParticles();
            BuildCursorAndRing();
            BuildTexts();

            // 初期状態を Idle に設定
            TransitionTo(DemoState.Idle);

            _stateTimer = 0.0;
            _demoNowMs = 0.0;
            _accumulator = 0.0;
            _isInitialized = true;
        }

        private void BuildBackground()
        {
            var bgGo = new GameObject("Background");
            bgGo.transform.SetParent(_rootObject.transform, false);
            bgGo.transform.localPosition = new Vector3(960f, -540f, 0f);
            bgGo.transform.localScale = new Vector3(1920f, 1080f, 1f);

            var bgSr = bgGo.AddComponent<SpriteRenderer>();
            bgSr.sprite = _solidSprite;
            bgSr.sharedMaterial = _renderer.SharedSpriteMaterial;
            bgSr.sortingOrder = 30;
            bgSr.color = new Color(5f / 255f, 6f / 255f, 10f / 255f, 0.80f);
        }

        private void BuildFrame()
        {
            var frameGo = new GameObject("Frame");
            frameGo.transform.SetParent(_rootObject.transform, false);

            _frameLine = frameGo.AddComponent<LineRenderer>();
            SetupLineRenderer(_frameLine, 4, 6f, new Color(1f, 1f, 1f, 0.25f), 31);

            Vector3[] framePos = new Vector3[4]
            {
                new Vector3(2f, -2f, 0f),
                new Vector3(1918f, -2f, 0f),
                new Vector3(1918f, -1078f, 0f),
                new Vector3(2f, -1078f, 0f)
            };
            _frameLine.SetPositions(framePos);
            _frameLine.enabled = true;
        }

        private void BuildDensityBar()
        {
            // 密度バー背景
            var barBgGo = new GameObject("BarBackground");
            barBgGo.transform.SetParent(_rootObject.transform, false);
            barBgGo.transform.localPosition = new Vector3(960f, -31f, 0f);
            barBgGo.transform.localScale = new Vector3(420f, 14f, 1f);

            var barBgSr = barBgGo.AddComponent<SpriteRenderer>();
            barBgSr.sprite = _solidSprite;
            barBgSr.sharedMaterial = _renderer.SharedSpriteMaterial;
            barBgSr.sortingOrder = 32;
            barBgSr.color = new Color(1f, 1f, 1f, 0.12f);

            // 密度バー塗り (左端 750 固定、幅 420 まで拡大)
            var barFillGo = new GameObject("BarFill");
            barFillGo.transform.SetParent(_rootObject.transform, false);
            _barFillTransform = barFillGo.transform;
            _barFillTransform.localPosition = new Vector3(750f, -31f, 0f);
            _barFillTransform.localScale = new Vector3(0f, 14f, 1f);

            _barFillSr = barFillGo.AddComponent<SpriteRenderer>();
            _barFillSr.sprite = _solidSprite;
            _barFillSr.sharedMaterial = _renderer.SharedSpriteMaterial;
            _barFillSr.sortingOrder = 33;
            _barFillSr.enabled = false;

            // 限界線 (しきい値 85%: 750 + 420 * 0.85 = 1107)
            var limitGo = new GameObject("LimitLine");
            limitGo.transform.SetParent(_rootObject.transform, false);
            limitGo.transform.localPosition = new Vector3(1107f, -31f, 0f);
            limitGo.transform.localScale = new Vector3(2f, 20f, 1f);

            var limitSr = limitGo.AddComponent<SpriteRenderer>();
            limitSr.sprite = _solidSprite;
            limitSr.sharedMaterial = _renderer.SharedSpriteMaterial;
            limitSr.sortingOrder = 34;
            limitSr.color = new Color(255f / 255f, 51f / 255f, 51f / 255f, 1f);
        }

        private void BuildFields()
        {
            // 集積円 (64点ループ)
            var attractGo = new GameObject("AttractCircle");
            attractGo.transform.SetParent(_rootObject.transform, false);
            _attractLine = attractGo.AddComponent<LineRenderer>();
            SetupLineRenderer(_attractLine, ATTRACT_SEGMENTS, 6f, new Color(1f, 1f, 1f, 0.4f), 32);
            _attractLine.enabled = false;

            // 測定円 (48点ループ、半径 R_MEASURE 固定)
            const int measureSegments = 48;
            var measureGo = new GameObject("MeasureCircle");
            measureGo.transform.SetParent(_rootObject.transform, false);
            _measureLine = measureGo.AddComponent<LineRenderer>();
            SetupLineRenderer(_measureLine, measureSegments, 6f, new Color(0f, 229f / 255f, 1f, 0.7f), 32);

            Vector3[] measurePos = new Vector3[measureSegments];
            for (int i = 0; i < measureSegments; i++)
            {
                float rad = i * (Mathf.PI * 2f / measureSegments);
                measurePos[i] = new Vector3(
                    960f + Mathf.Cos(rad) * GameConfig.R_MEASURE,
                    -540f + Mathf.Sin(rad) * GameConfig.R_MEASURE,
                    0f
                );
            }
            _measureLine.SetPositions(measurePos);
            _measureLine.enabled = false;
        }

        private void BuildParticles()
        {
            var particlesGroup = new GameObject("Particles");
            particlesGroup.transform.SetParent(_rootObject.transform, false);

            _particleTransforms = new Transform[GameConfig.TOTAL_PARTICLES];
            _particleSrs = new SpriteRenderer[GameConfig.TOTAL_PARTICLES];

            for (int i = 0; i < GameConfig.TOTAL_PARTICLES; i++)
            {
                var pGo = new GameObject($"Particle_{i}");
                pGo.transform.SetParent(particlesGroup.transform, false);

                var sr = pGo.AddComponent<SpriteRenderer>();
                sr.sprite = _circleSprite;
                sr.sharedMaterial = _renderer.SharedSpriteMaterial;
                sr.sortingOrder = 33;

                _particleTransforms[i] = pGo.transform;
                _particleSrs[i] = sr;
            }
        }

        private void BuildCursorAndRing()
        {
            // カーソル印 (中心 960, -540、scale 44)
            var cursorGo = new GameObject("CursorMark");
            cursorGo.transform.SetParent(_rootObject.transform, false);
            cursorGo.transform.localPosition = new Vector3(960f, -540f, 0f);
            cursorGo.transform.localScale = new Vector3(44f, 44f, 1f);

            _cursorSr = cursorGo.AddComponent<SpriteRenderer>();
            _cursorSr.sprite = _circleSprite;
            _cursorSr.sharedMaterial = _renderer.SharedSpriteMaterial;
            _cursorSr.sortingOrder = 35;
            _cursorSr.color = new Color(1f, 1f, 1f, 0.30f);

            // 離した瞬間の輪
            var ringGo = new GameObject("ReleaseRing");
            ringGo.transform.SetParent(_rootObject.transform, false);
            ringGo.transform.localPosition = new Vector3(960f, -540f, 0f);
            ringGo.transform.localScale = new Vector3(80f, 80f, 1f);
            _releaseRingTransform = ringGo.transform;

            _releaseRingSr = ringGo.AddComponent<SpriteRenderer>();
            _releaseRingSr.sprite = _circleSprite;
            _releaseRingSr.sharedMaterial = _renderer.SharedSpriteMaterial;
            _releaseRingSr.sortingOrder = 36;
            _releaseRingSr.color = new Color(1f, 1f, 1f, 0.7f);
            _releaseRingSr.enabled = false;
        }

        private void BuildTexts()
        {
            var font = Resources.Load<Font>("Fonts/BIZUDGothic-Bold");
            Material fontMat = (font != null) ? font.material : null;

            // 見出し "DEMO"
            var titleGo = new GameObject("TitleText");
            titleGo.transform.SetParent(_rootObject.transform, false);
            titleGo.transform.localPosition = new Vector3(40f, -20f, 0f);

            _titleTextMesh = titleGo.AddComponent<TextMesh>();
            _titleTextMesh.font = font;
            _titleTextMesh.fontSize = 64;
            _titleTextMesh.characterSize = 9f;   // TextMesh の高さ ≈ fontSize × characterSize ÷ 10 (デモ論理px)。root の 0.25 倍が掛かる
            _titleTextMesh.anchor = TextAnchor.UpperLeft;
            _titleTextMesh.alignment = TextAlignment.Left;
            _titleTextMesh.color = new Color(148f / 255f, 163f / 255f, 184f / 255f, 1f);
            _titleTextMesh.text = "DEMO";

            var titleMr = titleGo.GetComponent<MeshRenderer>();
            if (titleMr != null)
            {
                if (fontMat != null) titleMr.sharedMaterial = fontMat;
                titleMr.sortingOrder = 40;
            }

            // 状態文
            var statusGo = new GameObject("StatusText");
            statusGo.transform.SetParent(_rootObject.transform, false);
            statusGo.transform.localPosition = new Vector3(960f, -1040f, 0f);

            _statusTextMesh = statusGo.AddComponent<TextMesh>();
            _statusTextMesh.font = font;
            _statusTextMesh.fontSize = 64;
            _statusTextMesh.characterSize = 9f;
            _statusTextMesh.anchor = TextAnchor.LowerCenter;
            _statusTextMesh.alignment = TextAlignment.Center;
            _statusTextMesh.color = new Color(148f / 255f, 163f / 255f, 184f / 255f, 1f);
            _statusTextMesh.text = "HOLD TO GATHER, RELEASE AT THE EDGE";

            var statusMr = statusGo.GetComponent<MeshRenderer>();
            if (statusMr != null)
            {
                if (fontMat != null) statusMr.sharedMaterial = fontMat;
                statusMr.sortingOrder = 40;
            }
        }

        private void SetupLineRenderer(LineRenderer lr, int count, float width, Color color, int sortingOrder)
        {
            lr.sharedMaterial = _renderer.SharedSpriteMaterial;
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.positionCount = count;
            lr.startWidth = width;
            lr.endWidth = width;
            lr.startColor = color;
            lr.endColor = color;
            lr.sortingOrder = sortingOrder;
            lr.textureMode = LineTextureMode.Stretch;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
        }

        private void Update()
        {
            if (!_isInitialized || _manager == null || _manager.Simulation == null) return;

            // 本編が Ready のときだけ動かして表示。それ以外は非表示にして停止する。
            bool isReady = (_manager.Simulation.State == GameState.Ready);
            if (!isReady)
            {
                if (_rootObject != null && _rootObject.activeSelf)
                {
                    _rootObject.SetActive(false);
                }
                return;
            }

            if (_rootObject != null && !_rootObject.activeSelf)
            {
                _rootObject.SetActive(true);
            }

            // 時間進行 (固定ステップ)
            AdvanceTime();

            // 描画更新
            RenderDemo();
        }

        private void AdvanceTime()
        {
            double dt = Math.Min(Time.deltaTime, 0.25);
            _accumulator += dt;

            int steps = 0;
            while (_accumulator >= GameConfig.FIXED_DT && steps < GameConfig.MAX_SUBSTEPS)
            {
                _demoNowMs += GameConfig.FIXED_DT * 1000.0;
                AdvanceStep();
                _accumulator -= GameConfig.FIXED_DT;
                steps++;
            }

            // 上限まで進めても追いつけない残りは捨てる
            if (_accumulator >= GameConfig.FIXED_DT)
            {
                _accumulator = 0.0;
            }
        }

        private void AdvanceStep()
        {
            _stateTimer += GameConfig.FIXED_DT;

            switch (_scriptState)
            {
                case DemoState.Idle:
                    _demoSim.Step(GameConfig.FIXED_DT, _demoNowMs);
                    if (_stateTimer >= 1.2)
                    {
                        TransitionTo(DemoState.Holding);
                    }
                    break;

                case DemoState.Holding:
                    _demoSim.Step(GameConfig.FIXED_DT, _demoNowMs);
                    // しきい値を越えて3ステップ目で離す(GraceCounter >= 3)。安全弁として20秒超えた場合も確定へ進む
                    if (_demoSim.GraceCounter >= 3 || _stateTimer >= 20.0)
                    {
                        _demoSim.ConfirmRound();
                        TransitionTo(DemoState.Released);
                    }
                    break;

                case DemoState.Released:
                    _demoSim.Step(GameConfig.FIXED_DT, _demoNowMs);
                    if (_stateTimer >= 2.5)
                    {
                        TransitionTo(DemoState.Idle);
                    }
                    break;
            }
        }

        private void TransitionTo(DemoState nextState)
        {
            _scriptState = nextState;
            _stateTimer = 0.0;
            _cachedStage = (DangerStage)(-1);

            switch (nextState)
            {
                case DemoState.Idle:
                    _cursorSr.color = new Color(1f, 1f, 1f, 0.30f);
                    _attractLine.enabled = false;
                    _measureLine.enabled = false;
                    _barFillSr.enabled = false;
                    _releaseRingSr.enabled = false;
                    UpdateStatusText();
                    break;

                case DemoState.Holding:
                    _demoSim.CursorX = 960.0;
                    _demoSim.CursorY = 540.0;
                    _demoSim.StartRound(_demoNowMs);
                    _cursorSr.color = new Color(1f, 1f, 1f, 0.85f);
                    _attractLine.enabled = true;
                    _measureLine.enabled = true;
                    _barFillSr.enabled = true;
                    _releaseRingSr.enabled = false;
                    UpdateStatusText();
                    break;

                case DemoState.Released:
                    _cursorSr.color = new Color(0f, 229f / 255f, 1f, 0.90f);
                    _attractLine.enabled = false;
                    _measureLine.enabled = false;
                    _barFillSr.enabled = false;
                    _releaseRingSr.enabled = true;
                    _releaseRingTransform.localScale = new Vector3(80f, 80f, 1f);
                    _releaseRingSr.color = new Color(1f, 1f, 1f, 0.70f);
                    UpdateStatusText();
                    break;
            }
        }

        private void UpdateStatusText()
        {
            if (_statusTextMesh == null) return;

            switch (_scriptState)
            {
                case DemoState.Idle:
                    _statusTextMesh.text = "HOLD TO GATHER, RELEASE AT THE EDGE";
                    _statusTextMesh.color = new Color(148f / 255f, 163f / 255f, 184f / 255f, 1f);
                    break;

                case DemoState.Holding:
                    switch (_demoSim.Stage)
                    {
                        case DangerStage.Warm:
                            _statusTextMesh.text = "HOLDING…  DANGER: WARM";
                            _statusTextMesh.color = new Color(1f, 234f / 255f, 0f, 1f);
                            break;
                        case DangerStage.Hot:
                            _statusTextMesh.text = "HOLDING…  DANGER: HOT";
                            _statusTextMesh.color = new Color(1f, 59f / 255f, 48f / 255f, 1f);
                            break;
                        case DangerStage.Critical:
                            _statusTextMesh.text = "HOLDING…  DANGER: CRITICAL";
                            _statusTextMesh.color = Color.white;
                            break;
                        default: // Safe
                            _statusTextMesh.text = "HOLDING…  DANGER: SAFE";
                            _statusTextMesh.color = Color.white;
                            break;
                    }
                    break;

                case DemoState.Released:
                    _statusTextMesh.text = $"RELEASE!  SCORE {_demoSim.FinalScore}";
                    _statusTextMesh.color = new Color(0f, 229f / 255f, 1f, 1f);
                    break;
            }
        }

        private void RenderDemo()
        {
            // Holding 中の危険度段階変化を検知して状態文を更新
            if (_scriptState == DemoState.Holding && _demoSim.Stage != _cachedStage)
            {
                _cachedStage = _demoSim.Stage;
                UpdateStatusText();
            }

            // 1. 集積円の更新 (Holding 中のみ)
            if (_scriptState == DemoState.Holding)
            {
                double attractElapsedSec = Math.Max(0.0, (_demoNowMs - _demoSim.PressStartTime) / 1000.0);
                double attractR = GameConfig.R_MIN + (_demoSim.EffectiveRMax - GameConfig.R_MIN) * (1.0 - Math.Exp(-attractElapsedSec / GameConfig.TAU_R));
                float attractRf = (float)attractR;

                for (int k = 0; k < ATTRACT_SEGMENTS; k++)
                {
                    _attractPositions[k].x = 960f + _attractCos[k] * attractRf;
                    _attractPositions[k].y = -540f + _attractSin[k] * attractRf;
                    _attractPositions[k].z = 0f;
                }
                _attractLine.SetPositions(_attractPositions);

                Color attractColor;
                switch (_demoSim.Stage)
                {
                    case DangerStage.Warm:
                        attractColor = new Color(1f, 234f / 255f, 0f, 0.7f);
                        break;
                    case DangerStage.Hot:
                        attractColor = new Color(1f, 59f / 255f, 48f / 255f, 0.85f);
                        break;
                    case DangerStage.Critical:
                        attractColor = new Color(1f, 1f, 1f, 0.9f);
                        break;
                    default: // Safe
                        attractColor = new Color(1f, 1f, 1f, 0.4f);
                        break;
                }
                _attractLine.startColor = attractColor;
                _attractLine.endColor = attractColor;
            }

            // 2. 密度バー塗りの更新 (Holding 中のみ)
            if (_scriptState == DemoState.Holding)
            {
                float barDensityRatio = Mathf.Clamp01((float)(_demoSim.CurrentDensity / GameConfig.DENSITY_MAX));
                float barWidth = 420f * barDensityRatio;
                _barFillTransform.localPosition = new Vector3(750f + barWidth * 0.5f, -31f, 0f);
                _barFillTransform.localScale = new Vector3(barWidth, 14f, 1f);

                Color barColor;
                switch (_demoSim.Stage)
                {
                    case DangerStage.Warm:
                        barColor = new Color(1f, 234f / 255f, 0f, 1f);
                        break;
                    case DangerStage.Hot:
                        barColor = new Color(1f, 85f / 255f, 0f, 1f);
                        break;
                    case DangerStage.Critical:
                        // 白と (255, 0, 51) を 80ms で交互
                        bool barFlashWhite = (_demoNowMs % 160.0 < 80.0);
                        barColor = barFlashWhite ? Color.white : new Color(1f, 0f, 51f / 255f, 1f);
                        break;
                    default: // Safe
                        barColor = new Color(0f, 229f / 255f, 1f, 1f);
                        break;
                }
                _barFillSr.color = barColor;
            }

            // 3. 粒子の更新
            Color partOuterColor;
            Color partInnerColor;

            if (_scriptState == DemoState.Holding)
            {
                switch (_demoSim.Stage)
                {
                    case DangerStage.Warm:
                        partOuterColor = new Color(1f, 234f / 255f, 0f, 1f);
                        partInnerColor = new Color(1f, 249f / 255f, 166f / 255f, 1f);
                        break;
                    case DangerStage.Hot:
                        partOuterColor = new Color(1f, 59f / 255f, 48f / 255f, 1f);
                        partInnerColor = new Color(1f, 163f / 255f, 158f / 255f, 1f);
                        break;
                    case DangerStage.Critical:
                        partOuterColor = new Color(1f, 51f / 255f, 0f, 1f);
                        partInnerColor = Color.white;
                        break;
                    default: // Safe
                        partOuterColor = new Color(203f / 255f, 213f / 255f, 225f / 255f, 1f);
                        partInnerColor = Color.white;
                        break;
                }
            }
            else
            {
                partOuterColor = new Color(203f / 255f, 213f / 255f, 225f / 255f, 1f);
                partInnerColor = Color.white;
            }

            for (int i = 0; i < GameConfig.TOTAL_PARTICLES; i++)
            {
                float partPx = (float)_demoSim.X[i];
                float partPy = (float)(-_demoSim.Y[i]);
                _particleTransforms[i].localPosition = new Vector3(partPx, partPy, 0f);

                bool partOutOfReach = _demoSim.OutOfReach[i];
                bool partInMeasure = _demoSim.InMeasure[i];

                if (partOutOfReach)
                {
                    _particleTransforms[i].localScale = _particleScaleNormal;
                    _particleSrs[i].color = _colorOutOfReach;
                }
                else if (partInMeasure)
                {
                    _particleTransforms[i].localScale = _particleScaleMeasure;
                    _particleSrs[i].color = partInnerColor;
                }
                else
                {
                    _particleTransforms[i].localScale = _particleScaleNormal;
                    _particleSrs[i].color = partOuterColor;
                }
            }

            // 4. 離した瞬間の輪の更新 (Released 後 0.35秒で scale 80->520, α 0.7->0)
            if (_scriptState == DemoState.Released)
            {
                if (_stateTimer < 0.35)
                {
                    _releaseRingSr.enabled = true;
                    float ringProgress = Mathf.Clamp01((float)(_stateTimer / 0.35));
                    float ringScale = Mathf.Lerp(80f, 520f, ringProgress);
                    _releaseRingTransform.localScale = new Vector3(ringScale, ringScale, 1f);
                    _releaseRingSr.color = new Color(1f, 1f, 1f, Mathf.Lerp(0.7f, 0f, ringProgress));
                }
                else
                {
                    _releaseRingSr.enabled = false;
                }
            }
            else
            {
                _releaseRingSr.enabled = false;
            }
        }

        private void OnDestroy()
        {
            if (_circleSprite != null)
            {
                if (_circleSprite.texture != null)
                {
                    Destroy(_circleSprite.texture);
                }
                Destroy(_circleSprite);
                _circleSprite = null;
            }

            if (_solidSprite != null)
            {
                if (_solidSprite.texture != null)
                {
                    Destroy(_solidSprite.texture);
                }
                Destroy(_solidSprite);
                _solidSprite = null;
            }
        }
    }
}
