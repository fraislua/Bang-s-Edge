using System;
using UnityEngine;
using UnityEngine.UI;
using BangsEdge.Simulation;

namespace BangsEdge.Game
{
    /// <summary>
    /// HUD (密度バー、危険度表示、射程警告、FPS、スコア、中央案内文字、結果画面、ミュートボタン)
    /// の描画およびUI管理を担当するコンポーネント。
    /// uGUI (Screen Space - Camera) を実行時に動的構築し、GC Alloc を最小化する。
    /// </summary>
    public sealed class GameHud : MonoBehaviour
    {
        // フォント。通常と太字は別のファイルを使う(Unityに太字を合成させると、小さい漢字の線が潰れて読めなくなった)
        private Font _fontRegular;
        private Font _fontBold;

        // 生成スプライトおよびテクスチャ (OnDestroyで解放)
        private Sprite _solidWhiteSprite;
        private Sprite _barBgSprite;
        private Sprite _barFillSprite;
        private Sprite _muteBtnMutedSprite;
        private Sprite _muteBtnUnmutedSprite;

        // UI コンポーネント参照
        private Canvas _canvas;
        private CanvasScaler _scaler;

        // 密度バー
        private Image _barFillImage;
        private Color _cachedBarFillColor;

        // 85% 線 & ラベル
        private Image _limitLineImage;
        private Text _limitLabelText;

        // 危険度表示
        private Text _dangerStageText;

        // 射程表示 (Attracting時のみ)
        private GameObject _reachGroup;
        private Text _reachCountText;
        private Text _unreachWarnText;

        // FPS
        private Text _fpsText;

        // スコア / ハイスコア
        private Text _scoreText;
        private Text _highScoreText;

        // 中央文字群
        private GameObject _readyGroup;
        private GameObject _resolvedGroup;
        private Text _resolvedScoreText;
        private Text _resolvedNewHighText;
        private GameObject _bangGroup;

        // ミュートボタン
        private Image _muteBtnImage;

        // キャッシュ変数 (GC Alloc / 不要更新防止)
        private DangerStage _cachedDangerStage = (DangerStage)(-1);
        private int _cachedGraceCounter = -1;
        private bool _cachedCriticalPulse;
        private Color _cachedDangerColor;

        private int _cachedReachableCount = -1;
        private bool _cachedBangPossible = true;
        private bool _cachedUnreachPulse;

        private int _cachedFps = -1;
        private bool _cachedFpsLow;

        private GameState _cachedState = (GameState)(-1);
        private int _cachedDisplayScore = -1;
        private int _cachedHighScore = -1;
        private int _cachedFinalScore = -1;
        private bool _cachedIsNewHigh;

        private bool _cachedMuted;
        private bool _isMuteBtnInitialized;

        public void Initialize(Camera boardCamera)
        {
            if (boardCamera == null) throw new ArgumentNullException(nameof(boardCamera));

            // 1. フォントのロード
            _fontRegular = Resources.Load<Font>("Fonts/BIZUDGothic-Regular");
            _fontBold = Resources.Load<Font>("Fonts/BIZUDGothic-Bold");
            if (_fontRegular == null || _fontBold == null)
            {
                Debug.LogWarning("[GameHud] BIZUDGothic-Regular/Bold not found in Resources/Fonts/.");
            }

            // 2. 動的スプライトの生成
            CreateSprites();

            // 3. Canvas および CanvasScaler の設定
            SetupCanvas(boardCamera);

            // 4. UI 階層の構築
            BuildHudHierarchy();
        }

        private void SetupCanvas(Camera boardCamera)
        {
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceCamera;
            _canvas.worldCamera = boardCamera;
            _canvas.sortingOrder = 15; // 盤面(最大10)より上、枠(20)より下
            _canvas.planeDistance = 10f;
            _canvas.pixelPerfect = true; // 文字の頂点を画面のピクセルに揃え、倍率が半端なときのにじみを抑える

            _scaler = gameObject.AddComponent<CanvasScaler>();
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.referenceResolution = new Vector2(GameConfig.LOGICAL_WIDTH, GameConfig.LOGICAL_HEIGHT);
            _scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            _scaler.matchWidthOrHeight = 0.5f; // 16:9 レターボックスカメラのため完全追従
        }

        private void CreateSprites()
        {
            // 1. 白 1x1 スプライト
            var solidTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            solidTex.SetPixel(0, 0, Color.white);
            solidTex.Apply();
            _solidWhiteSprite = Sprite.Create(solidTex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);

            // 2. 密度バー背景 + 枠 (2x: 840x28, 角丸4px = 8実px, 枠線幅1px = 2実px)
            _barBgSprite = CreateBarBackgroundSprite();

            // 3. 密度バー塗り用 9-slice スプライト (32x32, 角丸8実px, border 8px, PPU 2 = 論理角丸4px)
            _barFillSprite = CreateBarFillSprite();

            // 4. ミュートボタン用スプライト (4x: 112x112, 28x28論理px, 角丸6px, スピーカー/×/音波)
            _muteBtnMutedSprite = CreateMuteButtonSprite(true);
            _muteBtnUnmutedSprite = CreateMuteButtonSprite(false);
        }

        private Sprite CreateBarBackgroundSprite()
        {
            int w = 840;
            int h = 28;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            var colors = new Color32[w * h];

            Color bgCol = new Color(12f / 255f, 14f / 255f, 24f / 255f, 0.85f);
            Color borderCol = new Color(1f, 1f, 1f, 0.2f);

            for (int y = 0; y < h; y++)
            {
                float ly = ((h - 1 - y) + 0.5f) / 2.0f; // 論理y (0〜14)
                for (int x = 0; x < w; x++)
                {
                    float lx = (x + 0.5f) / 2.0f; // 論理x (0〜420)
                    float relX = lx - 210f;
                    float relY = ly - 7f;
                    float qx = Mathf.Abs(relX) - (210f - 4f);
                    float qy = Mathf.Abs(relY) - (7f - 4f);
                    float dOuter = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f))
                                   + Mathf.Min(Mathf.Max(qx, qy), 0f) - 4f;

                    float alphaOuter = Mathf.Clamp01(0.5f - dOuter * 2.0f);
                    if (alphaOuter <= 0f)
                    {
                        colors[y * w + x] = new Color32(0, 0, 0, 0);
                        continue;
                    }

                    float dInner = dOuter + 1.0f;
                    float alphaInner = Mathf.Clamp01(0.5f - dInner * 2.0f);
                    float alphaBorder = Mathf.Max(0f, alphaOuter - alphaInner);

                    colors[y * w + x] = Composite(bgCol, alphaInner, borderCol, alphaBorder);
                }
            }

            tex.SetPixels32(colors);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 2f);
        }

        private Sprite CreateBarFillSprite()
        {
            int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            var colors = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                float py = y + 0.5f - 16f;
                for (int x = 0; x < size; x++)
                {
                    float px = x + 0.5f - 16f;
                    float qx = Mathf.Abs(px) - (16f - 8f);
                    float qy = Mathf.Abs(py) - (16f - 8f);
                    float d = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f))
                              + Mathf.Min(Mathf.Max(qx, qy), 0f) - 8f;
                    float a = Mathf.Clamp01(0.5f - d);
                    colors[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }

            tex.SetPixels32(colors);
            tex.Apply();
            // The texture is drawn at 2x. Sliced borders are measured against the canvas's
            // referencePixelsPerUnit (100), so 200 makes the 8-texel border 4 logical px.
            // With 2 the border became 400 units and the fill stretched into a long pill.
            return Sprite.Create(
                tex,
                new Rect(0, 0, size, size),
                new Vector2(0f, 0.5f),
                200f,
                0,
                SpriteMeshType.FullRect,
                new Vector4(8, 8, 8, 8)
            );
        }

        private Sprite CreateMuteButtonSprite(bool isMuted)
        {
            int size = 112; // 4x (28論理px)
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            var colors = new Color32[size * size];

            Color bgCol = new Color(15f / 255f, 23f / 255f, 42f / 255f, 0.7f);
            Color borderCol = isMuted
                ? new Color(148f / 255f, 163f / 255f, 184f / 255f, 0.4f)
                : new Color(0f, 229f / 255f, 1f, 0.4f);
            Color iconCol = isMuted
                ? new Color(148f / 255f, 163f / 255f, 184f / 255f, 1f)
                : new Color(0f, 229f / 255f, 1f, 1f);

            // スピーカー多角形の頂点 (中心基準)
            Vector2[] coneVerts = new Vector2[6]
            {
                new Vector2(-6.5f, -3.5f),
                new Vector2(-3.5f, -3.5f),
                new Vector2(0.0f, -6.5f),
                new Vector2(0.0f, 6.5f),
                new Vector2(-3.5f, 3.5f),
                new Vector2(-6.5f, 3.5f)
            };

            for (int y = 0; y < size; y++)
            {
                // Unityテクスチャ y=0 は下端。JS Canvas は上端が y=0。
                float ly = ((size - 1 - y) + 0.5f) / 4.0f; // 論理y (0〜28)
                for (int x = 0; x < size; x++)
                {
                    float lx = (x + 0.5f) / 4.0f; // 論理x (0〜28)
                    float relX = lx - 14.0f;
                    float relY = ly - 14.0f;

                    // 1. 角丸矩形 (サイズ28, 半径6)
                    float qx = Mathf.Abs(relX) - (14f - 6f);
                    float qy = Mathf.Abs(relY) - (14f - 6f);
                    float dOuter = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f))
                                   + Mathf.Min(Mathf.Max(qx, qy), 0f) - 6f;

                    float alphaOuter = Mathf.Clamp01(0.5f - dOuter * 4.0f);
                    if (alphaOuter <= 0f)
                    {
                        colors[y * size + x] = new Color32(0, 0, 0, 0);
                        continue;
                    }

                    // 枠線 (線幅1px)
                    float dInner = dOuter + 1.0f;
                    float alphaInner = Mathf.Clamp01(0.5f - dInner * 4.0f);
                    float alphaBorder = Mathf.Max(0f, alphaOuter - alphaInner);

                    Color pixelBg = Composite(bgCol, alphaInner, borderCol, alphaBorder);

                    // 2. スピーカーコーン部 (中心基準)
                    bool insideCone = false;
                    if (relX >= -6.5f && relX <= -3.5f && Mathf.Abs(relY) <= 3.5f)
                    {
                        insideCone = true;
                    }
                    else if (relX > -3.5f && relX <= 0.0f)
                    {
                        float maxSy = 3.5f + (6.5f - 3.5f) * ((relX + 3.5f) / 3.5f);
                        if (Mathf.Abs(relY) <= maxSy) insideCone = true;
                    }

                    float minConeDist = float.MaxValue;
                    for (int i = 0; i < 6; i++)
                    {
                        float dSeg = DistToSegment(new Vector2(relX, relY), coneVerts[i], coneVerts[(i + 1) % 6]);
                        if (dSeg < minConeDist) minConeDist = dSeg;
                    }
                    float dCone = insideCone ? -minConeDist : minConeDist;
                    float alphaCone = Mathf.Clamp01(0.5f - dCone * 4.0f);

                    // 3. アイコン詳細 (ミュート時は×、通常時は音波アーク2本)
                    float alphaDetail = 0f;
                    if (isMuted)
                    {
                        // × マーク (線幅1.6, 半径0.8)
                        float dX1 = DistToSegment(new Vector2(relX, relY), new Vector2(2.5f, -3.5f), new Vector2(7.5f, 3.5f));
                        float dX2 = DistToSegment(new Vector2(relX, relY), new Vector2(7.5f, -3.5f), new Vector2(2.5f, 3.5f));
                        float dX = Mathf.Min(dX1, dX2);
                        alphaDetail = Mathf.Clamp01(0.5f - (dX - 0.8f) * 4.0f);
                    }
                    else
                    {
                        // 音波アーク (中心 (2, 0), 半径4 と 7.5, 線幅1.6)
                        float dArc1 = DistToArc(new Vector2(relX, relY), new Vector2(2f, 0f), 4.0f, Mathf.PI / 3.0f);
                        float dArc2 = DistToArc(new Vector2(relX, relY), new Vector2(2f, 0f), 7.5f, Mathf.PI / 3.2f);
                        float dArc = Mathf.Min(dArc1, dArc2);
                        alphaDetail = Mathf.Clamp01(0.5f - (dArc - 0.8f) * 4.0f);
                    }

                    float alphaIcon = Mathf.Clamp01(alphaCone + alphaDetail);

                    // ソースオーバー合成
                    Color finalColor = Color.Lerp(pixelBg, iconCol, alphaIcon);
                    colors[y * size + x] = finalColor;
                }
            }

            tex.SetPixels32(colors);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 4f);
        }

        /// <summary>
        /// Combines two colours with coverage weights into a straight (non-premultiplied) alpha
        /// colour, which is what the sprite shader expects. Multiplying the whole Color by the
        /// coverage would also scale RGB by alpha, so a 20% white border displayed at about 4%.
        /// </summary>
        private static Color Composite(Color a, float coverageA, Color b, float coverageB)
        {
            float wa = a.a * coverageA;
            float wb = b.a * coverageB;
            float alpha = wa + wb;
            if (alpha <= 0f) return new Color(0f, 0f, 0f, 0f);
            return new Color(
                (a.r * wa + b.r * wb) / alpha,
                (a.g * wa + b.g * wb) / alpha,
                (a.b * wa + b.b * wb) / alpha,
                Mathf.Min(1f, alpha));
        }

        private static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 pa = p - a;
            Vector2 ba = b - a;
            float lenSq = ba.sqrMagnitude;
            if (lenSq <= 0.00001f) return pa.magnitude;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / lenSq);
            return (pa - ba * h).magnitude;
        }

        private static float DistToArc(Vector2 p, Vector2 center, float radius, float maxAngle)
        {
            Vector2 rel = p - center;
            float r = rel.magnitude;
            float angle = Mathf.Atan2(rel.y, rel.x);

            if (Mathf.Abs(angle) <= maxAngle && rel.x > 0f)
            {
                return Mathf.Abs(r - radius);
            }

            Vector2 pTop = center + new Vector2(radius * Mathf.Cos(maxAngle), radius * Mathf.Sin(maxAngle));
            Vector2 pBot = center + new Vector2(radius * Mathf.Cos(-maxAngle), radius * Mathf.Sin(-maxAngle));
            return Mathf.Min((p - pTop).magnitude, (p - pBot).magnitude);
        }

        private void BuildHudHierarchy()
        {
            var hudRoot = new GameObject("HudRoot");
            hudRoot.transform.SetParent(transform, false);
            var rootRt = hudRoot.AddComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;

            // 1. 密度バー (左上 750, 24, 幅420, 高さ14)
            var barRoot = new GameObject("DensityBar");
            barRoot.transform.SetParent(hudRoot.transform, false);
            var barRt = barRoot.AddComponent<RectTransform>();
            SetTopLeft(barRt, new Vector2(750f, -24f), new Vector2(420f, 14f));

            // バー背景 + 枠
            var barBgGo = new GameObject("Bg");
            barBgGo.transform.SetParent(barRoot.transform, false);
            var bgRt = barBgGo.AddComponent<RectTransform>();
            SetFillParent(bgRt);
            var bgImg = barBgGo.AddComponent<Image>();
            bgImg.sprite = _barBgSprite;
            bgImg.raycastTarget = false;

            // バー塗り (9-slice)
            var barFillGo = new GameObject("Fill");
            barFillGo.transform.SetParent(barRoot.transform, false);
            var fillRt = barFillGo.AddComponent<RectTransform>();
            fillRt.anchorMin = new Vector2(0f, 0.5f);
            fillRt.anchorMax = new Vector2(0f, 0.5f);
            fillRt.pivot = new Vector2(0f, 0.5f);
            fillRt.anchoredPosition = Vector2.zero;
            fillRt.sizeDelta = new Vector2(0f, 14f);
            _barFillImage = barFillGo.AddComponent<Image>();
            _barFillImage.sprite = _barFillSprite;
            _barFillImage.type = Image.Type.Sliced;
            _barFillImage.raycastTarget = false;
            _barFillImage.enabled = false;

            // 2. 85% 線 (x = 750 + 420*0.85 = 1107, y は 21〜41, 線幅2, #ff3333)
            var limitLineGo = new GameObject("LimitLine");
            limitLineGo.transform.SetParent(hudRoot.transform, false);
            var lineRt = limitLineGo.AddComponent<RectTransform>();
            lineRt.anchorMin = new Vector2(0f, 1f);
            lineRt.anchorMax = new Vector2(0f, 1f);
            lineRt.pivot = new Vector2(0.5f, 1f);
            lineRt.anchoredPosition = new Vector2(1107f, -21f);
            lineRt.sizeDelta = new Vector2(2f, 20f);
            _limitLineImage = limitLineGo.AddComponent<Image>();
            _limitLineImage.sprite = _solidWhiteSprite;
            _limitLineImage.color = new Color(1f, 51f / 255f, 51f / 255f, 1f);
            _limitLineImage.raycastTarget = false;

            // BANG LIMIT (85%) ラベル (ベースライン y=53, x=1107, 太字10, #ff4444)
            _limitLabelText = CreateText(
                hudRoot.transform,
                "LimitLabel",
                "BANG LIMIT (85%)",
                10,
                FontStyle.Bold,
                new Color(1f, 68f / 255f, 68f / 255f, 1f),
                TextAnchor.LowerCenter,
                1107f,
                53f
            );

            // 3. 危険度ステージ表示 (ベースライン y=16, x=960, 太字12)
            _dangerStageText = CreateText(
                hudRoot.transform,
                "DangerStage",
                "DANGER: SAFE",
                12,
                FontStyle.Bold,
                new Color(148f / 255f, 163f / 255f, 184f / 255f, 1f),
                TextAnchor.LowerCenter,
                960f,
                16f
            );

            // 4. 射程可視化表示 (Attracting時のみ)
            _reachGroup = new GameObject("ReachGroup");
            _reachGroup.transform.SetParent(hudRoot.transform, false);
            var reachRt = _reachGroup.AddComponent<RectTransform>();
            SetFillParent(reachRt);

            // 1行目: 射程内 N / 200  (必要 170) (ベースライン y=70, x=960, 太字11)
            _reachCountText = CreateText(
                _reachGroup.transform,
                "ReachCount",
                $"射程内 0 / {GameConfig.TOTAL_PARTICLES}  (必要 {GameConfig.REQUIRED_PARTICLES})",
                11,
                FontStyle.Bold,
                new Color(148f / 255f, 163f / 255f, 184f / 255f, 0.9f),
                TextAnchor.LowerCenter,
                960f,
                70f
            );

            // 2行目: 到達不能警告 (ベースライン y=86, x=960, 太字12)
            _unreachWarnText = CreateText(
                _reachGroup.transform,
                "UnreachWarn",
                "この位置では到達不能 — 画面の中央へ",
                12,
                FontStyle.Bold,
                new Color(1f, 68f / 255f, 68f / 255f, 1f),
                TextAnchor.LowerCenter,
                960f,
                86f
            );
            _reachGroup.SetActive(false);

            // 5. FPS (左下 16, 1064, 通常10)
            _fpsText = CreateText(
                hudRoot.transform,
                "Fps",
                "60 fps",
                10,
                FontStyle.Normal,
                new Color(148f / 255f, 163f / 255f, 184f / 255f, 0.55f),
                TextAnchor.LowerLeft,
                16f,
                1064f
            );

            // 6. スコア (左上 20, 36, 太字16, 白)
            _scoreText = CreateText(
                hudRoot.transform,
                "Score",
                "SCORE: 0000",
                16,
                FontStyle.Bold,
                Color.white,
                TextAnchor.LowerLeft,
                20f,
                36f
            );

            // 7. ハイスコア (右上 x=1860, y=36, 太字16, #ffd700, 右揃え)
            _highScoreText = CreateText(
                hudRoot.transform,
                "HighScore",
                "HIGH: 0000",
                16,
                FontStyle.Bold,
                new Color(1f, 215f / 255f, 0f, 1f),
                TextAnchor.LowerRight,
                1860f,
                36f
            );

            // 8. 中央文字群 (状態ごと、中央揃え x=960)
            BuildCenterTexts(hudRoot.transform);

            // 9. ミュートボタン (左上 1874, 18, 幅28, 高さ28)
            var muteGo = new GameObject("MuteButton");
            muteGo.transform.SetParent(hudRoot.transform, false);
            var muteRt = muteGo.AddComponent<RectTransform>();
            SetTopLeft(muteRt, new Vector2(1874f, -18f), new Vector2(28f, 28f));
            _muteBtnImage = muteGo.AddComponent<Image>();
            _muteBtnImage.sprite = _muteBtnUnmutedSprite;
            _muteBtnImage.raycastTarget = false;
        }

        private void BuildCenterTexts(Transform parent)
        {
            var centerRoot = new GameObject("CenterTexts");
            centerRoot.transform.SetParent(parent, false);
            var centerRt = centerRoot.AddComponent<RectTransform>();
            SetFillParent(centerRt);

            // --- Ready ---
            _readyGroup = new GameObject("ReadyGroup");
            _readyGroup.transform.SetParent(centerRoot.transform, false);
            SetFillParent(_readyGroup.AddComponent<RectTransform>());

            CreateText(_readyGroup.transform, "Title", "BANG'S-EDGE", 22, FontStyle.Bold, Color.white, TextAnchor.LowerCenter, 960f, 520f);
            CreateText(_readyGroup.transform, "Sub1", "CLICK & HOLD TO ACCUMULATE PARTICLES", 14, FontStyle.Normal, new Color(148f / 255f, 163f / 255f, 184f / 255f, 1f), TextAnchor.LowerCenter, 960f, 555f);
            CreateText(_readyGroup.transform, "Sub2", "RELEASE BEFORE BIG BANG TO LOCK SCORE", 14, FontStyle.Normal, new Color(148f / 255f, 163f / 255f, 184f / 255f, 1f), TextAnchor.LowerCenter, 960f, 578f);

            // --- Resolved ---
            _resolvedGroup = new GameObject("ResolvedGroup");
            _resolvedGroup.transform.SetParent(centerRoot.transform, false);
            SetFillParent(_resolvedGroup.AddComponent<RectTransform>());

            CreateText(_resolvedGroup.transform, "Title", "ROUND RESOLVED!", 24, FontStyle.Bold, new Color(0f, 229f / 255f, 1f, 1f), TextAnchor.LowerCenter, 960f, 515f);
            _resolvedScoreText = CreateText(_resolvedGroup.transform, "Score", "SCORE: 0", 36, FontStyle.Bold, Color.white, TextAnchor.LowerCenter, 960f, 558f);
            _resolvedNewHighText = CreateText(_resolvedGroup.transform, "NewHigh", "★ NEW HIGH SCORE! ★", 14, FontStyle.Bold, new Color(1f, 215f / 255f, 0f, 1f), TextAnchor.LowerCenter, 960f, 585f);
            _resolvedNewHighText.gameObject.SetActive(false);
            CreateText(_resolvedGroup.transform, "Sub", "CLICK TO START NEXT ROUND", 14, FontStyle.Normal, new Color(148f / 255f, 163f / 255f, 184f / 255f, 1f), TextAnchor.LowerCenter, 960f, 615f);

            // --- Bang ---
            _bangGroup = new GameObject("BangGroup");
            _bangGroup.transform.SetParent(centerRoot.transform, false);
            SetFillParent(_bangGroup.AddComponent<RectTransform>());

            // JSはこの文字に赤いぼかし影(shadowBlur 15)を付けている。uGUIの Outline と Shadow で真似たところ、
            // 文字がずれて二重・三重に重なり読めなくなった(ユーザー確認)ので付けない。発光の再現はUIの微調整で扱う
            CreateText(_bangGroup.transform, "Title", "BIG BANG DETECTED!", 32, FontStyle.Bold, new Color(1f, 34f / 255f, 0f, 1f), TextAnchor.LowerCenter, 960f, 520f);

            CreateText(_bangGroup.transform, "Fail", "ROUND FAILED — SCORE: 0", 18, FontStyle.Bold, new Color(1f, 163f / 255f, 158f / 255f, 1f), TextAnchor.LowerCenter, 960f, 560f);
            CreateText(_bangGroup.transform, "Sub", "CLICK TO RETRY", 14, FontStyle.Normal, new Color(148f / 255f, 163f / 255f, 184f / 255f, 1f), TextAnchor.LowerCenter, 960f, 595f);

            _readyGroup.SetActive(true);
            _resolvedGroup.SetActive(false);
            _bangGroup.SetActive(false);
        }

        private Text CreateText(
            Transform parent,
            string objName,
            string initialContent,
            int fontSize,
            FontStyle fontStyle,
            Color color,
            TextAnchor alignment,
            float logicalX,
            float logicalBaselineY)
        {
            var go = new GameObject(objName);
            go.transform.SetParent(parent, false);

            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);

            // 水平ピボットの判定
            float pivotX = 0.5f;
            if (alignment == TextAnchor.LowerLeft || alignment == TextAnchor.MiddleLeft || alignment == TextAnchor.UpperLeft)
            {
                pivotX = 0f;
            }
            else if (alignment == TextAnchor.LowerRight || alignment == TextAnchor.MiddleRight || alignment == TextAnchor.UpperRight)
            {
                pivotX = 1f;
            }

            // ベースライン配置: 文字の下端が y + fontSize * 0.25f に来るよう配置
            rt.pivot = new Vector2(pivotX, 0f);
            float unityY = -(logicalBaselineY + fontSize * 0.25f);
            rt.anchoredPosition = new Vector2(logicalX, unityY);
            rt.sizeDelta = new Vector2(1000f, fontSize * 1.5f);

            var text = go.AddComponent<Text>();
            // 太字は太字のフォントファイルで描き、合成の太字(FontStyle.Bold)は使わない
            text.font = fontStyle == FontStyle.Bold ? _fontBold : _fontRegular;
            text.fontSize = fontSize;
            text.fontStyle = FontStyle.Normal;
            text.color = color;
            text.alignment = alignment;
            text.raycastTarget = false;
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.text = initialContent;

            return text;
        }

        private static void SetTopLeft(RectTransform rt, Vector2 anchoredPos, Vector2 size)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
        }

        private static void SetFillParent(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// 毎フレームの描画更新を行う。GameManager の Render 直後に呼ばれる。
        /// </summary>
        public void Render(BangSimulation sim, double nowMs, double fpsEstimate)
        {
            if (sim == null) return;

            // 1. 密度バーの更新
            UpdateDensityBar(sim, nowMs);

            // 2. 危険度ステージの更新
            UpdateDangerStage(sim, nowMs);

            // 3. 射程可視化表示の更新 (Attracting時のみ)
            UpdateReachDisplay(sim, nowMs);

            // 4. FPS の更新
            UpdateFps(fpsEstimate);

            // 5. スコア / ハイスコアの更新
            UpdateScores(sim);

            // 6. 中央文字群の更新
            UpdateCenterTexts(sim);

            // 7. ミュートボタンの更新
            UpdateMuteButton();
        }

        private void UpdateDensityBar(BangSimulation sim, double nowMs)
        {
            double fillRatio = Math.Max(0.0, Math.Min(1.0, sim.CurrentDensity / GameConfig.DENSITY_MAX));
            float fillW = (float)(420.0 * fillRatio);

            if (fillW > 0f)
            {
                if (!_barFillImage.enabled) _barFillImage.enabled = true;
                _barFillImage.rectTransform.sizeDelta = new Vector2(fillW, 14f);

                Color targetCol;
                if (sim.Stage == DangerStage.Warm)
                {
                    targetCol = new Color(1f, 234f / 255f, 0f, 1f); // #ffea00
                }
                else if (sim.Stage == DangerStage.Hot)
                {
                    targetCol = new Color(1f, 85f / 255f, 0f, 1f); // #ff5500
                }
                else if (sim.Stage == DangerStage.Critical)
                {
                    // 80ms 周期点滅
                    bool isWhite = ((long)Math.Floor(nowMs / 80.0) % 2 == 0);
                    targetCol = isWhite ? Color.white : new Color(1f, 0f, 51f / 255f, 1f); // #ff0033
                }
                else
                {
                    targetCol = new Color(0f, 229f / 255f, 1f, 1f); // SAFE: #00e5ff
                }

                if (_cachedBarFillColor != targetCol)
                {
                    _cachedBarFillColor = targetCol;
                    _barFillImage.color = targetCol;
                }
            }
            else
            {
                if (_barFillImage.enabled) _barFillImage.enabled = false;
            }
        }

        private void UpdateDangerStage(BangSimulation sim, double nowMs)
        {
            DangerStage currentStage = sim.Stage;
            int currentGrace = sim.GraceCounter;

            if (currentStage == DangerStage.Critical)
            {
                bool isRed = ((long)Math.Floor(nowMs / 100.0) % 2 == 0);
                Color stageColor = isRed ? new Color(1f, 0f, 51f / 255f, 1f) : Color.white;

                if (_cachedDangerStage != DangerStage.Critical || _cachedGraceCounter != currentGrace)
                {
                    _cachedDangerStage = DangerStage.Critical;
                    _cachedGraceCounter = currentGrace;
                    _dangerStageText.text = $"CRITICAL WARNING ({currentGrace}/{GameConfig.BANG_GRACE_FRAMES})";
                }

                if (_cachedCriticalPulse != isRed || _cachedDangerColor != stageColor)
                {
                    _cachedCriticalPulse = isRed;
                    _cachedDangerColor = stageColor;
                    _dangerStageText.color = stageColor;
                }
            }
            else
            {
                if (_cachedDangerStage != currentStage)
                {
                    _cachedDangerStage = currentStage;
                    _cachedGraceCounter = -1;

                    Color stageColor;
                    string stageText;

                    if (currentStage == DangerStage.Warm)
                    {
                        stageText = "DANGER: WARM";
                        stageColor = new Color(1f, 234f / 255f, 0f, 1f); // #ffea00
                    }
                    else if (currentStage == DangerStage.Hot)
                    {
                        stageText = "DANGER: HOT";
                        stageColor = new Color(1f, 85f / 255f, 0f, 1f); // #ff5500
                    }
                    else
                    {
                        stageText = "DANGER: SAFE";
                        stageColor = new Color(148f / 255f, 163f / 255f, 184f / 255f, 1f); // #94a3b8
                    }

                    _dangerStageText.text = stageText;
                    _dangerStageText.color = stageColor;
                    _cachedDangerColor = stageColor;
                }
            }
        }

        private void UpdateReachDisplay(BangSimulation sim, double nowMs)
        {
            if (sim.State == GameState.Attracting)
            {
                if (!_reachGroup.activeSelf) _reachGroup.SetActive(true);

                // 1行目: 射程内個数
                int reachable = sim.ReachableCount;
                bool bangPossible = sim.BangPossible;

                if (reachable != _cachedReachableCount)
                {
                    _cachedReachableCount = reachable;
                    _reachCountText.text = $"射程内 {reachable} / {GameConfig.TOTAL_PARTICLES}  (必要 {GameConfig.REQUIRED_PARTICLES})";
                }

                if (bangPossible != _cachedBangPossible)
                {
                    _cachedBangPossible = bangPossible;
                    _reachCountText.color = bangPossible
                        ? new Color(148f / 255f, 163f / 255f, 184f / 255f, 0.9f)
                        : new Color(1f, 68f / 255f, 68f / 255f, 1f);
                }

                // 2行目: 到達不能警告 (bangPossible === false のときのみ)
                if (!bangPossible)
                {
                    if (!_unreachWarnText.gameObject.activeSelf) _unreachWarnText.gameObject.SetActive(true);

                    // 400ms 周期点滅
                    bool isBright = ((long)Math.Floor(nowMs / 400.0) % 2 == 0);
                    if (_cachedUnreachPulse != isBright)
                    {
                        _cachedUnreachPulse = isBright;
                        _unreachWarnText.color = isBright
                            ? new Color(1f, 68f / 255f, 68f / 255f, 1f) // #ff4444
                            : new Color(1f, 153f / 255f, 153f / 255f, 1f); // #ff9999
                    }
                }
                else
                {
                    if (_unreachWarnText.gameObject.activeSelf) _unreachWarnText.gameObject.SetActive(false);
                }
            }
            else
            {
                if (_reachGroup.activeSelf) _reachGroup.SetActive(false);
            }
        }

        private void UpdateFps(double fpsEstimate)
        {
            int roundedFps = (int)Math.Floor(fpsEstimate + 0.5);
            if (roundedFps != _cachedFps)
            {
                _cachedFps = roundedFps;
                _fpsText.text = $"{roundedFps} fps";
            }

            bool isLow = (roundedFps < 50);
            if (isLow != _cachedFpsLow)
            {
                _cachedFpsLow = isLow;
                _fpsText.color = isLow
                    ? new Color(1f, 153f / 255f, 153f / 255f, 0.85f)
                    : new Color(148f / 255f, 163f / 255f, 184f / 255f, 0.55f);
            }
        }

        private void UpdateScores(BangSimulation sim)
        {
            int displayScore = (sim.State == GameState.Resolved) ? sim.FinalScore : sim.CurrentScore;
            if (displayScore != _cachedDisplayScore)
            {
                _cachedDisplayScore = displayScore;
                _scoreText.text = $"SCORE: {displayScore:D4}";
            }

            int highScore = sim.HighScore;
            if (highScore != _cachedHighScore)
            {
                _cachedHighScore = highScore;
                _highScoreText.text = $"HIGH: {highScore:D4}";
            }
        }

        private void UpdateCenterTexts(BangSimulation sim)
        {
            GameState state = sim.State;
            if (state != _cachedState)
            {
                _cachedState = state;
                _readyGroup.SetActive(state == GameState.Ready);
                _resolvedGroup.SetActive(state == GameState.Resolved);
                _bangGroup.SetActive(state == GameState.Bang);
            }

            if (state == GameState.Resolved)
            {
                if (sim.FinalScore != _cachedFinalScore)
                {
                    _cachedFinalScore = sim.FinalScore;
                    _resolvedScoreText.text = $"SCORE: {sim.FinalScore}";
                }

                bool isNewHigh = (sim.FinalScore == sim.HighScore && sim.FinalScore > 0);
                if (isNewHigh != _cachedIsNewHigh)
                {
                    _cachedIsNewHigh = isNewHigh;
                    _resolvedNewHighText.gameObject.SetActive(isNewHigh);
                }
            }
        }

        private void UpdateMuteButton()
        {
            bool isMuted = AudioMuteManager.IsMuted;
            if (!_isMuteBtnInitialized || isMuted != _cachedMuted)
            {
                _isMuteBtnInitialized = true;
                _cachedMuted = isMuted;
                _muteBtnImage.sprite = isMuted ? _muteBtnMutedSprite : _muteBtnUnmutedSprite;
            }
        }

        private void OnDestroy()
        {
            // 動的生成したテクスチャとスプライトの明示的破棄
            DestroySpriteAndTexture(_solidWhiteSprite);
            DestroySpriteAndTexture(_barBgSprite);
            DestroySpriteAndTexture(_barFillSprite);
            DestroySpriteAndTexture(_muteBtnMutedSprite);
            DestroySpriteAndTexture(_muteBtnUnmutedSprite);
        }

        private static void DestroySpriteAndTexture(Sprite sprite)
        {
            if (sprite != null)
            {
                if (sprite.texture != null)
                {
                    Destroy(sprite.texture);
                }
                Destroy(sprite);
            }
        }
    }
}
