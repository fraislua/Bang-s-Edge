using System;
using System.Reflection;
using UnityEngine;
using unityroom.Api;

namespace BangsEdge.Game
{
    /// <summary>
    /// unityroom ランキングへのスコア送信を管理するクラス。
    /// </summary>
    public static class UnityroomRanking
    {
        public const int BoardScore = 1;
        public const int BoardBangTime = 2;

        private static bool _enabled;
        private static bool _isInitialized;

        /// <summary>
        /// ランキングクライアントの初期化を行います。
        /// </summary>
        public static void Initialize()
        {
            if (_isInitialized) return;
            _isInitialized = true;

            var hmacAsset = Resources.Load<TextAsset>("Secrets/unityroom-hmac");
            string rawKey = hmacAsset != null ? hmacAsset.text : null;
            string trimmedKey = rawKey != null ? rawKey.Trim() : null;

            if (string.IsNullOrEmpty(trimmedKey))
            {
                Debug.LogWarning("[Ranking] HMACキーが無いのでランキングに送信しません (Assets/Resources/Secrets/unityroom-hmac.txt)");
                _enabled = false;
                return;
            }

            var go = new GameObject("UnityroomApiClient");
            go.SetActive(false);

            var apiClient = go.AddComponent<UnityroomApiClient>();
            FieldInfo hmacField = typeof(UnityroomApiClient).GetField("HmacKey", BindingFlags.NonPublic | BindingFlags.Instance);
            if (hmacField == null)
            {
                Debug.LogError("[Ranking] UnityroomApiClient に HmacKey フィールドが見つかりません。ライブラリの仕様が変更された可能性があります。");
                UnityEngine.Object.Destroy(go);
                _enabled = false;
                return;
            }

            hmacField.SetValue(apiClient, trimmedKey);
            go.SetActive(true);
            _enabled = true;
        }

        /// <summary>
        /// 通常スコア (ボード1: 降順) を送信します。
        /// </summary>
        public static void SendScore(int finalScore)
        {
            if (!_enabled || finalScore <= 0) return;

            if (UnityroomApiClient.Instance != null)
            {
                UnityroomApiClient.Instance.SendScore(BoardScore, finalScore, ScoreboardWriteMode.HighScoreDesc);
            }
        }

        /// <summary>
        /// ビッグバン到達タイム (ボード2: 昇順) を送信します。
        /// </summary>
        public static void SendBangTime(double seconds)
        {
            if (!_enabled || seconds <= 0.0) return;

            if (UnityroomApiClient.Instance != null)
            {
                UnityroomApiClient.Instance.SendScore(BoardBangTime, (float)Math.Round(seconds, 2), ScoreboardWriteMode.HighScoreAsc);
            }
        }
    }
}
