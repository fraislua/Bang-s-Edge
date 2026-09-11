# CLAUDE.md — Bang's-Edge

長押しで遊ぶゲーム(PCはマウス、スマホはタッチ)。JS版をGitHub Pagesで公開していたが、2026-09-11にUnity(WebGL)へ移植し(`unity/`)、**unityroomで公開済み(スコアとビッグバン最短時間のランキング付き、スマホ対応済み)**。**リポジトリは公開**(2026-09-11にいったん非公開にし、同日に公開へ戻した。ライセンスはMIT)。JS版はGitHub Pagesで「調整段階の旧バージョン」として公開中。本プロジェクトは同時に2つの実験を兼ねる: (1) discord-ai-hub MCP統合構想(`docs/concept-ai-hub-integration.md`)の実地検証、(2) **Claude Codeからどれだけタスクを分散させられるかの実験**。

## Brainとしての役割(2026-09-10方針)

実装フェーズでは、Claude Code自身が**Opus5**モデルで動作し、「Brain」としてタスクの分解・委任先決定・結果検証・記録を担う。実装そのものは極力自分で書かず、agy(実装も監査も両方依頼可能)やdiscord-ai-hub経由のモデルに委任する。

- **モデル切り替えについて**: Sonnet5→Opus5への切り替えはユーザー側の操作(`/model`等)が必要。この文書を読むだけでは切り替わらないので、実装フェーズに入る前にユーザーに確認する。
- 委任前に`docs/model-experiment-log.md`を確認し、類似タスクの実績(モデル・effort別の成否)を参考にモデルを選ぶ。
- 委任後は結果と選択理由を`docs/model-experiment-log.md`に追記する(後回しにしない)。自動集計の仕組みは今回は導入しない——Brain(自分)が毎回ログを読んで判断する運用でよい。
- agyには実装・監査どちらも依頼できる(同一系統への委任が自己監査になる場面は今回意図的に許容する実験設定)。

## 最優先で守る制約(2026-09-10に範囲を緩和)

本プロジェクトは実験目的のため、**「明らかにユーザーのアイデアと分かるもの」以外は`gpt-5.6-*`に共有してよい**(ユーザー判断)。

- **送らない**: *赤い粒子をマウスの長押しで集め、ビッグバン直前に離して密度を競う*という発想の組み合わせと、タイトル`Bang's-Edge`。`docs/game-concept.md`の「概要」「ゲームメカニクス」「スコアリング」「ラウンド構造」がこれにあたる。
- **送ってよい**: 同ファイルの「実装仕様」(数式・パラメータ・音響設計)、実装コード、一般的な技術相談、定型作業。

**判断基準**: そのプロンプトだけを読んで「どんなゲームか」が復元できてしまうなら、`models`配列から`gpt-5.6-*`を外す。復元できないよう抽象化できるなら送ってよい。ローカルモデル・Vertex AI・agy経由には全て渡してよい。詳細: `docs/workflow.md` §1。

## 読むべきドキュメント

- **`docs/handoff.md` — セッション開始時にまず読む。** 前回終了時点の状態・未解決事項・実測済みのハマりどころがまとまっている
- `docs/game-concept.md` — ゲーム仕様の正本(`gpt-5.6-*`に送らない部分を含む。上の制約を参照)
- `docs/workflow.md` — データ共有方針・モデル別役割分担・委任呼び出し方法・モデル選択ログ運用
- `docs/model-experiment-log.md` — モデル・effort別の成否と選択理由の記録(委任前に必ず確認)
- `docs/dev-notes.md` — 他の文書に属さない実装中の気づき・保留事項・却下したアプローチの記録。「書くべきかどうか迷ったらここに書く」の位置づけ
- **`.claude/skills/discord-ai-hub/SKILL.md` — discord-ai-hubの`ask`/`compare`を呼ぶ前に必ず読む。** 呼び出し前後の確認項目(送信可否・モデル選択・`null`の渡し方・途中切れ・記録)をここに一本化してある。スキルとして自動で読み込まれなかった場合も、呼ぶ前に直接読む
- `docs/MCP_AGENT_GUIDE.md` — discord-ai-hub MCPサーバーの利用ガイド(**サーバー側が配布する正本**)。モデル一覧・引数・無料枠・応答が不完全な場合の挙動
- `docs/mcp-feedback.md` / `docs/mcp-feedback-response.md` — MCPサーバーへの不具合報告と、それへの回答(決着済み)。**こちらの原因推定が誤っていた点の訂正表を含む**
- `docs/cost-log.md` — discord-ai-hub経由Vertex AI(`xai/grok-4.6`等、月$10共有枠)を呼ぶたびに追記する
- `AGENTS.md` — agy(Antigravity)がセッション開始時に自動で読む共通コンテキスト。実装・監査両方の基本方針・品質基準はここに定義してあり、`docs/workflow.md`と役割分担している

## agyへの依頼方法(実装・監査共通)

**委任するときは`.claude/skills/agy-delegation/SKILL.md`を読む。** 委任するかの判断・プロンプトの書き方・呼び出し・検証・記録の手順をまとめてある。スキルとして自動で読み込まれなかった場合も直接読む。

常設のカスタムエージェント(`.agents/agents/...`)は使わない。以下の形で都度呼び出す(`--add-dir`を付けるとそのディレクトリの`AGENTS.md`が自動で読み込まれ、基本方針・品質基準が反映される)。プロンプト冒頭に`[実装]`/`[監査]`で役割を明示する:

```
agy --model <model> --mode accept-edits --add-dir "<このリポジトリの絶対パス>" -p "[実装|監査] <対象の指定>"
```

ファイルを書かせない呼び出し(監査・提案のみ)では`--mode accept-edits`を付けない。

モデル・effortは`docs/model-experiment-log.md`の実績を見て都度選ぶ。Vertex AI経由のモデルを使った場合は`docs/cost-log.md`にも記録する。

## 技術スタック(2026-09-11にUnityへの移植を開始)

- **移植先(`unity/`)**: Unity 6000.3.11f1・URP(Universal 2D)・C#。ターゲットはWebGL。移植方針・作業分割・検証方法は`docs/unity-port-plan.md`。ビルド手順は`docs/dev-notes.md`。**unityroomへの公開ビルドは、ユーザーがエディターからビルドプロファイル`Web - Mobile - Release`で手動ビルドする**(Brainのバッチビルドは投稿で弾かれたことがある)。ランキングのHMACキーは`unity/Assets/Resources/Secrets/unityroom-hmac.txt`(gitignore済み、コミットしない)。手順は`docs/handoff.md` §1。
- **JS版(リポジトリ直下)**: 素のHTML/CSS/JS(Canvas API)、ビルド無し。移植前の版で、GitHub Pagesで「調整段階の旧バージョン」として公開中(変更を確かめるときはローカルで配信する)で、**物理のロジックの基準**。Unity版は設定値3つ(揺動を止めて`TAU_R`を8に)を意図的に変えてあり、JS版は以前の遊びのまま残す(ユーザー判断、`docs/unity-port-plan.md` §5-6)。
- 仕様の正本は`docs/game-concept.md`の「実装仕様」(JS版・Unity版共通)。
- **agyはターミナルを使えないのでUnityのコンパイル・テスト・ビルドはできない。** 委任したC#は、Brainが`unity build`/`unity test`で確認する。
