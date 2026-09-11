#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif
using System;
using UnityEngine;

namespace BangsEdge.Game
{
    /// <summary>
    /// WebGL環境におけるブラウザの表示解像度 (CSS px比率) やポインター種別の判定を提供する静的クラス。
    /// </summary>
    public static class WebDisplay
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern double BangsDisplay_GetScreenPixelsPerCssPixel();

        [DllImport("__Internal")]
        private static extern int BangsDisplay_IsCoarsePointer();
#endif

        /// <summary>
        /// キャンバスのCSS 1pxあたりのスクリーンピクセル数。
        /// </summary>
        public static float ScreenPixelsPerCssPixel
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                double val = BangsDisplay_GetScreenPixelsPerCssPixel();
                if (double.IsNaN(val) || double.IsInfinity(val) || val <= 0.0)
                {
                    return 1f;
                }
                return (float)val;
#else
                return Screen.dpi > 0f ? Screen.dpi / 96f : 1f;
#endif
            }
        }

        /// <summary>
        /// タッチが主な入力の端末 (coarse pointer) かどうか。
        /// </summary>
        public static bool IsCoarsePointer
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return BangsDisplay_IsCoarsePointer() == 1;
#else
                return false;
#endif
            }
        }
    }
}
