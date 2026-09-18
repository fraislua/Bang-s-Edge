#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif
using BangsEdge.Simulation;

namespace BangsEdge.Game
{
    /// <summary>
    /// WebGL 環境で jslib (BangsAudio.jslib) を介して window.AudioController を呼び出す
    /// IAudioEvents 実装。
    /// エディタおよび非WebGL環境では安全に何もしない (no-op)。
    /// </summary>
    public sealed class WebAudioEvents : IAudioEvents
    {
        private double _nextResolveEdge = 0.7;    // 既定値 (SetNextResolve が呼ばれなかったときの従来相当)
        private double _nextResolveWindow = 0.0;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void BangsAudio_StartDrone();

        [DllImport("__Internal")]
        private static extern void BangsAudio_StopDrone();

        [DllImport("__Internal")]
        private static extern void BangsAudio_UpdateWarning(double dangerRatio, double dt);

        [DllImport("__Internal")]
        private static extern void BangsAudio_PlayResolve(double edge, double window);

        [DllImport("__Internal")]
        private static extern void BangsAudio_PlayBigBang();

        [DllImport("__Internal")]
        private static extern int BangsAudio_ToggleMute();

        [DllImport("__Internal")]
        private static extern int BangsAudio_IsMuted();

        [DllImport("__Internal")]
        private static extern void BangsAudio_SetVolume(double volume);

        [DllImport("__Internal")]
        private static extern double BangsAudio_GetVolume();
#endif

        /// <summary>
        /// 次に PlayResolve() が鳴らす確定音の際どさを指定する。両引数は 0〜1 にクランプする。
        /// </summary>
        public void SetNextResolve(double edge, double window)
        {
            if (double.IsNaN(edge)) edge = 0.7;
            if (double.IsNaN(window)) window = 0.0;
            _nextResolveEdge = edge < 0.0 ? 0.0 : (edge > 1.0 ? 1.0 : edge);
            _nextResolveWindow = window < 0.0 ? 0.0 : (window > 1.0 ? 1.0 : window);
        }

        public void StartDrone()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            BangsAudio_StartDrone();
#endif
        }

        public void StopDrone()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            BangsAudio_StopDrone();
#endif
        }

        public void UpdateWarning(double dangerRatio, double dt)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            BangsAudio_UpdateWarning(dangerRatio, dt);
#endif
        }

        public void PlayResolve()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            BangsAudio_PlayResolve(_nextResolveEdge, _nextResolveWindow);
#endif
            _nextResolveEdge = 0.7;
            _nextResolveWindow = 0.0;
        }

        public void PlayBigBang()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            BangsAudio_PlayBigBang();
#endif
        }

        /// <summary>
        /// ミュート状態をトグル反転する。
        /// WebGLビルド時は BangsAudio_ToggleMute を呼び出し、反転後のミュート状態 (true: ミュート) を返す。
        /// 非WebGL・エディタ環境では常に false を返す。
        /// </summary>
        public static bool ToggleMute()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return BangsAudio_ToggleMute() != 0;
#else
            return false;
#endif
        }

        /// <summary>
        /// 現在のミュート状態を取得する。
        /// WebGLビルド時は BangsAudio_IsMuted を呼び出し、ミュート状態 (true: ミュート) を返す。
        /// 非WebGL・エディタ環境では常に false を返す。
        /// </summary>
        public static bool IsMuted()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return BangsAudio_IsMuted() != 0;
#else
            return false;
#endif
        }

        /// <summary>
        /// マスター音量を設定する (0.0〜1.0)。
        /// WebGLビルド時は BangsAudio_SetVolume を呼び出す。
        /// 非WebGL・エディタ環境では何もしない。
        /// </summary>
        public static void SetVolume(float volume)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            BangsAudio_SetVolume(volume);
#endif
        }

        /// <summary>
        /// 現在のマスター音量を取得する (0.0〜1.0)。
        /// WebGLビルド時は BangsAudio_GetVolume を呼び出す。
        /// 非WebGL・エディタ環境では 1.0f を返す。
        /// </summary>
        public static float GetVolume()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return (float)BangsAudio_GetVolume();
#else
            return 1f;
#endif
        }
    }
}
