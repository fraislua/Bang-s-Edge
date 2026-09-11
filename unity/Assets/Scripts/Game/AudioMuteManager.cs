using UnityEngine;

namespace BangsEdge.Game
{
    /// <summary>
    /// ミュート状態の管理および永続化を担当する静的クラス。
    /// JS版 audio.js の STORAGE_KEY_MUTED ('bangs_edge_muted') に準拠し、
    /// PlayerPrefs の文字列キーとして "true" / "false" を読み書きする。
    /// </summary>
    public static class AudioMuteManager
    {
        public const string STORAGE_KEY_MUTED = "bangs_edge_muted";

        private static bool _isMuted;
        private static bool _isLoaded;

        /// <summary>
        /// 現在のミュート状態を取得する。
        /// </summary>
        public static bool IsMuted
        {
            get
            {
                EnsureLoaded();
                return _isMuted;
            }
        }

        /// <summary>
        /// 初回アクセス時に PlayerPrefs から状態を復元する。
        /// </summary>
        private static void EnsureLoaded()
        {
            if (_isLoaded) return;

            // JS版の localStorage と同様、"true" という文字列ならミュート有効
            string saved = PlayerPrefs.GetString(STORAGE_KEY_MUTED, "false");
            _isMuted = (saved == "true");
            _isLoaded = true;
        }

        /// <summary>
        /// ミュート状態をトグル反転し、PlayerPrefs に保存する。
        /// </summary>
        /// <returns>切り替え後のミュート状態</returns>
        public static bool ToggleMute()
        {
            EnsureLoaded();
            _isMuted = !_isMuted;
            PlayerPrefs.SetString(STORAGE_KEY_MUTED, _isMuted ? "true" : "false");
            PlayerPrefs.Save();
            return _isMuted;
        }

        /// <summary>
        /// ミュート状態を明示的に設定して保存する (テスト・初期設定用)。
        /// </summary>
        public static void SetMuted(bool muted)
        {
            EnsureLoaded();
            _isMuted = muted;
            PlayerPrefs.SetString(STORAGE_KEY_MUTED, _isMuted ? "true" : "false");
            PlayerPrefs.Save();
        }
    }
}
