using UnityEngine;

namespace BangsEdge.Game
{
    /// <summary>
    /// ミュート状態の管理および永続化を担当する静的クラス。
    /// JS版 audio.js の STORAGE_KEY_MUTED ('bangs_edge_muted') に準拠。
    /// WebGL環境では audio.js (localStorage) の状態を正として同期し、
    /// エディタおよび非WebGL環境では PlayerPrefs を用いて永続化する。
    /// </summary>
    public static class AudioMuteManager
    {
        public const string STORAGE_KEY_MUTED = "bangs_edge_muted";

#if !UNITY_WEBGL || UNITY_EDITOR
        private static bool _isMuted;
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
    }
}
