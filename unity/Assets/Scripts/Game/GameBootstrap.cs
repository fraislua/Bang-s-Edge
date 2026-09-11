using UnityEngine;

namespace BangsEdge.Game
{
    /// <summary>
    /// シーンロード完了時に自動実行され、カメラ、レターボックス、ゲームループ、描画系を実行時に構築するブートストラップ。
    /// </summary>
    public static class GameBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void OnSceneLoaded()
        {
            // 1. 盤面カメラの取得または生成
            Camera boardCamera = Camera.main;
            if (boardCamera == null)
            {
                var camGo = new GameObject("BoardCamera");
                boardCamera = camGo.AddComponent<Camera>();
            }

            boardCamera.name = "BoardCamera";
            boardCamera.orthographic = true;
            boardCamera.orthographicSize = 540f;
            boardCamera.transform.position = new Vector3(960f, -540f, -10f);
            boardCamera.clearFlags = CameraClearFlags.SolidColor;
            boardCamera.backgroundColor = new Color(5f / 255f, 6f / 255f, 10f / 255f, 1f);
            boardCamera.depth = 0;

            // 2. 黒帯描画用カメラの生成 (画面全体を黒でクリア)
            var blackBarsGo = new GameObject("BlackBarsCamera");
            var blackCam = blackBarsGo.AddComponent<Camera>();
            blackCam.orthographic = true;
            blackCam.clearFlags = CameraClearFlags.SolidColor;
            blackCam.backgroundColor = Color.black;
            blackCam.cullingMask = 0; // 何も映さず黒背景のみ
            blackCam.rect = new Rect(0f, 0f, 1f, 1f);
            blackCam.depth = -10; // 盤面カメラより前にレンダリング

            // 3. レターボックス管理コンポーネントのアタッチ
            var letterbox = boardCamera.gameObject.AddComponent<LetterboxCamera>();
            letterbox.Initialize(boardCamera);

            // 4. ゲームマネージャー、レンダラー、およびHUDの生成と初期化
            var managerGo = new GameObject("GameManager");
            var renderer = managerGo.AddComponent<GameRenderer>();
            var hudGo = new GameObject("GameHud");
            var hud = hudGo.AddComponent<GameHud>();
            var manager = managerGo.AddComponent<GameManager>();

            hud.Initialize(boardCamera);
            renderer.Initialize();
            manager.Initialize(renderer, letterbox, hud);

            // シーン再読込時にも保持
            Object.DontDestroyOnLoad(boardCamera.gameObject);
            Object.DontDestroyOnLoad(blackBarsGo);
            Object.DontDestroyOnLoad(managerGo);
            Object.DontDestroyOnLoad(hudGo);
        }
    }
}
