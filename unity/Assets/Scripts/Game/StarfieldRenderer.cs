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
            public bool hasGlow;

            public Transform rootTransform;
            public Transform coreTransform;
            public SpriteRenderer coreSr;
            public Transform glowTransform;
            public SpriteRenderer glowSr;
        }

        // 420個では歪む範囲に入る星が数個しかなく、レンズが見て取れなかったので増やした
        private const int STAR_COUNT = 1000;

        // アインシュタイン半径の最大値 (px)。110 では粒子の塊とほぼ同じ大きさで輪が見えなかった
        private const float LENS_MAX_RADIUS = 200f;

        // 星ごとの青白色パレット
        private static readonly Color[] StarColors = new Color[]
        {
            new Color(207f / 255f, 224f / 255f, 255f / 255f),
            new Color(232f / 255f, 238f / 255f, 255f / 255f),
            new Color(188f / 255f, 212f / 255f, 255f / 255f)
        };

        private Texture2D _starTexture;
        private Sprite _starSprite;
        private StarView[] _stars;

        // 重力レンズ状態
        private float _s = 0f;
        private GameState _prevGameState = GameState.Ready;
        private float _lensCenterX = GameConfig.LOGICAL_WIDTH * 0.5f;
        private float _lensCenterY = GameConfig.LOGICAL_HEIGHT * 0.5f;

        /// <summary>
        /// 星空スプライトと星オブジェクトの初期化を行う。
        /// </summary>
        public void Initialize(Material sharedSpriteMaterial)
        {
            _starSprite = CreateStarSprite();

            // シミュレーション乱数列や UnityEngine.Random に触れないよう、固定シードの System.Random を使用
            var rng = new System.Random(12345);

            _stars = new StarView[STAR_COUNT];

            for (int i = 0; i < STAR_COUNT; i++)
            {
                float originX = (float)(rng.NextDouble() * GameConfig.LOGICAL_WIDTH);
                float originY = (float)(rng.NextDouble() * GameConfig.LOGICAL_HEIGHT);

                // 等級判定: 暗い星85%、中くらい13%、明るい星2%
                double roll = rng.NextDouble();
                float baseSize;
                float baseAlpha;
                bool hasGlow = false;

                if (roll < 0.85)
                {
                    // 暗い星 (2.5px・0.25〜0.45 ではほとんど見えなかった)
                    baseSize = 3f;
                    baseAlpha = 0.35f + (float)rng.NextDouble() * (0.6f - 0.35f);
                }
                else if (roll < 0.98)
                {
                    // 中くらい
                    baseSize = 3.5f;
                    baseAlpha = 0.55f + (float)rng.NextDouble() * (0.8f - 0.55f);
                }
                else
                {
                    // 明るい星
                    baseSize = 5f;
                    baseAlpha = 0.70f + (float)rng.NextDouble() * (0.85f - 0.70f);
                    hasGlow = true;
                }

                Color color = StarColors[rng.Next(3)];
                float frequency = 0.15f + (float)rng.NextDouble() * (0.6f - 0.15f);
                float phase = (float)(rng.NextDouble() * Math.PI * 2.0);

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
                    hasGlow = hasGlow,
                    rootTransform = rootGo.transform,
                    coreTransform = coreGo.transform,
                    coreSr = coreSr,
                    glowTransform = glowTransform,
                    glowSr = glowSr
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
            if (sim == null || _stars == null) return;

            // 1. 重力レンズの強さ計算
            float dt = Mathf.Clamp(Time.unscaledDeltaTime, 0f, 0.1f);

            float target = 0f;
            if (sim.State == GameState.Attracting)
            {
                float x = Mathf.Clamp01(((float)sim.CurrentDangerRatio - 0.2f) / 0.8f);
                target = Mathf.Pow(x, 1.5f);
            }

            // 指数追従: 上がるとき 0.12秒、下がるとき 0.8秒
            float tau = (target > _s) ? 0.12f : 0.8f;
            _s += (target - _s) * (1f - Mathf.Exp(-dt / tau));

            // ビッグバンの瞬間の跳ね上がり
            if (_prevGameState != GameState.Bang && sim.State == GameState.Bang)
            {
                _s = Mathf.Max(_s, 1.2f);
            }
            _prevGameState = sim.State;

            // レンズ中心の更新 (Attracting 時のみ更新、それ以外は据え置き)
            if (sim.State == GameState.Attracting)
            {
                _lensCenterX = (float)sim.CursorX;
                _lensCenterY = (float)sim.CursorY;
            }

            // アインシュタイン半径 (px)
            float thetaE = LENS_MAX_RADIUS * _s;

            // 2. 星ごとの描画更新
            float timeSec = (float)(nowMs / 1000.0);
            const float twoPi = Mathf.PI * 2f;

            for (int i = 0; i < _stars.Length; i++)
            {
                var star = _stars[i];
                float dx = star.originX - _lensCenterX;
                float dy = star.originY - _lensCenterY;
                float b = Mathf.Sqrt(dx * dx + dy * dy);

                float posX;
                float posY;
                float mu;

                if (thetaE < 0.5f || b < 0.001f)
                {
                    posX = star.originX;
                    posY = star.originY;
                    mu = 1f;
                }
                else
                {
                    float theta = (b + Mathf.Sqrt(b * b + 4f * thetaE * thetaE)) * 0.5f;
                    float ratio = theta / b;
                    posX = _lensCenterX + dx * ratio;
                    posY = _lensCenterY + dy * ratio;

                    float u = b / thetaE;
                    float rawMu = ((u * u + 2f) / (u * Mathf.Sqrt(u * u + 4f)) + 1f) * 0.5f;
                    mu = Mathf.Clamp(rawMu, 1f, 3f);
                }

                // 瞬きと不透明度
                float twinkle = 0.75f + 0.25f * Mathf.Sin(twoPi * star.frequency * timeSec + star.phase);
                float coreAlpha = Mathf.Clamp01(star.baseAlpha * twinkle * mu);

                // 大きさ倍率
                float sizeMult = Mathf.Min(1.8f, Mathf.Sqrt(mu));
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
