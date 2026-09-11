using System;
using UnityEngine;
using BangsEdge.Simulation;

namespace BangsEdge.Game
{
    /// <summary>
    /// 盤面背景の遠い星空と重力レンズ歪み演出を描画するコンポーネント。
    /// 画面振動の影響を受けず、集積時の危険度やビッグバンに応じて背景の星々が歪む。
    /// </summary>
    public sealed class StarfieldRenderer : MonoBehaviour
    {
        private struct StarView
        {
            public float originX;
            public float originY;
            public float baseSize;
            public float baseAlpha;
            public Color color;
            public float frequency;
            public float phase;
            public float driftFrequency;
            public float driftPhase;
            public bool hasGlow;

            public Transform rootTransform;
            public Transform coreTransform;
            public SpriteRenderer coreSr;
            public Transform glowTransform;
            public SpriteRenderer glowSr;
        }

        private struct ShootingStar
        {
            public bool active;
            public float posX;
            public float posY;
            public float vx;
            public float vy;
            public float elapsedSec;
            public float historyAccumSec;
            public Vector3[] history;
            public Vector3[] primaryPositions;
            public Vector3[] secondaryPositions;
            public LineRenderer lineRenderer;
            public Transform headTransform;
            public SpriteRenderer headSr;
            public LineRenderer secondaryLineRenderer;
            public Transform secondaryHeadTransform;
            public SpriteRenderer secondaryHeadSr;
        }

        // 420個では歪む範囲に入る星が数個しかなく、レンズが見て取れなかったので増やした
        private const int STAR_COUNT = 1000;

        // アインシュタイン半径の最大値 (px)
        private const float LENS_MAX_RADIUS = 230f;

        private const int MAX_SHOOTING_STARS = 3;
        private const int SHOOTING_STAR_POINTS = 24;

        // 星ごとの青白色パレット
        private static readonly Color[] StarColors = new Color[]
        {
            new Color(207f / 255f, 224f / 255f, 255f / 255f),
            new Color(232f / 255f, 238f / 255f, 255f / 255f),
            new Color(188f / 255f, 212f / 255f, 255f / 255f)
        };

        private static readonly Color ShootingStarBaseColor = new Color(232f / 255f, 238f / 255f, 255f / 255f);

        private Texture2D _starTexture;
        private Sprite _starSprite;
        private StarView[] _stars;

        // 描画専用乱数
        private System.Random _rng;

        // 重力レンズ状態
        private float _s = 0f;
        private bool _releaseActive = false;
        private float _releaseTime = 0f;
        private float _releaseStartS = 0f;
        private GameState _prevGameState = GameState.Ready;
        private float _lensCenterX = GameConfig.LOGICAL_WIDTH * 0.5f;
        private float _lensCenterY = GameConfig.LOGICAL_HEIGHT * 0.5f;
        private float _thetaE = 0f;
        private bool _lensPositive = false;

        // 流れ星状態
        private ShootingStar[] _shootingStars;
        private float _shootingStarTimer = 0f;

        /// <summary>
        /// 星空スプライトと星オブジェクトの初期化を行う。
        /// </summary>
        public void Initialize(Material sharedSpriteMaterial)
        {
            _starSprite = CreateStarSprite();

            // 起動ごとに違う星空にするため Environment.TickCount をシードとして使用 (System.Random 1つのみ)
            _rng = new System.Random(Environment.TickCount);

            _stars = new StarView[STAR_COUNT];

            for (int i = 0; i < STAR_COUNT; i++)
            {
                float originX = (float)(_rng.NextDouble() * GameConfig.LOGICAL_WIDTH);
                float originY = (float)(_rng.NextDouble() * GameConfig.LOGICAL_HEIGHT);

                // 等級判定: 暗い星85%、中くらい13%、明るい星2%
                double roll = _rng.NextDouble();
                float baseSize;
                float baseAlpha;
                bool hasGlow = false;

                if (roll < 0.85)
                {
                    // 暗い星 (2.5px・0.25〜0.45 ではほとんど見えなかった)
                    baseSize = 3f;
                    baseAlpha = 0.35f + (float)_rng.NextDouble() * (0.6f - 0.35f);
                }
                else if (roll < 0.98)
                {
                    // 中くらい
                    baseSize = 3.5f;
                    baseAlpha = 0.55f + (float)_rng.NextDouble() * (0.8f - 0.55f);
                }
                else
                {
                    // 明るい星
                    baseSize = 5f;
                    baseAlpha = 0.70f + (float)_rng.NextDouble() * (0.85f - 0.70f);
                    hasGlow = true;
                }

                Color color = StarColors[_rng.Next(3)];
                float frequency = 0.15f + (float)_rng.NextDouble() * (0.6f - 0.15f);
                float phase = (float)(_rng.NextDouble() * Math.PI * 2.0);
                float driftFrequency = 0.055f + (float)_rng.NextDouble() * (0.17f - 0.055f);
                float driftPhase = (float)(_rng.NextDouble() * Math.PI * 2.0);

                // 星のルート GameObject (画面振動で動かないよう StarfieldRenderer の子に配置)
                var rootGo = new GameObject($"Star_{i}");
                rootGo.transform.SetParent(transform, false);
                rootGo.transform.localPosition = new Vector3(originX, -originY, 0f);

                // 星の本体 (大きさを変える部品同士は親子にせず兄弟にする)
                var coreGo = new GameObject("Core");
                coreGo.transform.SetParent(rootGo.transform, false);
                coreGo.transform.localScale = new Vector3(baseSize, baseSize, 1f);

                var coreSr = coreGo.AddComponent<SpriteRenderer>();
                coreSr.sprite = _starSprite;
                coreSr.sharedMaterial = sharedSpriteMaterial;
                coreSr.sortingOrder = -5;
                coreSr.color = new Color(color.r, color.g, color.b, baseAlpha);

                Transform glowTransform = null;
                SpriteRenderer glowSr = null;

                if (hasGlow)
                {
                    // 明るい星の光のにじみ (本体と兄弟関係、sortingOrder = -6)
                    var glowGo = new GameObject("Glow");
                    glowGo.transform.SetParent(rootGo.transform, false);
                    glowGo.transform.localScale = new Vector3(14f, 14f, 1f);

                    glowSr = glowGo.AddComponent<SpriteRenderer>();
                    glowSr.sprite = _starSprite;
                    glowSr.sharedMaterial = sharedSpriteMaterial;
                    glowSr.sortingOrder = -6;
                    glowSr.color = new Color(color.r, color.g, color.b, 0.12f);
                    glowTransform = glowGo.transform;
                }

                _stars[i] = new StarView
                {
                    originX = originX,
                    originY = originY,
                    baseSize = baseSize,
                    baseAlpha = baseAlpha,
                    color = color,
                    frequency = frequency,
                    phase = phase,
                    driftFrequency = driftFrequency,
                    driftPhase = driftPhase,
                    hasGlow = hasGlow,
                    rootTransform = rootGo.transform,
                    coreTransform = coreGo.transform,
                    coreSr = coreSr,
                    glowTransform = glowTransform,
                    glowSr = glowSr
                };
            }

            // 流れ星オブジェクトとバッファの初期化 (最大3個)
            _shootingStars = new ShootingStar[MAX_SHOOTING_STARS];
            _shootingStarTimer = 10f + (float)_rng.NextDouble() * (25f - 10f);

            for (int i = 0; i < MAX_SHOOTING_STARS; i++)
            {
                // LineRenderer: 画面振動を受けないよう StarfieldRenderer の直下の子に配置
                var lineGo = new GameObject($"ShootingStar_{i}_Line");
                lineGo.transform.SetParent(transform, false);
                lineGo.transform.localPosition = Vector3.zero;

                var lr = lineGo.AddComponent<LineRenderer>();
                lr.useWorldSpace = false;
                lr.sharedMaterial = sharedSpriteMaterial;
                lr.sortingOrder = -4;
                lr.positionCount = SHOOTING_STAR_POINTS;
                lr.loop = false;
                lr.startWidth = 0f;
                lr.endWidth = 2.5f;
                lr.startColor = new Color(ShootingStarBaseColor.r, ShootingStarBaseColor.g, ShootingStarBaseColor.b, 0f);
                lr.endColor = new Color(ShootingStarBaseColor.r, ShootingStarBaseColor.g, ShootingStarBaseColor.b, 0.9f);
                lr.enabled = false;

                // 頭: LineRenderer とは別の兄弟 GameObject
                var headGo = new GameObject($"ShootingStar_{i}_Head");
                headGo.transform.SetParent(transform, false);
                headGo.transform.localScale = new Vector3(7f, 7f, 1f);

                var headSr = headGo.AddComponent<SpriteRenderer>();
                headSr.sprite = _starSprite;
                headSr.sharedMaterial = sharedSpriteMaterial;
                headSr.sortingOrder = -4;
                headSr.color = new Color(ShootingStarBaseColor.r, ShootingStarBaseColor.g, ShootingStarBaseColor.b, 0.9f);
                headSr.enabled = false;

                // 二つ目の像 (Secondary) の LineRenderer: 兄弟 GameObject
                var secLineGo = new GameObject($"ShootingStar_{i}_SecondaryLine");
                secLineGo.transform.SetParent(transform, false);
                secLineGo.transform.localPosition = Vector3.zero;

                var secLr = secLineGo.AddComponent<LineRenderer>();
                secLr.useWorldSpace = false;
                secLr.sharedMaterial = sharedSpriteMaterial;
                secLr.sortingOrder = -4;
                secLr.positionCount = SHOOTING_STAR_POINTS;
                secLr.loop = false;
                secLr.startWidth = 0f;
                secLr.endWidth = 2.5f;
                secLr.startColor = new Color(ShootingStarBaseColor.r, ShootingStarBaseColor.g, ShootingStarBaseColor.b, 0f);
                secLr.endColor = new Color(ShootingStarBaseColor.r, ShootingStarBaseColor.g, ShootingStarBaseColor.b, 0.9f);
                secLr.enabled = false;

                // 二つ目の像 (Secondary) の頭: 兄弟 GameObject
                var secHeadGo = new GameObject($"ShootingStar_{i}_SecondaryHead");
                secHeadGo.transform.SetParent(transform, false);
                secHeadGo.transform.localScale = new Vector3(7f, 7f, 1f);

                var secHeadSr = secHeadGo.AddComponent<SpriteRenderer>();
                secHeadSr.sprite = _starSprite;
                secHeadSr.sharedMaterial = sharedSpriteMaterial;
                secHeadSr.sortingOrder = -4;
                secHeadSr.color = new Color(ShootingStarBaseColor.r, ShootingStarBaseColor.g, ShootingStarBaseColor.b, 0.9f);
                secHeadSr.enabled = false;

                _shootingStars[i] = new ShootingStar
                {
                    active = false,
                    posX = 0f,
                    posY = 0f,
                    vx = 0f,
                    vy = 0f,
                    elapsedSec = 0f,
                    historyAccumSec = 0f,
                    history = new Vector3[SHOOTING_STAR_POINTS],
                    primaryPositions = new Vector3[SHOOTING_STAR_POINTS],
                    secondaryPositions = new Vector3[SHOOTING_STAR_POINTS],
                    lineRenderer = lr,
                    headTransform = headGo.transform,
                    headSr = headSr,
                    secondaryLineRenderer = secLr,
                    secondaryHeadTransform = secHeadGo.transform,
                    secondaryHeadSr = secHeadSr
                };
            }
        }

        /// <summary>
        /// 16x16 の星スプライトを動的生成する。
        /// </summary>
        private Sprite CreateStarSprite()
        {
            const int size = 16;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true)
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
                    // 中心からの距離 d に対し alpha = exp(-d*d/(2*3*3))、RGBは白
                    float alpha = Mathf.Exp(-d * d / (2f * 3f * 3f));
                    colors[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            tex.SetPixels(colors);
            tex.Apply(true);

            _starTexture = tex;
            // pixelsPerUnit = 16 (1ワールド単位 = 16px、localScale がそのまま描画サイズ px になる)
            _starSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 16f);
            return _starSprite;
        }

        /// <summary>
        /// 毎フレームの星空および重力レンズ歪みの描画更新を行う。
        /// </summary>
        public void Render(BangSimulation sim, double nowMs)
        {
            if (sim == null || _stars == null || _shootingStars == null) return;

            // 1. 重力レンズの強さ計算
            float dt = Mathf.Clamp(Time.unscaledDeltaTime, 0f, 0.1f);

            bool enteringBang = (_prevGameState != GameState.Bang && sim.State == GameState.Bang);
            bool leavingAttract = (_prevGameState == GameState.Attracting && sim.State != GameState.Attracting);

            if (enteringBang)
            {
                _s = Mathf.Max(_s, 1.2f);
            }

            if (leavingAttract || enteringBang)
            {
                _releaseActive = true;
                _releaseTime = 0f;
                _releaseStartS = _s;
            }

            if (sim.State == GameState.Attracting)
            {
                float holdSec = Mathf.Max(0f, (float)((nowMs - sim.PressStartTime) / 1000.0));
                float baseTerm = 0.3f * (1f - Mathf.Exp(-holdSec / 4f));
                float dangerNormalized = Mathf.Clamp01(((float)sim.CurrentDangerRatio - 0.2f) / 0.8f);
                float dangerTerm = Mathf.Pow(dangerNormalized, 1.5f);
                float target = 1f - (1f - baseTerm) * (1f - dangerTerm);

                float tau = (target > _s) ? 0.12f : 0.8f;
                _s += (target - _s) * (1f - Mathf.Exp(-dt / tau));
                _releaseActive = false;
            }
            else if (_releaseActive)
            {
                _releaseTime += dt;
                if (_releaseTime < 0.45f)
                {
                    float p = _releaseTime / 0.45f;
                    float e = 0.5f - 0.5f * Mathf.Cos(Mathf.PI * p);
                    _s = Mathf.LerpUnclamped(_releaseStartS, -0.35f * _releaseStartS, e);
                }
                else if (_releaseTime < 1.45f)
                {
                    float p = (_releaseTime - 0.45f) / 1.0f;
                    float oneMinusP = 1f - p;
                    float e = 1f - oneMinusP * oneMinusP * oneMinusP;
                    _s = Mathf.LerpUnclamped(-0.35f * _releaseStartS, 0f, e);
                }
                else
                {
                    _s = 0f;
                    _releaseActive = false;
                }
            }
            else
            {
                _s = 0f;
            }

            _prevGameState = sim.State;
            _s = Mathf.Clamp(_s, -1.5f, 1.5f);

            // レンズ中心の更新 (Attracting 時のみ更新、それ以外は据え置き)
            if (sim.State == GameState.Attracting)
            {
                _lensCenterX = (float)sim.CursorX;
                _lensCenterY = (float)sim.CursorY;
            }

            // レンズパラメータのキャッシュ
            _thetaE = LENS_MAX_RADIUS * Mathf.Abs(_s);
            _lensPositive = (_s >= 0f);

            // 2. 星ごとの描画更新
            float timeSec = (float)(nowMs / 1000.0);
            const float twoPi = Mathf.PI * 2f;

            for (int i = 0; i < _stars.Length; i++)
            {
                var star = _stars[i];
                MapLensPrimary(star.originX, star.originY, out float posX, out float posY, out float mu);

                // 瞬きとドリフト
                float twinkle = 0.75f + 0.25f * Mathf.Sin(twoPi * star.frequency * timeSec + star.phase);
                float drift = 1f + 0.25f * Mathf.Sin(twoPi * star.driftFrequency * timeSec + star.driftPhase);
                float coreAlpha = Mathf.Clamp01(star.baseAlpha * twinkle * drift * mu);

                // 大きさ倍率
                float sizeMult = Mathf.Clamp(Mathf.Sqrt(mu), 0.7f, 1.8f);
                float coreDrawSize = star.baseSize * sizeMult;

                // 座標 (論理 y 下向き -> ワールド -y)
                star.rootTransform.localPosition = new Vector3(posX, -posY, 0f);
                star.coreTransform.localScale = new Vector3(coreDrawSize, coreDrawSize, 1f);
                star.coreSr.color = new Color(star.color.r, star.color.g, star.color.b, coreAlpha);

                if (star.hasGlow)
                {
                    float glowAlpha = Mathf.Clamp01(0.12f * mu);
                    float glowDrawSize = 14f * sizeMult;
                    star.glowTransform.localScale = new Vector3(glowDrawSize, glowDrawSize, 1f);
                    star.glowSr.color = new Color(star.color.r, star.color.g, star.color.b, glowAlpha);
                }
            }

            // 3. 流れ星の更新と描画
            UpdateShootingStars(dt);
        }

        /// <summary>
        /// 重力レンズによる主像の位置と明るさ倍率を計算する。
        /// </summary>
        private void MapLensPrimary(float x, float y, out float outX, out float outY, out float mu)
        {
            float dx = x - _lensCenterX;
            float dy = y - _lensCenterY;
            float b = Mathf.Sqrt(dx * dx + dy * dy);

            if (_thetaE < 0.5f || b < 0.001f)
            {
                outX = x;
                outY = y;
                mu = 1f;
                return;
            }

            float u = b / _thetaE;
            float muPos = ((u * u + 2f) / (u * Mathf.Sqrt(u * u + 4f)) + 1f) * 0.5f;

            float theta;
            if (_lensPositive)
            {
                theta = (b + Mathf.Sqrt(b * b + 4f * _thetaE * _thetaE)) * 0.5f;
                mu = Mathf.Clamp(muPos, 1f, 3f);
            }
            else
            {
                theta = 2f * b * b / (b + Mathf.Sqrt(b * b + 4f * _thetaE * _thetaE));
                mu = Mathf.Clamp(1f / muPos, 0.4f, 1f);
            }

            float ratio = theta / b;
            outX = _lensCenterX + dx * ratio;
            outY = _lensCenterY + dy * ratio;
        }

        /// <summary>
        /// 重力レンズによる二つ目の像 (中心の反対側に現れる暗い像) の位置と明るさ倍率を計算する。
        /// </summary>
        private bool MapLensSecondary(float x, float y, out float outX, out float outY, out float muSecondary)
        {
            if (!_lensPositive || _thetaE < 0.5f)
            {
                outX = x;
                outY = y;
                muSecondary = 0f;
                return false;
            }

            float dx = x - _lensCenterX;
            float dy = y - _lensCenterY;
            float b = Mathf.Sqrt(dx * dx + dy * dy);

            if (b < 0.001f)
            {
                outX = x;
                outY = y;
                muSecondary = 0f;
                return false;
            }

            float thetaMinus = (b - Mathf.Sqrt(b * b + 4f * _thetaE * _thetaE)) * 0.5f;
            float ratio = thetaMinus / b;
            outX = _lensCenterX + dx * ratio;
            outY = _lensCenterY + dy * ratio;

            float u = b / _thetaE;
            muSecondary = ((u * u + 2f) / (u * Mathf.Sqrt(u * u + 4f)) - 1f) * 0.5f;
            return true;
        }

        /// <summary>
        /// 流れ星の出現、物理移動、履歴更新、描画を行う。
        /// </summary>
        private void UpdateShootingStars(float dt)
        {
            if (_rng == null || _shootingStars == null) return;

            // 出現タイマー更新
            _shootingStarTimer -= dt;
            if (_shootingStarTimer <= 0f)
            {
                _shootingStarTimer = 10f + (float)_rng.NextDouble() * (25f - 10f);

                for (int i = 0; i < MAX_SHOOTING_STARS; i++)
                {
                    if (!_shootingStars[i].active)
                    {
                        SpawnShootingStar(i);
                        break;
                    }
                }
            }

            const float historyInterval = 1f / 120f;

            for (int i = 0; i < MAX_SHOOTING_STARS; i++)
            {
                if (!_shootingStars[i].active) continue;

                float posX = _shootingStars[i].posX;
                float posY = _shootingStars[i].posY;
                float vx = _shootingStars[i].vx;
                float vy = _shootingStars[i].vy;

                // 等速直線移動 (サブステップなし)
                posX += vx * dt;
                posY += vy * dt;

                float elapsed = _shootingStars[i].elapsedSec + dt;

                // 消える条件: 盤面から 200px 以上外に出た (0.5秒以上のときのみ判定)、または経過時間 6秒 (歪ませる前の位置で判定)
                bool isOutside = (posX < -200f || posX > GameConfig.LOGICAL_WIDTH + 200f ||
                                  posY < -200f || posY > GameConfig.LOGICAL_HEIGHT + 200f);
                if ((elapsed >= 0.5f && isOutside) || elapsed >= 6f)
                {
                    _shootingStars[i].active = false;
                    _shootingStars[i].lineRenderer.enabled = false;
                    _shootingStars[i].headSr.enabled = false;
                    _shootingStars[i].secondaryLineRenderer.enabled = false;
                    _shootingStars[i].secondaryHeadSr.enabled = false;
                    continue;
                }

                // 尾の履歴 (1/120 秒ごとに現在位置を記録、歪ませる前の論理座標 (x, y))
                float accum = _shootingStars[i].historyAccumSec + dt;
                var historyArr = _shootingStars[i].history;

                while (accum >= historyInterval)
                {
                    accum -= historyInterval;
                    for (int k = 0; k < SHOOTING_STAR_POINTS - 1; k++)
                    {
                        historyArr[k] = historyArr[k + 1];
                    }
                    historyArr[SHOOTING_STAR_POINTS - 1] = new Vector3(posX, posY, 0f);
                }

                // フェード (最後の 0.5秒)
                float fade = 1f;
                if (elapsed > 5.5f)
                {
                    fade = Mathf.Clamp01((6f - elapsed) / 0.5f);
                }

                // 状態書き戻し
                _shootingStars[i].posX = posX;
                _shootingStars[i].posY = posY;
                _shootingStars[i].elapsedSec = elapsed;
                _shootingStars[i].historyAccumSec = accum;

                // 主像の描画更新
                var primaryPositions = _shootingStars[i].primaryPositions;
                for (int k = 0; k < SHOOTING_STAR_POINTS; k++)
                {
                    Vector3 h = historyArr[k];
                    MapLensPrimary(h.x, h.y, out float ox, out float oy, out float _);
                    primaryPositions[k] = new Vector3(ox, -oy, 0f);
                }

                MapLensPrimary(posX, posY, out float headPrimaryX, out float headPrimaryY, out float headMu);
                float primaryMult = Mathf.Clamp(Mathf.Sqrt(headMu), 0.7f, 1.8f);
                float primaryAlpha = 0.9f * fade;

                _shootingStars[i].lineRenderer.SetPositions(primaryPositions);
                _shootingStars[i].lineRenderer.startWidth = 0f;
                _shootingStars[i].lineRenderer.endWidth = 2.5f * primaryMult;
                _shootingStars[i].lineRenderer.startColor = new Color(ShootingStarBaseColor.r, ShootingStarBaseColor.g, ShootingStarBaseColor.b, 0f);
                _shootingStars[i].lineRenderer.endColor = new Color(ShootingStarBaseColor.r, ShootingStarBaseColor.g, ShootingStarBaseColor.b, primaryAlpha);
                _shootingStars[i].lineRenderer.enabled = true;

                _shootingStars[i].headTransform.localPosition = new Vector3(headPrimaryX, -headPrimaryY, 0f);
                _shootingStars[i].headTransform.localScale = new Vector3(7f * primaryMult, 7f * primaryMult, 1f);
                _shootingStars[i].headSr.color = new Color(ShootingStarBaseColor.r, ShootingStarBaseColor.g, ShootingStarBaseColor.b, primaryAlpha);
                _shootingStars[i].headSr.enabled = true;

                // 二つ目の像 (Secondary) の描画更新
                bool hasSecondary = MapLensSecondary(posX, posY, out float secHeadX, out float secHeadY, out float muSecondary);
                if (!hasSecondary)
                {
                    _shootingStars[i].secondaryLineRenderer.enabled = false;
                    _shootingStars[i].secondaryHeadSr.enabled = false;
                }
                else
                {
                    float a2 = 0.9f * fade * Mathf.Clamp01(muSecondary);
                    if (a2 < 0.02f)
                    {
                        _shootingStars[i].secondaryLineRenderer.enabled = false;
                        _shootingStars[i].secondaryHeadSr.enabled = false;
                    }
                    else
                    {
                        var secondaryPositions = _shootingStars[i].secondaryPositions;
                        Vector3 prevPos = new Vector3(secHeadX, -secHeadY, 0f);

                        for (int k = 0; k < SHOOTING_STAR_POINTS; k++)
                        {
                            Vector3 h = historyArr[k];
                            if (MapLensSecondary(h.x, h.y, out float sx, out float sy, out float _))
                            {
                                prevPos = new Vector3(sx, -sy, 0f);
                            }
                            secondaryPositions[k] = prevPos;
                        }

                        float secMult = Mathf.Clamp(Mathf.Sqrt(muSecondary), 0.5f, 1.5f);

                        _shootingStars[i].secondaryHeadTransform.localPosition = new Vector3(secHeadX, -secHeadY, 0f);
                        _shootingStars[i].secondaryHeadTransform.localScale = new Vector3(7f * secMult, 7f * secMult, 1f);
                        _shootingStars[i].secondaryHeadSr.color = new Color(ShootingStarBaseColor.r, ShootingStarBaseColor.g, ShootingStarBaseColor.b, a2);
                        _shootingStars[i].secondaryHeadSr.enabled = true;

                        _shootingStars[i].secondaryLineRenderer.SetPositions(secondaryPositions);
                        _shootingStars[i].secondaryLineRenderer.startWidth = 0f;
                        _shootingStars[i].secondaryLineRenderer.endWidth = 2.5f * secMult;
                        _shootingStars[i].secondaryLineRenderer.startColor = new Color(ShootingStarBaseColor.r, ShootingStarBaseColor.g, ShootingStarBaseColor.b, 0f);
                        _shootingStars[i].secondaryLineRenderer.endColor = new Color(ShootingStarBaseColor.r, ShootingStarBaseColor.g, ShootingStarBaseColor.b, a2);
                        _shootingStars[i].secondaryLineRenderer.enabled = true;
                    }
                }
            }
        }

        /// <summary>
        /// 流れ星を1つ出現させる。
        /// </summary>
        private void SpawnShootingStar(int index)
        {
            // 出発点の選定 (上辺・左辺・右辺の外側)
            int edge = _rng.Next(3);
            float startX;
            float startY;

            if (edge == 0)
            {
                // 上辺の外 (x: -100〜2020, y: -60)
                startX = -100f + (float)_rng.NextDouble() * (2020f - (-100f));
                startY = -60f;
            }
            else if (edge == 1)
            {
                // 左辺の外 (x: -60, y: -100〜1180)
                startX = -60f;
                startY = -100f + (float)_rng.NextDouble() * (1180f - (-100f));
            }
            else
            {
                // 右辺の外 (x: 1980, y: -100〜1180)
                startX = 1980f;
                startY = -100f + (float)_rng.NextDouble() * (1180f - (-100f));
            }

            // 盤面内の目標点 (x: 300〜1620, y: 200〜880)
            float targetX = 300f + (float)_rng.NextDouble() * (1620f - 300f);
            float targetY = 200f + (float)_rng.NextDouble() * (880f - 200f);

            float dirX = targetX - startX;
            float dirY = targetY - startY;
            float dist = Mathf.Sqrt(dirX * dirX + dirY * dirY);
            if (dist > 0.0001f)
            {
                dirX /= dist;
                dirY /= dist;
            }
            else
            {
                dirX = 1f;
                dirY = 0f;
            }

            float speed = 900f + (float)_rng.NextDouble() * (1400f - 900f);
            float vx = dirX * speed;
            float vy = dirY * speed;

            _shootingStars[index].active = true;
            _shootingStars[index].posX = startX;
            _shootingStars[index].posY = startY;
            _shootingStars[index].vx = vx;
            _shootingStars[index].vy = vy;
            _shootingStars[index].elapsedSec = 0f;
            _shootingStars[index].historyAccumSec = 0f;

            // 履歴には歪ませる前の論理座標 (x, y) を記録
            Vector3 startPos = new Vector3(startX, startY, 0f);
            var historyArr = _shootingStars[index].history;
            for (int k = 0; k < SHOOTING_STAR_POINTS; k++)
            {
                historyArr[k] = startPos;
            }

            _shootingStars[index].lineRenderer.enabled = false;
            _shootingStars[index].headSr.enabled = false;
            _shootingStars[index].secondaryLineRenderer.enabled = false;
            _shootingStars[index].secondaryHeadSr.enabled = false;
        }

        private void OnDestroy()
        {
            if (_starSprite != null)
            {
                Destroy(_starSprite);
                _starSprite = null;
            }
            if (_starTexture != null)
            {
                Destroy(_starTexture);
                _starTexture = null;
            }
        }
    }
}
