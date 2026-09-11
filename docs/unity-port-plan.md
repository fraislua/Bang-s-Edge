---
ファイル名: unity-port-plan.md
ステータス: 確定(2026-09-11、§5の3点をユーザーが決定)
最終更新日: 2026年9月11日
---

# Unity移植計画

JS版(リポジトリ直下)を、`unity/`のUnity 6000.3.11f1・URP(Universal 2D)・C#へ移植し、WebGLで動かすための方針。Brainがタスクを分割し、実装は原則agyへ委任する(`.claude/skills/agy-delegation/SKILL.md`)。

## 0. 方針

- **同じ遊びをそのまま移す。** 数式・定数・更新の順序はJS版と完全に揃える。新しい要素(Bloomなどの見た目の追加、バランス調整)は、移植が終わって比較できる状態になってから別に扱う。
- **JS版は移植中は変更しない。** 基準がずれると比較できなくなるため。JS版のバグを見つけたら記録だけして、移植後に両方へ直す。
- **物理の固定ステップ1/60秒と論理盤面1920×1080は変えない。** `FIXED_DT`を変えるとビッグバンまでの時間が約10%変わる(`docs/handoff.md` §4)。
- **音は`audio.js`が正本。** `docs/game-concept.md`「音」の節は、grokの設計で作り直す前の記述が残っている(例: ドローンを三角波130→300Hzと書いているが、現行は110Hz基準の7ノード構成)。
- **シーンやアセットを手で書かない前提で組む。** agyは`.unity`/`.prefab`/`.asset`を安全に編集できず、Unityエディタも起動できない。ゲームの構成は**C#コードから実行時に組み立てる**(`[RuntimeInitializeOnLoadMethod]`で起動し、既存の`SampleScene`は空のまま使う)。

## 1. 構成の対応表

| JS版 | Unity版 | 補足 |
|---|---|---|
| `config.js` の `CONFIG` | `GameConfig.cs`(静的クラスの定数、名前は同じ) | 派生定数(`MEASURE_AREA`・`DENSITY_MAX`・`BANG_THRESHOLD`・`REQUIRED_PARTICLES`)も同じ式で持つ |
| `script.js` の物理・状態(`initParticles`/`startRound`/`confirmRound`/`triggerBigBang`/`update`/`handleBoundaryBounce`) | `BangSimulation.cs`(**UnityEngineに依存しない純粋なC#**) | 数値は`double`で持つ(JSと同じ倍精度)。乱数は外から渡す(§3)。これによりEditModeテストでJS版と数値比較できる |
| `loop()`(アキュムレータ・`MAX_SUBSTEPS`・0.25秒の切り捨て・`simNow`) | `GameLoop.cs`(MonoBehaviour)の`Update()`内に**自前のアキュムレータ** | Unityの`FixedUpdate`は使わない。取りこぼし時の挙動(JS版は残りを捨ててスローモーションにする)が異なるため |
| `resize()`・`viewScale`/`viewOffset`・レターボックス | 正投影カメラ(論理1920×1080)+ `Camera.rect`で黒帯 | 盤面の外に描かれないので、JS版の`clip()`は不要になる |
| 座標系(**y軸が下向き**) | シミュレーションはJS版の座標のまま。**描画と入力の境界でだけy軸を反転**する | 物理コードに座標変換を混ぜない |
| ポインター入力(`pointerdown`/`move`/`up`/`cancel`) | Input System(プロジェクトは新Input Systemのみ: `activeInputHandler: 1`)の`Mouse.current` | `pointercancel`相当は、フォーカス喪失(`OnApplicationFocus(false)`)で`confirmRound`する |
| `localStorage` のハイスコア | `PlayerPrefs`(キーは同じ`bangs_edge_high_score`) | WebGLではIndexedDBに保存される |
| 描画: 粒子3パス+発光層、集積円(最大半径1432px)、点線の測定円、十字、フラッシュ、画面振動、盤面の枠 | 粒子はコードで生成した円テクスチャのスプライト、円は`LineRenderer` | 色・半径・透明度はJS版の値をそのまま使う。**画面振動の乱数はシミュレーションと別の乱数にする**(§3) |
| HUD(密度バー・段階表示・射程内の個数と警告・fps・SCORE/HIGH・待機/確定/ビッグバン画面) | uGUIをコードで組み立て、**日本語フォントを同梱**(§5-2) | **BIZ UDゴシック**の通常と太字(`unity/Assets/Resources/Fonts/BIZUDGothic-Regular.ttf`/`-Bold.ttf`、各4.6MB、google/fonts)を同梱。ライセンス(SIL OFL 1.1)は同じフォルダの`BIZUDGothic-OFL.txt`で、Resources内にあるのでビルドにも含まれる。太字は太字ファイルで描く(当初のNoto Sans JPは太字ファイルが無く、合成の太字で漢字が潰れたので差し替えた。等幅でJS版の見た目にも近い)。**💥はフォントに無いので付けない**(「BIG BANG DETECTED!」のみ。JSの赤いぼかしはuGUIの縁取りで近づける)。★と—は含まれる。unityroomなどで公開するときはクレジットにフォント名とライセンスを書く |
| ミュートボタン(右上28px、押してもラウンドを始めない) | 同じ位置・同じ判定 | |
| `audio.js`(Web Audioの合成音: ドローン7ノード・警告パルス・爆発3層・確定和音) | **`audio.js`をjslibプラグインとして流用**(§5-1) | C#からは`[DllImport("__Internal")]`で呼ぶ。エディタ上では何もしない代替実装に切り替える(`#if UNITY_WEBGL && !UNITY_EDITOR`) |

## 2. 作業分割と委任先

モデルの選択は`docs/model-experiment-log.md`の実績による。数値まで決まっている転記型の実装は`gemini-3.8-flash-high`で7回続けて通っている。

| 段階 | 内容 | 担当 | 検証 |
|---|---|---|---|
| P0 | 検証の土台: 固定シードの乱数でJS版をNodeで回し、基準データ(JSON)を出すハーネス。C#側のEditModeテストの雛形 | Brain | ハーネスが本物の`script.js`を読み込み、乱数の差し替えが効いていることを実行前にアサートする |
| P1 | `GameConfig.cs` と `BangSimulation.cs` | agy / gemini-3.8-flash-high | P2のテスト |
| P2 | 物理の一致テスト(§3)を書いて `unity test` で実行 | Brain | 短期の数値一致と、発火時刻の統計の一致 |
| P3 | `GameLoop.cs`: 起動処理・アキュムレータ・入力・カメラとレターボックス・ハイスコア | agy / gemini-3.8-flash-high | コンパイルとEditModeテスト(Brain)、WebGLで操作(ユーザー) |
| P4 | 描画: 粒子・発光層・集積円・測定円・フラッシュ・画面振動・枠 | agy / gemini-3.8-flash-high | WebGLビルドで見た目とfps(ユーザー) |
| P5 | HUDとミュートボタン(日本語フォントの同梱を含む。フォントファイルの取得と配置はBrain) | agy / gemini-3.8-flash-high | 同上 |
| P6 | 音: `audio.js`のjslib化と、C#側の呼び出し・エディタ用の代替実装 | agy | ユーザーがWebGLビルドで聴き、JS版と比べる |
| P7 | WebGLビルドの設定(圧縮形式・テンプレート)と通しプレイ | Brain+ユーザー | JS版と並べて遊ぶ |

P1とP3〜P5は、P2の一致テストが通ってから順に進める。物理がずれたまま描画を載せると、見た目の違和感の原因が切り分けられなくなる。

**進捗(2026-09-11)**: P0〜P2完了。EditModeテスト10件が通過(乱数と定数はビット一致、短期は1e-9以内で一致、200シードの発火時刻の分布も一致)。**P3とP4は1回の委任にまとめた**(描画が無いとユーザーが確認できず、分けても検証の手段が増えないため)。P3+P4はユーザー確認済み(全画面テンプレート、Gamma色空間、光の円の大きさを修正)。**P5もユーザー確認済み**(フォントをBIZ UDゴシックに変更、ビッグバンの文字の縁取りは外した。UIの大きさなどの微調整は音が入ってから行う)。**P6着手**: `audio.js`はBrainが`unity/Assets/Plugins/WebGL/audio.jspre`へファイルごとコピー(711行をモデルに書き写させない)し、押下イベントの中で`init()`を呼ぶ`audio-unlock.jspre`も用意。C#側の中継はagyに委任。**P6の接続はヘッドレスChromeで検証済み**(音のメソッドの呼ばれ方がJS版と一致、ミュートの保存と再読み込みも一致)。聴感はユーザー確認待ち。

### P6(音)の設計メモ

`audio.js`は即時関数で`window.AudioController`(`init`/`resume`/`updateWarning`/`playBigBang`/`playResolve`/`toggleMute`/`isMuted`/`startDrone`/`stopDrone`/`updateDrone`)を公開する作り。これをそのまま使う。

- **`audio.js`は`.jspre`としてビルドに含める**(Unityのフレームワークの前に連結され、`window.AudioController`がそのまま定義される)。中身はJS版の`audio.js`の写し。JS版は凍結中なので、写しが食い違う心配は移植中は無い。
- **C#からの呼び出しは薄い`.jslib`**(`AudioController`の各メソッドを呼ぶだけの関数)と、`[DllImport("__Internal")]`で`IAudioEvents`を実装するクラス。エディタとWebGL以外では何もしない実装に切り替える。
- **自動再生ポリシー**: ブラウザは、ユーザー操作のイベント処理の中でないと`AudioContext`を開始させないことがある。Unityの入力は次のフレームで処理されるので、イベント処理の外になる。**`.jspre`側で`document`の`pointerdown`を直接受け、その場で`AudioController.init()`を呼ぶ**(JS版も押下のたびに`init()`を呼んでいる)。
- **ミュートの状態が二重になる**: P5のミュート状態はUnityの`PlayerPrefs`(WebGLではIndexedDB)に保存するが、`audio.js`は`localStorage`の同じキー`bangs_edge_muted`を読む。WebGLでは`AudioController.isMuted()`を正とし、切り替えは`toggleMute()`を呼んで結果を反映する。

## 3. 検証方法

### 3.1 物理の一致(Nodeで回すJS版と、EditModeテストのC#版を比べる)

- **乱数を揃える。** JS版は`Math.random`を、C#版は同じアルゴリズムの乱数(例: Mulberry32)を使い、同じシードから始める。ハーネスでは`Math.random`をシード付きの関数に差し替える。
- **乱数を呼ぶ順序も揃える。** シミュレーション内の呼び出しは、初期配置(一様配置→クラスタ中心→正規分布)、射程外の揺動(粒子ごとにx→y)、危険度CRITICALの画面振動、ビッグバンの飛散(角度→速さ)。**描画側の画面振動はJS版では`draw()`内の`Math.random`だが、Nodeハーネスは`draw()`を呼ばないので、C#版では描画用に別の乱数を使う。**
- **短期の数値一致**: 同じ初期配置・同じカーソル操作で1・10・60ステップ後の全粒子の位置と速度を比べる。`Math.exp`/`Math.cos`の最終桁はJSのエンジンと.NETで一致する保証がなく、この系は差が時間とともに拡大するので、**完全一致を求めるのは短い区間だけ**にする。
- **統計の一致**: 複数のシードで「中央付近で微細振動」のポリシーを回し、ビッグバンまでのゲーム内時間の分布を比べる(JS版の実測は中央静止で約10秒)。許容差はP0で両方の分布を見てから決める。
- 人間のプレイを模したポリシーの注意(`docs/handoff.md` §4): 測りたい変数以外を固定した対照を置く。静止ではなく微細振動を基準にする。

### 3.2 コンパイル・テスト・ビルド

- agyはUnityを起動できないので、**委任のたびにBrainが`unity test "<リポジトリ>\unity" --mode EditMode`でコンパイルとテストを確認する**。
- WebGLビルドは`WebGLBuild.Build`(`docs/dev-notes.md`)。

### 3.3 見た目・操作感・音

- Chrome拡張が未接続のため、**ユーザーがWebGLビルドを遊んで確認する**。JS版(公開中)と並べて比べてもらう。
- 手元でビルドを開くにはHTTPサーバーが要る(`file://`では動かない)。Brotli圧縮はサーバー側の設定が要るので、**確認用のビルドは圧縮なしにする**。

## 4. 既知のリスク

- **WebGLではリアルタイムの音声合成ができない。** `OnAudioFilterRead`は呼ばれず、AudioMixerは音量以外の効果が使えず、ピッチは正の値のみ(Unity公式マニュアル「Audio in Web」)。JS版の音をC#で再現する方式は大きく制限される(§5-1)。
- **コードで作ったマテリアルのシェーダーが、ビルド時に削除されることがある。** `Shader.Find`で取るシェーダーがWebGLビルドに含まれないと描画されない。P4で必ずWebGLビルドで確認し、必要なら「Always Included Shaders」への追加をBrainが行う。
- **画面の外でマウスボタンを離した場合**、WebGLではその通知が届かないことがある。フォーカス喪失で確定する処理を入れ、実機で確認する。
- `double`で書いても、`Mathf`(float)の関数を混ぜると精度が落ちる。シミュレーションでは`System.Math`だけを使う。

## 5. ユーザーの決定(2026-09-11)

1. **音: `audio.js`をjslibで流用する(方式A)。** 音はJS版と同じになる。WebGLビルドでしか鳴らず、Unityエディタ上では無音。却下した方式B(C#で作り直す)は、WebGLでは事前生成した音声クリップの音量・ピッチしか変えられず、危険度に連動して音色が変わるドローンを再現できないため。
2. **HUDの日本語2文(「射程内 N / 200 (必要 170)」「この位置では到達不能 — 画面の中央へ」)は、日本語フォントを同梱して今のまま表示する。** 英語化はしない。絵文字💥はフォントに無いので、P5で表現を決める。
3. **JS版は移植中は変更しない。**
4. **UIは「少し大きすぎるくらいで見える」ようにする(UI微調整の開始時)。** UIの大きさはJS版に合わせない。`GameHud.HudScale`(まず2)でHUD全体を拡大し、位置は最寄りの画面端か中央からの距離を広げるので、部品同士の並び方はJS版のまま。倍率はこの定数1つで調整する。

