# Bang's-Edge

長押しで粒子を集め、ビッグバンが起きる寸前で離してスコアを競うミニゲーム。PCはマウス、スマホはタッチで遊べる。Unity(WebGL)版をunityroomで公開中(スコアとビッグバン最短時間のランキング付き)。

## このリポジトリについて

ゲームづくりと同時に、**AIにどこまで作業を分散させられるか**を試したプロジェクト。Claude Code(Opus 5)を「Brain」としてタスクの分解・委任先の選択・検証・記録に専念させ、実装の多くはAntigravity CLI(agy)経由のGeminiなどや、discord-ai-hub経由のGPT・Grokなどに委任した。委任の成否や判断の記録も含めて置いている。

## 構成

| 場所 | 中身 |
|---|---|
| `unity/` | Unity 6000.3・URP・C#のWebGL版。unityroomで公開している版 |
| リポジトリ直下(`index.html`・`script.js`・`config.js`・`audio.js`・`style.css`) | 最初に作ったJS版(Canvas、ビルド無し)。物理の基準。Unity版とは設定値が一部違う |
| `tools/` | JS版を固定シードで回してUnity版の基準データを作るハーネス、ヘッドレスChromeで撮影・音・タッチ操作を確かめるスクリプト |
| `docs/` | 仕様・計画・開発の記録 |

## ドキュメント

- `docs/game-concept.md` — ゲーム仕様(数式・パラメータ・音響設計)
- `docs/unity-port-plan.md` — Unity移植の方針と、Unity版で加えた変更(HUD・背景の星・ランキング・スマホ対応)の決定記録
- `docs/workflow.md` — AIへの委任の運用ルール
- `docs/model-experiment-log.md` — 委任したモデルごとの成否と選んだ理由
- `docs/dev-notes.md` — 実装中の気づき・実測・見送った案
- `docs/handoff.md` — 作業セッション間の引き継ぎメモ
- `docs/concept-ai-hub-integration.md` — discord-ai-hub MCP統合の構想メモ
- `docs/cost-log.md` — discord-ai-hub経由のVertex AI利用コストの記録

## 動かし方

- **JS版**: リポジトリ直下を`python -m http.server 8000`などで配信し、`index.html`を開く(`file://`では開かない)。
- **Unity版**: Unity 6000.3.11f1で`unity/`を開く。unityroomのランキングへ送るには`unity/Assets/Resources/Secrets/unityroom-hmac.txt`にHMACキーが必要(gitの対象外)。無ければ送信しないだけで、遊ぶことはできる。

## クレジット

- フォント: BIZ UDゴシック(SIL Open Font License 1.1。ライセンス文は`unity/Assets/Resources/Fonts/`に同梱)
