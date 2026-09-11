using System;
using UnityEngine;
using BangsEdge.Simulation;

namespace BangsEdge.Game
{
    /// <summary>
    /// 盤面の描画を担当するコンポーネント。
    /// 粒子、集積円、測定円ターゲット、フラッシュ、画面振動、盤面枠を管理する。
    /// </summary>
    public sealed class GameRenderer : MonoBehaviour
    {
        private struct ParticleView
        {
            public Transform transform;
            public SpriteRenderer mainSr;
            public SpriteRenderer glowSr;
        }

        // 共通マテリアル
        private Material _sharedSpriteMaterial;

        // 生成スプライト
        private Sprite _circleSprite;
        private Sprite _measureTargetSprite;
        private Sprite _whiteSolidSprite;

        // 階層オブジェクト
        private GameObject _worldContainer;
        private ParticleView[] _particleViews;

        // 集積円 (塗り、輪郭、発光輪郭)
        private SpriteRenderer _attractFillSr;
        private LineRenderer _attractLine;
        private LineRenderer _attractGlowLine;

        // 測定ターゲット (点線測定円 & 中心十字)
        private SpriteRenderer _measureTargetSr;

        // フラッシュ
        private SpriteRenderer _flashSr;

        // 盤面枠
        private LineRenderer _frameLine;

        // 星空背景 (重力レンズ歪み演出)
        private StarfieldRenderer _starfield;

        // LineRenderer 用の事前確保バッファ (GC Alloc ゼロ)
        private const int CIRCLE_VERTICES = 128;
        private float[] _unitCircleCos;
        private float[] _unitCircleSin;
        private Vector3[] _circlePositions;

        // 事前定義色
        private readonly Color _colorOutOfReach = new Color(100f / 255f, 116f / 255f, 139f / 255f, 0.25f); // #64748b 25%
        private readonly Color _colorFrame = new Color(1f, 1f, 1f, 0.12f);

        public void Initialize()
        {
            // 1. 共通マテリアルの取得 (SpriteRenderer の既定マテリアルを使い回す)
            var dummy = new GameObject("DummySpriteRenderer");
            var dummySr = dummy.AddComponent<SpriteRenderer>();
            _sharedSpriteMaterial = dummySr.sharedMaterial;
            Destroy(dummy);

            // 2. スプライトの動的生成
            _circleSprite = CreateCircleSprite(64, 29.5f);
            _measureTargetSprite = CreateMeasureTargetSprite();
            _whiteSolidSprite = CreateSolidSprite();

            // 3. 画面振動対象となるワールドコンテナの生成
            _worldContainer = new GameObject("WorldContainer");
            _worldContainer.transform.SetParent(transform, false);

            // 4. LineRenderer 用バッファの初期化
            _unitCircleCos = new float[CIRCLE_VERTICES];
            _unitCircleSin = new float[CIRCLE_VERTICES];
            _circlePositions = new Vector3[CIRCLE_VERTICES];
            for (int i = 0; i < CIRCLE_VERTICES; i++)
            {
                float rad = i * (Mathf.PI * 2f / CIRCLE_VERTICES);
                _unitCircleCos[i] = Mathf.Cos(rad);
                _unitCircleSin[i] = Mathf.Sin(rad);
            }

            // 5. 集積円の構築
            BuildAttractField();

            // 6. 測定円ターゲットの構築
            BuildMeasureTarget();

            // 7. 粒子オブジェクトプールの構築 (200個事前生成)
            BuildParticles();

            // 8. フラッシュの構築
            BuildFlash();

            // 9. 盤面枠の構築 (画面振動で動かないよう _worldContainer の外に配置)
            BuildBoardFrame();

            // 10. 星空背景の構築 (画面振動で動かないよう外側に配置)
            _starfield = gameObject.AddComponent<StarfieldRenderer>();
            _starfield.Initialize(_sharedSpriteMaterial);
        }

        private void BuildAttractField()
        {
            var attractRoot = new GameObject("AttractField");
            attractRoot.transform.SetParent(_worldContainer.transform, false);

            // 薄い塗り
            var fillGo = new GameObject("AttractFill");
            fillGo.transform.SetParent(attractRoot.transform, false);
            _attractFillSr = fillGo.AddComponent<SpriteRenderer>();
            _attractFillSr.sprite = _circleSprite;
            _attractFillSr.sharedMaterial = _sharedSpriteMaterial;
            _attractFillSr.sortingOrder = 0;
            _attractFillSr.enabled = false;

            // 発光輪郭 LineRenderer
            var glowLineGo = new GameObject("AttractGlowLine");
            glowLineGo.transform.SetParent(attractRoot.transform, false);
            _attractGlowLine = glowLineGo.AddComponent<LineRenderer>();
            SetupLineRenderer(_attractGlowLine, CIRCLE_VERTICES, 1);
            _attractGlowLine.enabled = false;

            // 通常輪郭 LineRenderer
            var lineGo = new GameObject("AttractLine");
            lineGo.transform.SetParent(attractRoot.transform, false);
            _attractLine = lineGo.AddComponent<LineRenderer>();
            SetupLineRenderer(_attractLine, CIRCLE_VERTICES, 2);
            _attractLine.enabled = false;
        }

        private void BuildMeasureTarget()
        {
            var targetGo = new GameObject("MeasureTarget");
            targetGo.transform.SetParent(_worldContainer.transform, false);
            _measureTargetSr = targetGo.AddComponent<SpriteRenderer>();
            _measureTargetSr.sprite = _measureTargetSprite;
            _measureTargetSr.sharedMaterial = _sharedSpriteMaterial;
            // JS draws the fields (including this target) before the particles, so it sits beneath them
            _measureTargetSr.sortingOrder = 3;
            _measureTargetSr.enabled = false;
        }

        private void BuildParticles()
        {
            var particlesParent = new GameObject("Particles");
            particlesParent.transform.SetParent(_worldContainer.transform, false);

            _particleViews = new ParticleView[GameConfig.TOTAL_PARTICLES];
            for (int i = 0; i < GameConfig.TOTAL_PARTICLES; i++)
            {
                var pGo = new GameObject($"Particle_{i}");
                pGo.transform.SetParent(particlesParent.transform, false);

                // 発光層 (下層: sortingOrder = 5)
                var glowGo = new GameObject("Glow");
                glowGo.transform.SetParent(pGo.transform, false);
                var glowSr = glowGo.AddComponent<SpriteRenderer>();
                glowSr.sprite = _circleSprite;
                glowSr.sharedMaterial = _sharedSpriteMaterial;
                glowSr.sortingOrder = 5;
                glowSr.enabled = false;

                // メイン粒子 (射程外 / 通常 / 測定円内: sortingOrder = 4, 6, 7)
                // 発光層と兄弟にする。pGo 自体に置いて拡大すると、子の発光層がその拡大率を引き継いで
                // 半径が数倍になる(実際に CRITICAL で 6.7px のはずが約29px になっていた)
                var coreGo = new GameObject("Core");
                coreGo.transform.SetParent(pGo.transform, false);
                var mainSr = coreGo.AddComponent<SpriteRenderer>();
                mainSr.sprite = _circleSprite;
                mainSr.sharedMaterial = _sharedSpriteMaterial;
                mainSr.sortingOrder = 6;

                _particleViews[i] = new ParticleView
                {
                    transform = pGo.transform,
                    mainSr = mainSr,
                    glowSr = glowSr
                };
            }
        }

        private void BuildFlash()
        {
            var flashGo = new GameObject("Flash");
            flashGo.transform.SetParent(_worldContainer.transform, false);
            _flashSr = flashGo.AddComponent<SpriteRenderer>();
            _flashSr.sprite = _whiteSolidSprite;
            _flashSr.sharedMaterial = _sharedSpriteMaterial;
            _flashSr.sortingOrder = 10;
            flashGo.transform.localPosition = new Vector3(GameConfig.LOGICAL_WIDTH * 0.5f, -GameConfig.LOGICAL_HEIGHT * 0.5f, 0f);
            flashGo.transform.localScale = new Vector3(GameConfig.LOGICAL_WIDTH, GameConfig.LOGICAL_HEIGHT, 1f);
            _flashSr.enabled = false;
        }

        private void BuildBoardFrame()
        {
            var frameGo = new GameObject("BoardFrame");
            frameGo.transform.SetParent(transform, false); // 画面振動しないよう外側に配置
            _frameLine = frameGo.AddComponent<LineRenderer>();
            SetupLineRenderer(_frameLine, 4, 20);
            _frameLine.startWidth = 2f;
            _frameLine.endWidth = 2f;
            _frameLine.startColor = _colorFrame;
            _frameLine.endColor = _colorFrame;

            // 盤面内側 1px の矩形: (1, -1) -> (1919, -1) -> (1919, -1079) -> (1, -1079)
            Vector3[] framePos = new Vector3[4]
            {
                new Vector3(1f, -1f, 0f),
                new Vector3(GameConfig.LOGICAL_WIDTH - 1f, -1f, 0f),
                new Vector3(GameConfig.LOGICAL_WIDTH - 1f, -(GameConfig.LOGICAL_HEIGHT - 1f), 0f),
                new Vector3(1f, -(GameConfig.LOGICAL_HEIGHT - 1f), 0f)
            };
            _frameLine.SetPositions(framePos);
            _frameLine.enabled = true;
        }

        private void SetupLineRenderer(LineRenderer lr, int count, int sortingOrder)
        {
            lr.sharedMaterial = _sharedSpriteMaterial;
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.positionCount = count;
            lr.sortingOrder = sortingOrder;
            lr.textureMode = LineTextureMode.Stretch;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
        }

        /// <summary>
        /// 毎フレームの描画更新を行う。
        /// </summary>
        public void Render(BangSimulation sim, double nowMs)
        {
            if (sim == null) return;

            // 星空背景の更新 (重力レンズ歪み)
            _starfield.Render(sim, nowMs);

            // 1. 画面振動
            if (sim.ShakeMagnitude > 0.0)
            {
                // シミュレーション乱数系列を狂わせないため、描画用には UnityEngine.Random を使用
                float sx = (UnityEngine.Random.value * 2f - 1f) * (float)sim.ShakeMagnitude;
                float sy = (UnityEngine.Random.value * 2f - 1f) * (float)sim.ShakeMagnitude;
                _worldContainer.transform.localPosition = new Vector3(sx, -sy, 0f);
            }
            else
            {
                _worldContainer.transform.localPosition = Vector3.zero;
            }

            // 2. 集積円 & 測定円ターゲットの描画 (吸引中のみ)
            if (sim.State == GameState.Attracting)
            {
                RenderAttractField(sim, nowMs);
                RenderMeasureTarget(sim);
            }
            else
            {
                _attractFillSr.enabled = false;
                _attractLine.enabled = false;
                _attractGlowLine.enabled = false;
                _measureTargetSr.enabled = false;
            }

            // 3. 粒子の描画
            RenderParticles(sim);

            // 4. フラッシュの描画
            if (sim.FlashOpacity > 0.0)
            {
                _flashSr.enabled = true;
                _flashSr.color = new Color(1f, 1f, 1f, Mathf.Min(1f, (float)sim.FlashOpacity));
            }
            else
            {
                _flashSr.enabled = false;
            }
        }

        private void RenderAttractField(BangSimulation sim, double nowMs)
        {
            double t = Math.Max(0.0, (nowMs - sim.PressStartTime) / 1000.0);
            double currentR = GameConfig.R_MIN + (sim.EffectiveRMax - GameConfig.R_MIN) * (1.0 - Math.Exp(-t / GameConfig.TAU_R));

            // 色・線幅・発光の設定 (drawFields の分岐に厳密準拠)
            Color circleColor = new Color(1f, 1f, 1f, 0.4f);
            float circleWidth = 1.5f;
            Color glowColor = Color.clear;
            float glowBlur = 0f;

            if (sim.Stage == DangerStage.Warm)
            {
                circleColor = new Color(1f, 234f / 255f, 0f, 0.7f);
                circleWidth = 2.5f;
                glowColor = new Color(1f, 234f / 255f, 0f, 0.4f);
                glowBlur = 6f;
            }
            else if (sim.Stage == DangerStage.Hot)
            {
                circleColor = new Color(1f, 59f / 255f, 48f / 255f, 0.85f);
                circleWidth = 3.5f;
                glowColor = new Color(1f, 59f / 255f, 48f / 255f, 0.6f);
                glowBlur = 12f;
            }
            else if (sim.Stage == DangerStage.Critical)
            {
                float pulse = 0.8f + 0.2f * (float)Math.Sin(nowMs * 0.025);
                circleColor = new Color(1f, 1f, 1f, pulse);
                circleWidth = 5.0f;
                glowColor = new Color(1f, 34f / 255f, 0f, 0.9f);
                glowBlur = 20f;
            }

            // 塗り
            Color fillColor = (sim.Stage == DangerStage.Critical)
                ? new Color(1f, 50f / 255f, 0f, 0.08f)
                : new Color(1f, 1f, 1f, 0.03f);

            float cx = (float)sim.CursorX;
            float cy = (float)(-sim.CursorY);
            float r = (float)currentR;

            _attractFillSr.transform.localPosition = new Vector3(cx, cy, 0f);
            _attractFillSr.transform.localScale = new Vector3(r * 2f, r * 2f, 1f);
            _attractFillSr.color = fillColor;
            _attractFillSr.enabled = true;

            // 円周頂点の計算 (ローカル座標)
            for (int i = 0; i < CIRCLE_VERTICES; i++)
            {
                _circlePositions[i].x = cx + _unitCircleCos[i] * r;
                _circlePositions[i].y = cy + _unitCircleSin[i] * r;
                _circlePositions[i].z = 0f;
            }

            // 発光輪郭
            if (glowBlur > 0f)
            {
                _attractGlowLine.enabled = true;
                _attractGlowLine.startWidth = circleWidth + glowBlur;
                _attractGlowLine.endWidth = circleWidth + glowBlur;
                _attractGlowLine.startColor = glowColor;
                _attractGlowLine.endColor = glowColor;
                _attractGlowLine.SetPositions(_circlePositions);
            }
            else
            {
                _attractGlowLine.enabled = false;
            }

            // 通常輪郭
            _attractLine.enabled = true;
            _attractLine.startWidth = circleWidth;
            _attractLine.endWidth = circleWidth;
            _attractLine.startColor = circleColor;
            _attractLine.endColor = circleColor;
            _attractLine.SetPositions(_circlePositions);
        }

        private void RenderMeasureTarget(BangSimulation sim)
        {
            _measureTargetSr.enabled = true;
            _measureTargetSr.transform.localPosition = new Vector3((float)sim.CursorX, (float)(-sim.CursorY), 0f);
        }

        private void RenderParticles(BangSimulation sim)
        {
            // 状態に応じた色とパラメータの決定 (drawParticles に準拠)
            Color outerColor = new Color(203f / 255f, 213f / 255f, 225f / 255f, 1f); // #cbd5e1
            Color innerColor = Color.white;                                            // #ffffff
            float glowBlur = 0f;
            Color glowColor = Color.clear;

            if (sim.State == GameState.Bang)
            {
                outerColor = new Color(1f, 51f / 255f, 0f, 1f);                     // #ff3300
                innerColor = new Color(1f, 245f / 255f, 204f / 255f, 1f);             // #fff5cc
                glowBlur = 8f;
                glowColor = new Color(1f, 102f / 255f, 0f, 1f);                     // #ff6600
            }
            else if (sim.Stage == DangerStage.Warm)
            {
                outerColor = new Color(1f, 234f / 255f, 0f, 1f);                     // #ffea00
                innerColor = new Color(1f, 249f / 255f, 166f / 255f, 1f);             // #fff9a6
            }
            else if (sim.Stage == DangerStage.Hot)
            {
                outerColor = new Color(1f, 59f / 255f, 48f / 255f, 1f);              // #ff3b30
                innerColor = new Color(1f, 163f / 255f, 158f / 255f, 1f);             // #ffa39e
                glowBlur = 4f;
                glowColor = new Color(1f, 59f / 255f, 48f / 255f, 1f);
            }
            else if (sim.Stage == DangerStage.Critical)
            {
                outerColor = new Color(1f, 51f / 255f, 0f, 1f);                     // #ff3300
                innerColor = Color.white;
                glowBlur = 10f;
                glowColor = new Color(1f, 0f, 51f / 255f, 1f);                      // #ff0033
            }

            float haloR = 2.2f + glowBlur * 0.45f;
            float haloDiameter = haloR * 2f;
            Color haloColorWithAlpha = new Color(glowColor.r, glowColor.g, glowColor.b, 0.3f);

            // 粒子ごとの描画更新
            for (int i = 0; i < GameConfig.TOTAL_PARTICLES; i++)
            {
                var view = _particleViews[i];
                float px = (float)sim.X[i];
                float py = (float)(-sim.Y[i]);
                bool outOfReach = sim.OutOfReach[i];
                bool inMeasure = sim.InMeasure[i];

                view.transform.localPosition = new Vector3(px, py, 0f);

                // 発光層 (glowBlur > 0 かつ 射程内)
                if (glowBlur > 0f && !outOfReach)
                {
                    view.glowSr.enabled = true;
                    view.glowSr.transform.localScale = new Vector3(haloDiameter, haloDiameter, 1f);
                    view.glowSr.color = haloColorWithAlpha;
                }
                else
                {
                    view.glowSr.enabled = false;
                }

                // メイン粒子 (重なり順: 測定円ターゲット: 3 < 射程外: 4 < 発光層: 5 < 通常: 6 < 測定円内: 7)
                if (outOfReach)
                {
                    view.mainSr.transform.localScale = new Vector3(4.4f, 4.4f, 1f); // 半径 2.2
                    view.mainSr.color = _colorOutOfReach;
                    view.mainSr.sortingOrder = 4;
                }
                else if (inMeasure)
                {
                    view.mainSr.transform.localScale = new Vector3(6.4f, 6.4f, 1f); // 半径 3.2
                    view.mainSr.color = innerColor;
                    view.mainSr.sortingOrder = 7;
                }
                else
                {
                    view.mainSr.transform.localScale = new Vector3(4.4f, 4.4f, 1f); // 半径 2.2
                    view.mainSr.color = outerColor;
                    view.mainSr.sortingOrder = 6;
                }
            }
        }

        /// <summary>
        /// 縁をアンチエイリアスで滑らかにした円スプライトを生成する。
        /// 直径 1.0 ワールド単位相当 (半径 0.5) になるよう pixelsPerUnit を設定する。
        /// </summary>
        private static Sprite CreateCircleSprite(int size, float r)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            float cx = size * 0.5f;
            float cy = size * 0.5f;
            Color[] colors = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) - cx;
                    float dy = (y + 0.5f) - cy;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);

                    float alpha;
                    if (d <= r - 0.75f)
                    {
                        alpha = 1f;
                    }
                    else if (d >= r + 0.75f)
                    {
                        alpha = 0f;
                    }
                    else
                    {
                        alpha = Mathf.SmoothStep(1f, 0f, (d - (r - 0.75f)) / 1.5f);
                    }

                    colors[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            tex.SetPixels(colors);
            tex.Apply();

            // ワールド単位で直径 1.0 になるよう pixelsPerUnit = r * 2
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), r * 2f);
        }

        /// <summary>
        /// 半径40pxの点線測定円と中心十字レティクルを描画したスプライトを生成する。
        /// </summary>
        private static Sprite CreateMeasureTargetSprite()
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            float cx = size * 0.5f;
            float cy = size * 0.5f;
            Color[] colors = new Color[size * size];

            // 測定円定数
            const float targetR = (float)GameConfig.R_MEASURE; // 40px
            const float halfLineWidth = 0.75f;                 // 線幅 1.5px
            const int dashPeriods = 32;                       // 32周期 (約4px描画、約4px空き)
            const float periodAngle = (Mathf.PI * 2f) / dashPeriods;

            Color baseCyan = new Color(0f, 229f / 255f, 1f, 1f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) - cx;
                    float dy = (y + 0.5f) - cy;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);

                    // 1. 点線円 (4px描いて4px空ける、rgba(0, 229, 255, 0.7))
                    float distToR = Mathf.Abs(d - targetR);
                    float rAlpha = Mathf.Clamp01(1f - (distToR - (halfLineWidth - 0.5f)));

                    float angle = Mathf.Atan2(dy, dx);
                    if (angle < 0f) angle += Mathf.PI * 2f;
                    float phase = (angle % periodAngle) / periodAngle;
                    float dashAlpha = (phase <= 0.5f) ? 1f : 0f;

                    float circleAlpha = rAlpha * dashAlpha * 0.7f;

                    // 2. 中心の十字 (上下左右に5px、rgba(0, 229, 255, 0.5)、線幅1)
                    float absDx = Mathf.Abs(dx);
                    float absDy = Mathf.Abs(dy);

                    float hLine = Mathf.Clamp01(1f - (absDy - 0.25f)) * Mathf.Clamp01(1f - (absDx - 4.5f));
                    float vLine = Mathf.Clamp01(1f - (absDx - 0.25f)) * Mathf.Clamp01(1f - (absDy - 4.5f));
                    float crossAlpha = Mathf.Max(hLine, vLine) * 0.5f;

                    // 合成
                    float finalAlpha = Mathf.Clamp01(circleAlpha + crossAlpha);
                    colors[y * size + x] = new Color(baseCyan.r, baseCyan.g, baseCyan.b, finalAlpha);
                }
            }

            tex.SetPixels(colors);
            tex.Apply();

            // 1論理px = 1ワールド単位
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 1f);
        }

        /// <summary>
        /// フラッシュ用の 1x1 白スプライトを生成する。
        /// </summary>
        private static Sprite CreateSolidSprite()
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            Color[] colors = new Color[4] { Color.white, Color.white, Color.white, Color.white };
            tex.SetPixels(colors);
            tex.Apply();

            return Sprite.Create(tex, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f), 1f);
        }
    }
}
