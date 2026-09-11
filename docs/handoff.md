---
ファイル名: handoff.md
ステータス: 引き継ぎメモ(セッション間で随時更新)
最終更新日: 2026年9月11日(unityroomで公開し、ランキングを入れた時点)
---

# セッション引き継ぎメモ

前回セッションの終了時点の状態と、次に着手できることをまとめたもの。**新しいセッションはまずこれを読むこと。** 詳細は各ドキュメントにあるので、ここには要点と「知らないとハマること」だけを書く。決定事項の経緯は`docs/unity-port-plan.md` §5、実測と気づきは`docs/dev-notes.md`、委任の成否は`docs/model-experiment-log.md`。

## 1. 現在の状態

**Unity版をunityroomで公開した(2026-09-11)。ランキング2つ(スコア、ビッグバン最短時間)も動いている(ユーザーが反映を確認)。**

- **GitHub**: `fraislua/Bang-s-Edge`は2026-09-11にいったん非公開にしたが、unityroomへの公開とスマホ対応が済んだので**公開に戻すとユーザーが決めた**(切り替えはユーザーが行う。公開前の点検結果は`docs/dev-notes.md`)。GitHub Pagesの設定は残っていない(`has_pages: false`)ので、公開してもJS版は自動では出ない。PagesでJS版を再公開するかは未決定。**すべてpush済み**(未pushは`git log --oneline origin/main..HEAD`で確認)。
- **未追跡の`gif/`**: ユーザーがアイコン用に作った素材。コミットするかは聞いていない(触らない)。
- **JS版**(リポジトリ直下): 以前の遊び(揺動あり)のまま残す(ユーザー判断)。**物理のロジックの基準**で、`golden.js`はこれを動かしてUnity版の基準データを作る。確認するときは`python -m http.server <port> --bind 127.0.0.1`で配信する。
- **Unity版**(`unity/`): Unity 6000.3.11f1、URP(Universal 2D)、C#、WebGL。**JS版から意図的に変えた点**(すべて`docs/unity-port-plan.md` §5):
  - HUD全体を2倍(`GameHud.HudScale`、ユーザー方針「少し大きすぎるくらい」)
  - 音量スライダー(ミュートボタンの左)。そのため`audio.jspre`は`audio.js`に音量関数を足したもの
  - 「射程内 N / 200」は満数を下回ったときだけ表示
  - **揺動を止め(`WOBBLE_AX`/`WOBBLE_AY`=0)、`TAU_R`を3→8**(揺動があると「中央で約8秒で離す」で解けたため。ユーザーが試遊して採用)
  - 背景の遠い星と重力レンズ(`StarfieldRenderer.cs`。危険度で歪み、離すと一度だけ逆に歪んで戻る。稀に流れ星)
  - 起動時のUnityロゴ画面を外した
  - **unityroomランキング**: ボード1=確定したスコア(降順)、ボード2=押してから爆発までの秒(昇順、小数点以下2桁)。ラウンドごとに自動送信。爆発画面にBANG TIMEと自己ベスト

### スマホ対応(2026-09-11、unityroomに公開済み。ユーザーがスマホで動作と表示を確認)

- 決定と理由は`docs/unity-port-plan.md` §5-10(指の少し上に集める・実機はAndroidのChrome。当初の「横向き前提」は、unityroomでは縦向きの判定が働かないと分かり、ユーザー判断で外した)。
- **対応前の公開版はタッチでは遊べなかった**(長押ししてもラウンドが始まらない。ヘッドレスChromeのタッチ再現で確認、`docs/dev-notes.md`)。
- 入れたもの: 入力を`Pointer.current`に / タッチのときだけ引き寄せる点を指の60 CSS px上へ(`GameManager.TOUCH_CURSOR_OFFSET_CSS_PX`、試遊で調整) / ミュートとスライダーの当たり判定を上下に44 CSS pxまで拡大 / 案内文をTOUCH・TAPに切り替え / 描画の解像度を端末の値に戻す(`pixel-ratio.jspre`、unityroomがスマホで1倍に固定するため) / `touch-gestures.jspre`(長押しの文字選択・メニュー、スクロール、タップ後の疑似マウスを抑止) / `audio-unlock.jspre`(指を離したときにも音を開始) / `BangsDisplay.jslib`(キャンバスのCSS倍率とcoarse pointer)。
- **ヘッドレスChromeのタッチ再現**(`node tools/web-shot/cdp-touch-check.mjs <url> <outDir> <name> [width] [height] [dpr]`)と、**AndroidのChrome実機**(ユーザーの古めの端末。iPhoneは無いのでiOSは確認できない)で確認済み: 読み込み・TOUCH/TAPの文言・ずらす量(60 CSS pxで問題なし)・長押しで文字選択やスクロールが出ない・音(1回目は離すまで鳴らず、2回目から鳴る)・ボタン・縦向きの案内と復帰。
- **実機でアドレスバーに画面の下(fps)が隠れた** → テンプレートのキャンバスの高さ`100vh`を`100%`+`100dvh`に直して解消(`docs/dev-notes.md`)。**これは手元の確認用ページだけの修正で、unityroomは自前のページを使う。**
- 実機への配信は`python -m http.server 8770 --bind 0.0.0.0 --directory <ビルド>`で、スマホから`http://<PCのLAN内のアドレス>:8770/`(スマホはPCと同じルーターにつなぐ。Pythonの受信許可はプライベート・パブリック両方に登録済み)。
- **unityroomに投稿してユーザーが確認した(1回目)**: 縦向きのまま遊べた / 横にすると画質が落ちた。原因はunityroomのゲームページで、スマホでは描画を1倍の解像度に固定し、キャンバスは縦向きでも横長で置く(`docs/dev-notes.md`)。**ユーザー判断で縦向きの判定と案内を外し**、`pixel-ratio.jspre`で解像度を端末の値(上限3)に戻した。unityroomのページを写した確認用ページは`unity/Build/WebGL/ur-test.html`(ビルドで上書きされないが、Buildフォルダを消すと消える。gitの対象外)。
- **修正版をunityroomへ出し直し、ユーザーがスマホで動作と表示を確認した**(横向きでも画質が落ちず、縦向きでも遊べる)。unityroomのページの黒帯や配置はビルドからは変えない。表示の大きさに不満が出たら、ゲーム内の全画面ボタン(AndroidのChromeならボタンを押したときに全画面にできる)などを検討する。**iPhoneでの挙動は未確認**(確かめる端末が無い)。

### unityroomへ公開ビルドを出す手順(次の更新でもこのとおりにする)

1. **HMACキーを置く**: `unity/Assets/Resources/Secrets/unityroom-hmac.txt`にキーの文字列だけを書く。**gitignore済みでコミットしない**。無いと送信しない(警告ログのみ)。Brainの確認用にダミーのキーを置いたら、確認後に必ず消す(残すとダミーで公開してしまう)。
2. **ユーザーがエディターからビルドする**: ビルドプロファイル`Assets/Settings/Build Profiles/Web - Mobile - Release.asset`(Gzip、Decompression Fallbackオフ、Wasm 2023の3項目オン)。**Brainのバッチビルド(出力`WebGL-gzip`、ファイル名に大文字)は、アップロードの最後に「ビルドファイルをアップロードしてください」で弾かれた**(原因は未確定)。バッチで作るなら出力フォルダ名を小文字にする。
3. **unityroomのWebGL設定**: Unityのバージョンで**6000.3を選んで保存してから**アップロードする(未選択だと`UnityLoader.js`・`*.unityweb`の古い枠が出る)。割り当てメモリは既定256MB(実測121.8MBで一定)。「ドット絵をはっきり表示する」はオフ。
4. **出すファイル**: ビルドの`Build/`にある`.loader.js`・`.data.gz`・`.framework.js.gz`・`.wasm.gz`の4つ(Gzipで約23MB。縮小はユーザー判断で行わない)。
5. 説明文にフォントのクレジット(BIZ UDゴシック、SIL OFL 1.1)。アイコンのGIFはユーザーが作る。

### Unity版の構成

| 場所 | 中身 |
|---|---|
| `unity/Assets/Scripts/Simulation/` | 物理とゲーム状態。**UnityEngineに依存しない純粋なC#**(asmdefで`noEngineReferences`)、倍精度。`BangSimulation`(`RoundSteps`/`LastBangSteps`/`BestBangSeconds`でビッグバン時間を固定ステップで数える)・`GameConfig`・`Mulberry32` |
| `unity/Assets/Scripts/Game/` | コードから実行時に組み立てる(シーンは編集しない)。`GameBootstrap`・`GameManager`(ループ・入力・ハイスコアと自己ベストの保存・状態遷移でランキング送信)・`LetterboxCamera`・`GameRenderer`・`StarfieldRenderer`・`GameHud`・`AudioMuteManager`・`WebAudioEvents`・`UnityroomRanking` |
| `unity/Assets/Plugins/WebGL/` | `audio.jspre`(`audio.js`+音量)・`audio-unlock.jspre`・`BangsAudio.jslib` |
| `unity/Assets/Resources/Fonts/` / `Secrets/` | BIZ UDゴシック通常・太字(OFL同梱) / HMACキー(gitignore) |
| `unity/Assets/Settings/Build Profiles/` | 公開に使ったビルドプロファイル |
| `unity/Assets/Editor/WebGLBuild.cs` | バッチビルド用(`-webglCompression`はビルド後に元へ戻す。プロジェクト既定はGzip) |
| `unity/Assets/Tests/EditMode/` | 数値一致テスト10件と基準データ(`Golden/`) |
| `unity/Packages/manifest.json` | `com.unityroom.client`(v0.9.6、gitのタグで固定)を追加済み |
| `tools/sim-harness/golden.js` | 本物の`script.js`を固定シードで回して基準データを作る。`GameConfig.cs`と`config.js`の差分は意図した3項目(揺動2つと`TAU_R`)だけを読み込み時に上書きし、それ以外の差があれば止まる |
| `tools/sim-harness/bang-timing.js` | 「一定秒数で離す」と「危険度を見て離す」の平均点を、揺動・設定・操作ごとに比べる |
| `tools/web-shot/` | `cdp-shot.mjs`(撮影。`HOLD_X/HOLD_Y`で押す位置、第7引数でHUD倍率)・`cdp-audio-check.mjs`(音・ミュート・音量スライダー)・`cdp-early-frames.mjs`(起動直後を0.5秒おきに撮る)・`serve-gzip.mjs`(Gzipビルドを正しいヘッダーで配信) |

### よく使うコマンド(Unityエディタを閉じた状態で。開いているとバッチ実行はロックで失敗する)

```
unity test "<リポジトリ>\unity" --mode EditMode --output <xml>
unity build "<リポジトリ>\unity" --target WebGL --execute-method WebGLBuild.Build -o "<リポジトリ>\unity\Build\WebGL" --args "-webglCompression Disabled" --no-tail
python -m http.server 8765 --bind 127.0.0.1 --directory "<リポジトリ>\unity\Build\WebGL"   (無圧縮ビルド。ユーザーは Ctrl+F5)
node tools/web-shot/serve-gzip.mjs <Gzipビルドのフォルダ> 8767                            (Gzipビルドはこちら)
node tools/sim-harness/golden.js                                       (JS版かGameConfig.csを変えたら作り直す)
node tools/sim-harness/bang-timing.js --seeds 200 --variants "noWobble+TAU_R=8"
node tools/web-shot/cdp-shot.mjs <url> 1920 1080 <outDir> <name> [holdMs] [hudScale=2]
node tools/web-shot/cdp-audio-check.mjs <url> <outDir> <name> [bangHoldMs] [hudScale=2]
```
`unity`は`C:\Users\ryoga\AppData\Local\Unity\bin\unity.exe`(CLI 1.0.0-beta.9)。ビルドは2分前後。**セッションが再開すると、バックグラウンドで動かしていた配信は止まっている**ので立ち上げ直す。

### 実測値(Unity版の設定)

- 中央付近で押し続けた発火は平均9.86秒・標準偏差1.04秒(200シード)。「一定秒数で離す」は理論上の最高点の72〜73%、「危険度を見て離す」は84〜92%。
- ビッグバン時間は1/60秒刻み(発火は5ステップ連続でしきい値超え)で、送信と表示は小数点以下2桁。
- WebGLのゲーム本体のメモリは121.8MBで一定。Gzipビルドは約23MB(フォント8.9MB、Unityロゴ画像2.7MBは表示しないが同梱、URPのポストエフェクト素材約3MB)。

## 2. このプロジェクトの2つの目的

1. 長押しで遊ぶゲームを作る(PCはマウス、スマホはタッチ。仕様: `docs/game-concept.md`)
2. **Claude Codeからどれだけタスクを分散できるかの実験**。Claude CodeはBrain(実装せず、分解・委任・検証・記録を担う)として動き、agyとdiscord-ai-hubに委任する。記録は`docs/model-experiment-log.md`。

## 3. 最初に押さえる運用ルール(詳細は`CLAUDE.md`と`docs/workflow.md`)

- **ユーザーへの報告・まとめは日本語で書く**(コミットメッセージは既存どおり英語)。
- **Opus5への切り替えはユーザー操作が必要**。実装フェーズに入る前に確認する。
- **委任するときはスキルを読む**: agyは`.claude/skills/agy-delegation/SKILL.md`、discord-ai-hubは`.claude/skills/discord-ai-hub/SKILL.md`。
- **委任前に`docs/model-experiment-log.md`を読み、委任後に結果と選択理由を追記する**(後回しにしない)。
- **データ共有**: *赤い粒子を長押しで集めビッグバン直前に離して密度を競う*という発想とタイトルだけが`gpt-5.6-*`に非公開(unityroomでは公開済みだが、ルールはユーザーが変えるまで守る)。
- Vertex AIを使ったら`docs/cost-log.md`に記録する。
- **コミットは適宜行ってよい**。**pushはユーザーに確認する**。
- 見た目・遊び心地の変更は、ビルドを作ってユーザーに試遊してもらってから決める(星の演出・揺動の停止はこの流れで決めた)。
- Unityの公式スキル31個をユーザー全体に導入済み。`setup-vivox-voice-chat`はSnyk評価Criticalで、残すかは未決定。

## 4. 知らないとハマること(実測済み。詳細は`docs/dev-notes.md`)

### agy

- **報告を信用しない。** `git status`・差分・テスト・撮影で検証する。
- **書き込ませるには`--mode accept-edits`が必須。** ターミナルは使えないので、コンパイル・テスト・ビルドはBrainが行う。
- **既存の長いメソッドへの追記では、ローカル変数名の重なり(CS0128)に気づけない**(音量スライダーで発生)。プロンプトに「追加する変数には用途の分かる名前を付ける」と書くと以後は起きていない。
- **`Mathf.Lerp`は0〜1に丸める**ので、負の値を補間するときは`LerpUnclamped`を指定する。
- **長いファイルを書き写させない。** **近似の判断を任せない**(`shadowBlur`の代わりの`Outline`+`Shadow`で文字が重なった)。
- Brainの並行作業と触るファイルが重ならなければ、同時に進めても衝突しなかった(ランキング)。

### Unity

- **子オブジェクトは親の拡大率を引き継ぐ** / **9スライスは`referencePixelsPerUnit`(100)基準** / **実行時テクスチャの色は不透明度を掛けずに書く** / Universal 2Dの既定はLinear色空間(Gammaに変更済み) / 既定のWebテンプレートは960×600固定(FullWindowに変更済み) / 太字ファイルの無いフォントは合成の太字で潰れる / `JsonUtility`はdoubleを1ulpずれて読む。
- **`unity build`はWebGLを`--target`だけでは作れない**(`--execute-method`か`--profile`)。
- **WebGLでは`OnAudioFilterRead`もミキサーの効果も使えない**(音はJS版をjslibで流用)。
- **unityroomの公式クライアントはシーンに置く前提**(非公開フィールド`HmacKey`、Awakeで検査)。シーンを編集しないので、非アクティブのGameObjectに追加→リフレクションでキー→有効化、としている。ライブラリはボードごとに最短6秒間隔で送り、応答が返らないと6秒おきに送信を始め直す(手元のサーバーで観測)。
- **エディターからビルドすると、Input Systemが`ProjectSettings.asset`の`preloadedAssets`に`InputSystem_Actions.inputactions`を足す**(このゲームは使っていないが害は無い)。`GraphicsSettings`の`m_LightsUseLinearIntensity`も変わった(ライト未使用)。
- **起動時のロゴ画面は`m_ShowUnitySplashScreen: 0`で消える**が、ロゴ画像はビルドに残る。消えたかは`cdp-early-frames.mjs`で確かめる。

### 検証

- **静止した見た目と音の呼ばれ方はBrainが確かめられる**(`tools/web-shot/`)。動き(星の戻り方・流れ星)・fps・聴感はユーザーに頼む。
- **ゲームの性質は`bang-timing.js`で点数として測れる**。操作モデルは粗い近似なので、採用前にユーザーの試遊を挟む。
- **検証そのものを疑う。** 今回踏んだもの: 照合できたキーが0件の比較(字下げのずれ)、止まっていた配信に対する確認(応答000で何も読み込まれていなかった)、jqの正規表現のエスケープ。**「何件照合したか」「応答コード」を毎回出す。**
- **Windows環境**: **Bashで`cd`すると以後の作業ディレクトリが変わる**(今回も2回踏んだ)ので`git -C`や絶対パスを使う。PowerShell 5.1はネイティブコマンドへの引数内のダブルクォートを崩す。

## 5. 未解決・保留事項

1. **表示サイズと小さいウィンドウ**: unityroomの既定960×540では粒子が1〜2pxになる。投稿時に選んだサイズは記録していない。
2. **揺動ありを別の遊び方として出すか**: ユーザーは「以前のランダム性にも別の面白さがあった」と言っている(JS版は揺動ありのまま)。
3. **ゲームバランス**(`R_MAX_RATIO`・`F_CREEP`・`TAU_R`)は煮詰め途中。易しくする方向は確認してから。
4. **仕様書の更新**: `docs/game-concept.md`の「音」の節が古い。Unity版だけの要素(HUD倍率・音量・星・ランキング)は仕様書に無く、`docs/unity-port-plan.md` §5にある。未確定事項2つ(「一度離したら即終了」で良いか、ビッグバン演出の作り込み)も残る。
5. **GitHubの扱い**(2026-09-11にユーザーが決定): 公開に戻す(切り替えはユーザー) / ライセンスはMIT(同梱フォントはOFLのまま) / **JS版はGitHub Pagesで再公開する**。画面の右下に「開発途中(調整段階)の旧バージョン」とunityroomへのリンクを出し、Jekyllの変換を止める`.nojekyll`を置いた。**Pagesの有効化は、リポジトリが公開になってからBrainが行う**(無料プランは非公開のリポジトリでPagesを使えない)。unityroomのURLは https://unityroom.com/games/bang-s-edge 。残っているのは、Unity版の到達点にタグを打つか(既存は`v1`・`v1.1`) / コミットの作者欄のメールアドレスをそのままにするか(`docs/dev-notes.md`。Brainは書き換えないことを推奨)。
6. **165Hz表示では動きが60Hzに量子化される**(固定ステップの副作用)。指摘は無し。
7. **ビルドの縮小案**(フォントを使う文字だけにする8.9MB減など)はユーザー判断で見送り。容量で困ったら再検討。
8. **細かい残り**: Unity Cloudの紐付け(`cloudProjectId`)を外すか / 不要パッケージ(Visual Scripting等)の整理 / vivoxスキルを残すか / MCPの未検証2点(推論トークンだけで出力上限を使い切る経路、326秒超の`compare`)。

## 6. Brain自身のコストについて

- Opus5/XHighで、v1.1完成時点で5時間制限の約40%を消費(ユーザー観測)。2026-09-10後半からOpus5/High。
- 2026-09-11(移植〜unityroom公開)は途中で数回セッションが再開された長いセッションで、消費率は未観測。

## 7. 実験で分かったこと(次回の委任判断に使う)

### 委任

- **仕様が数値まで確定していれば、軽量モデル(`gemini-3.8-flash-high`)で通る。** JS版で7回、Unity版で10回。コンパイルエラーは音量スライダーの1回だけ(ローカル変数名の重なり)。
- **移植は「数値一致テストを先に用意」してから委任する。** 報告を読まずに合否が決まる。
- **委任先の誤りの多くは「移植先の環境の約束事」で起きた。** プロンプトに約束事を書く(`.claude/skills/agy-delegation/SKILL.md`)。
- **見た目の数値はBrainの指定が外れることがある**(星が暗すぎた・レンズが小さすぎた)。撮影で確かめてから渡す。
- **外部ライブラリを使わせるときは、Brainが先にソースを読んで使い方を決めてから渡す**(unityroomクライアントの初期化順)。
- 数行〜十数行の修正はBrainが直接直す。重いモデルの設計でも数式は検算する。`gpt-5.6-sol`は数式の設計に強い。

### ユーザーとの協働

- **欠陥の多くは、ユーザーの体感が先に捉えていた**(光の円が大きい、文字がぼやける、BIG BANGが重なる、射程内が変わらない)。
- **Brainの計測は、ユーザーの体感を定量化して選択肢を示すときに最も効いた**(揺動の件は計測→候補の比較→試遊→採用)。ただし数値の結論と体感は一致しないことがある(「予想しやすくなったが固定された感じも強い」)。**決めるのはユーザー。**
- ユーザーは物理的なもっともらしさを重視する(「ブラックホールは見え方だけを変える」)。演出を物理から外すときは、そう伝える。
