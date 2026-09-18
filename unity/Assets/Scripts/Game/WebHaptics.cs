#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace BangsEdge.Game
{
    /// <summary>
    /// WebGL 環境で jslib (BangsHaptics.jslib) を介して navigator.vibrate を呼ぶ。
    /// 対応していない環境 (iOS Safari、PC、エディタ) では何もしない。音のミュートとは独立。
    /// </summary>
    public static class WebHaptics
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void BangsHaptics_Vibrate(int milliseconds);
#endif

        /// <summary>指定ミリ秒だけ振動させる (0 以下は無視)。</summary>
        public static void Vibrate(int milliseconds)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (milliseconds > 0)
            {
                BangsHaptics_Vibrate(milliseconds);
            }
#endif
        }
    }
}
