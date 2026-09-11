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
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void BangsAudio_StartDrone();

        [DllImport("__Internal")]
        private static extern void BangsAudio_StopDrone();

        [DllImport("__Internal")]
        private static extern void BangsAudio_UpdateWarning(double dangerRatio, double dt);

        [DllImport("__Internal")]
        private static extern void BangsAudio_PlayResolve();

        [DllImport("__Internal")]
        private static extern void BangsAudio_PlayBigBang();

        [DllImport("__Internal")]
        private static extern int BangsAudio_ToggleMute();

        [DllImport("__Internal")]
        private static extern int BangsAudio_IsMuted();
#endif

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
            BangsAudio_PlayResolve();
#endif
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
    }
}
