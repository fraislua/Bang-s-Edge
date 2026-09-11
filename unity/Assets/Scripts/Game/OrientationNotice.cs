using UnityEngine;
using UnityEngine.UI;

namespace BangsEdge.Game
{
    /// <summary>
    /// 画面が縦長のとき、画面全体に横向き案内を表示するオーバーレイCanvasコンポーネント。
    /// レターボックスの外側を含め、画面全体を覆って案内を出す。
    /// </summary>
    public sealed class OrientationNotice : MonoBehaviour
    {
        private GameObject _root;
        private Sprite _bgSprite;
        private bool _lastShowNotice;
        private bool _isInitialized;

        public void Initialize()
        {
            // 1. 全面オーバーレイCanvasの設定
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100; // 最前面に表示

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0f; // 画面幅に基準を合わせる

            // 2. フォントの読み込み
            var fontBold = Resources.Load<Font>("Fonts/BIZUDGothic-Bold");
            var fontRegular = Resources.Load<Font>("Fonts/BIZUDGothic-Regular");
            if (fontBold == null || fontRegular == null)
            {
                Debug.LogWarning("[OrientationNotice] BIZUDGothic-Bold or BIZUDGothic-Regular font not found in Resources/Fonts/.");
            }

            // 3. ルートコンテナの生成 (画面全体に広げる)
            _root = new GameObject("Root");
            _root.transform.SetParent(transform, false);
            var rootRt = _root.AddComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;

            // 4. 背景画像の生成 (白1x1テクスチャ、不透明度はImage.colorで指定)
            var bgTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            bgTex.SetPixel(0, 0, Color.white);
            bgTex.Apply();
            _bgSprite = Sprite.Create(bgTex, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);

            var bgGo = new GameObject("Background");
            bgGo.transform.SetParent(_root.transform, false);
            var bgRt = bgGo.AddComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;
            var bgImg = bgGo.AddComponent<Image>();
            bgImg.sprite = _bgSprite;
            bgImg.color = new Color(0f, 0f, 0f, 0.92f);
            bgImg.raycastTarget = false;

            // 5. 案内文の生成 (背景と兄弟関係にしてスケーリングの影響を分離)
            // 1行目: 横向きにしてください
            var line1Go = new GameObject("NoticeLine1");
            line1Go.transform.SetParent(_root.transform, false);
            var line1Rt = line1Go.AddComponent<RectTransform>();
            line1Rt.anchorMin = new Vector2(0.5f, 0.5f);
            line1Rt.anchorMax = new Vector2(0.5f, 0.5f);
            line1Rt.pivot = new Vector2(0.5f, 0.5f);
            line1Rt.anchoredPosition = new Vector2(0f, 50f);
            line1Rt.sizeDelta = new Vector2(1000f, 100f);
            var text1 = line1Go.AddComponent<Text>();
            text1.font = fontBold;
            text1.fontSize = 64;
            text1.fontStyle = FontStyle.Normal;
            text1.color = Color.white;
            text1.alignment = TextAnchor.MiddleCenter;
            text1.horizontalOverflow = HorizontalWrapMode.Overflow;
            text1.verticalOverflow = VerticalWrapMode.Overflow;
            text1.raycastTarget = false;
            text1.supportRichText = false;
            text1.text = "横向きにしてください";

            // 2行目: ROTATE YOUR DEVICE TO LANDSCAPE
            var line2Go = new GameObject("NoticeLine2");
            line2Go.transform.SetParent(_root.transform, false);
            var line2Rt = line2Go.AddComponent<RectTransform>();
            line2Rt.anchorMin = new Vector2(0.5f, 0.5f);
            line2Rt.anchorMax = new Vector2(0.5f, 0.5f);
            line2Rt.pivot = new Vector2(0.5f, 0.5f);
            line2Rt.anchoredPosition = new Vector2(0f, -50f);
            line2Rt.sizeDelta = new Vector2(1000f, 60f);
            var text2 = line2Go.AddComponent<Text>();
            text2.font = fontRegular;
            text2.fontSize = 36;
            text2.fontStyle = FontStyle.Normal;
            text2.color = new Color(148f / 255f, 163f / 255f, 184f / 255f, 1f);
            text2.alignment = TextAnchor.MiddleCenter;
            text2.horizontalOverflow = HorizontalWrapMode.Overflow;
            text2.verticalOverflow = VerticalWrapMode.Overflow;
            text2.raycastTarget = false;
            text2.supportRichText = false;
            text2.text = "ROTATE YOUR DEVICE TO LANDSCAPE";

            // 6. 現在の向きに合わせて初期表示状態を反映
            bool initialShow = Screen.height > Screen.width;
            _lastShowNotice = initialShow;
            _root.SetActive(initialShow);
            _isInitialized = true;
        }

        private void Update()
        {
            if (!_isInitialized || _root == null) return;

            // 画面向きの変化を監視し、切り替わった時のみSetActiveを呼び出す
            bool show = Screen.height > Screen.width;
            if (show != _lastShowNotice)
            {
                _lastShowNotice = show;
                _root.SetActive(show);
            }
        }

        private void OnDestroy()
        {
            // 動的生成したテクスチャとスプライトの破棄
            if (_bgSprite != null)
            {
                if (_bgSprite.texture != null)
                {
                    Destroy(_bgSprite.texture);
                }
                Destroy(_bgSprite);
            }
        }
    }
}
