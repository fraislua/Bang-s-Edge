using System;
using UnityEngine;
using BangsEdge.Simulation;

namespace BangsEdge.Game
{
    /// <summary>
    /// レターボックス計算、正投影カメラ管理、およびスクリーン座標 ⇔ 論理座標の変換を行うコンポーネント。
    /// </summary>
    public sealed class LetterboxCamera : MonoBehaviour
    {
        [SerializeField] private Camera _boardCamera;

        public double ViewScale { get; private set; } = 1.0;
        public double ViewOffsetX { get; private set; } = 0.0;
        public double ViewOffsetY { get; private set; } = 0.0;

        public void Initialize(Camera boardCamera)
        {
            _boardCamera = boardCamera;
            UpdateLayout();
        }

        private void Update()
        {
            UpdateLayout();
        }

        /// <summary>
        /// 画面解像度に合わせてカメラの正規化 rect と変換係数を更新する。
        /// </summary>
        public void UpdateLayout()
        {
            if (_boardCamera == null) return;

            int screenW = Screen.width;
            int screenH = Screen.height;
            if (screenW <= 0 || screenH <= 0) return;

            // viewScale = min(画面幅 / 1920, 画面高さ / 1080)
            double scale = Math.Min(
                (double)screenW / GameConfig.LOGICAL_WIDTH,
                (double)screenH / GameConfig.LOGICAL_HEIGHT
            );

            // 余白 = (画面 - 盤面 × viewScale) / 2
            double boardW = GameConfig.LOGICAL_WIDTH * scale;
            double boardH = GameConfig.LOGICAL_HEIGHT * scale;
            double offsetX = (screenW - boardW) * 0.5;
            double offsetY = (screenH - boardH) * 0.5;

            ViewScale = scale;
            ViewOffsetX = offsetX;
            ViewOffsetY = offsetY;

            // カメラの rect を正規化座標 (0.0〜1.0) で設定
            float rx = (float)(offsetX / screenW);
            float ry = (float)(offsetY / screenH);
            float rw = (float)(boardW / screenW);
            float rh = (float)(boardH / screenH);

            _boardCamera.rect = new Rect(rx, ry, rw, rh);
        }

        /// <summary>
        /// Unityスクリーン座標 (左下原点) をゲーム論理座標 (左上原点 1920x1080) に変換する。
        /// 論理x = (マウスx - 余白x) / viewScale
        /// 論理y = ((画面高さ - マウスy) - 余白y) / viewScale
        /// </summary>
        public Vector2 ScreenToLogical(Vector2 screenPos)
        {
            if (ViewScale <= 0.000001) return Vector2.zero;

            int screenH = Screen.height;
            double lx = (screenPos.x - ViewOffsetX) / ViewScale;
            double ly = ((screenH - screenPos.y) - ViewOffsetY) / ViewScale;
            return new Vector2((float)lx, (float)ly);
        }
    }
}
