using UnityEngine;

namespace BangsEdge.Game
{
    /// <summary>
    /// ミュート状態および音量の管理・永続化を担当する静的クラス。
    /// JS版 audio.js の STORAGE_KEY_MUTED ('bangs_edge_muted') および STORAGE_KEY_VOLUME ('bangs_edge_volume') に準拠。
    /// WebGL環境では audio.js (localStorage) の状態を正として同期し、
    /// エディタおよび非WebGL環境では PlayerPrefs を用いて永続化する。
    /// </summary>
    public static class AudioMuteManager
    {
        public const string STORAGE_KEY_MUTED = "bangs_edge_muted";
        public const string STORAGE_KEY_VOLUME = "bangs_edge_volume";

#if !UNITY_WEBGL || UNITY_EDITOR
        private static bool _isMuted;
        private static float _volume = 1f;
        private static bool _isLoaded;
#endif

        /// <summary>
        /// 現在のミュート状態を取得する。
        /// WebGL環境では audio.js の状態を返し、それ以外では PlayerPrefs の値を返す。
        /// </summary>
        public static bool IsMuted
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return WebAudioEvents.IsMuted();
#else
                EnsureLoaded();
                return _isMuted;
#endif
            }
        }

        /// <summary>
        /// 現在のマスター音量 (0.0〜1.0) を取得する。
        /// WebGL環境では audio.js の状態を返し、それ以外では PlayerPrefs の値を返す。
        /// </summary>
        public static float Volume
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return WebAudioEvents.GetVolume();
#else
                EnsureLoaded();
                return _volume;
#endif
            }
        }

#if !UNITY_WEBGL || UNITY_EDITOR
        /// <summary>
        /// 初回アクセス時に PlayerPrefs から状態を復元する (エディタ・非WebGL用)。
        /// </summary>
        private static void EnsureLoaded()
        {
            if (_isLoaded) return;

            // JS版の localStorage と同様、"true" という文字列ならミュート有効
            string saved = PlayerPrefs.GetString(STORAGE_KEY_MUTED, "false");
            _isMuted = (saved == "true");
            _volume = Mathf.Clamp01(PlayerPrefs.GetFloat(STORAGE_KEY_VOLUME, 1f));
            _isLoaded = true;
        }
#endif

        /// <summary>
        /// ミュート状態をトグル反転する。
        /// WebGL環境では audio.js の toggleMute を呼び出し、それ以外では PlayerPrefs を更新する。
        /// </summary>
        /// <returns>切り替え後のミュート状態</returns>
        public static bool ToggleMute()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return WebAudioEvents.ToggleMute();
#else
            EnsureLoaded();
            _isMuted = !_isMuted;
            PlayerPrefs.SetString(STORAGE_KEY_MUTED, _isMuted ? "true" : "false");
            PlayerPrefs.Save();
            return _isMuted;
#endif
        }

        /// <summary>
        /// ミュート状態を明示的に設定する (テスト・初期設定用)。
        /// WebGL環境では現在の状態と異なる場合のみ ToggleMute を呼び出す。
        /// </summary>
        public static void SetMuted(bool muted)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (IsMuted != muted)
            {
                ToggleMute();
            }
#else
            EnsureLoaded();
            _isMuted = muted;
            PlayerPrefs.SetString(STORAGE_KEY_MUTED, _isMuted ? "true" : "false");
            PlayerPrefs.Save();
#endif
        }

        /// <summary>
        /// マスター音量を設定する (0.0〜1.0)。
        /// WebGL環境では WebAudioEvents.SetVolume を呼び出し、それ以外では PlayerPrefs を更新する (Saveは呼ばない)。
        /// </summary>
        public static void SetVolume(float volume)
        {
            float clamped = Mathf.Clamp01(volume);
#if UNITY_WEBGL && !UNITY_EDITOR
            WebAudioEvents.SetVolume(clamped);
#else
            EnsureLoaded();
            _volume = clamped;
            PlayerPrefs.SetFloat(STORAGE_KEY_VOLUME, clamped);
#endif
        }
    }
}
