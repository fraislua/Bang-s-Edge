# Bang's-Edge

長押しで粒子を集め、ビッグバンが起きる寸前で離してスコアを競うミニゲーム。PCはマウス、スマホはタッチで遊べる。

## 遊ぶ

- **最新版(Unity版)**: https://unityroom.com/games/bang-s-edge — スコアとビッグバン最短時間のランキング付き、スマホ対応
- **旧バージョン(JS版)**: https://fraislua.github.io/Bang-s-Edge/ — 開発途中の調整段階のもの。PCのマウスのみで、ランキングは無く、遊びの調整も今の版とは違う

## このリポジトリについて

ゲームづくりと同時に、**AIにどこまで作業を分散させられるか**を試したプロジェクト。Claude Code(Opus 5)を「Brain」としてタスクの分解・委任先の選択・検証・記録に専念させ、実装の多くはAntigravity CLI(agy)経由のGeminiなどや、discord-ai-hub経由のGPT・Grokなどに委任した。委任の成否や判断の記録も含めて置いている。

## 構成

| 場所 | 中身 |
|---|---|
| `unity/` | Unity 6000.3・URP・C#のWebGL版。unityroomで公開している最新版 |
| リポジトリ直下(`index.html`・`script.js`・`config.js`・`audio.js`・`style.css`) | 最初に作ったJS版(Canvas、ビルド無し)。**調整段階の旧バージョン**としてGitHub Pagesに置いている。物理のロジックはUnity版と同じで、Unity版の基準にも使っているが、設定値が一部違う(引き寄せる点が揺れる、など) |
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

## ライセンス

[MIT License](LICENSE)。ただし同梱のフォント(BIZ UDゴシック、`unity/Assets/Resources/Fonts/`)は、同じフォルダにあるSIL Open Font License 1.1に従う。
