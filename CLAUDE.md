# CLAUDE.md — Bang's-Edge

マウスのみで遊ぶブラウザゲーム(GitHub Pages公開予定)。本プロジェクトは同時に2つの実験を兼ねる: (1) discord-ai-hub MCP統合構想(`docs/concept-ai-hub-integration.md`)の実地検証、(2) **Claude Codeからどれだけタスクを分散させられるかの実験**。

## Brainとしての役割(2026-09-10方針)

実装フェーズでは、Claude Code自身が**Opus5**モデルで動作し、「Brain」としてタスクの分解・委任先決定・結果検証・記録を担う。実装そのものは極力自分で書かず、agy(実装も監査も両方依頼可能)やdiscord-ai-hub経由のモデルに委任する。

- **モデル切り替えについて**: Sonnet5→Opus5への切り替えはユーザー側の操作(`/model`等)が必要。この文書を読むだけでは切り替わらないので、実装フェーズに入る前にユーザーに確認する。
- 委任前に`docs/model-experiment-log.md`を確認し、類似タスクの実績(モデル・effort別の成否)を参考にモデルを選ぶ。
- 委任後は結果と選択理由を`docs/model-experiment-log.md`に追記する(後回しにしない)。自動集計の仕組みは今回は導入しない——Brain(自分)が毎回ログを読んで判断する運用でよい。
- agyには実装・監査どちらも依頼できる(同一系統への委任が自己監査になる場面は今回意図的に許容する実験設定)。

## 最優先で守る制約

**`docs/game-concept.md`の内容(ゲームのアイデアの種)を、discord-ai-hub経由のOpenAIデータ共有モデル(`gpt-5.6-sol/terra/luna`)に送らない。** `ask`・`compare`いずれも対象。引用・要約・言い換えも含む。`compare`の`models`配列に`gpt-5.6-*`を入れる場合は、送信するプロンプトが該当しないか必ず確認する。それ以外(実装コード・定型作業)は共有可。詳細: `docs/workflow.md` §1。

## 読むべきドキュメント

- `docs/game-concept.md` — ゲーム仕様の正本(非公開情報を含む)
- `docs/workflow.md` — データ共有方針・モデル別役割分担・委任呼び出し方法・モデル選択ログ運用
- `docs/model-experiment-log.md` — モデル・effort別の成否と選択理由の記録(委任前に必ず確認)
- `docs/cost-log.md` — discord-ai-hub経由Vertex AI(`xai/grok-4.6`等、月$10共有枠)を呼ぶたびに追記する
- `AGENTS.md` — agy(Antigravity)がセッション開始時に自動で読む共通コンテキスト。実装・監査両方の基本方針・品質基準はここに定義してあり、`docs/workflow.md`と役割分担している

## agyへの依頼方法(実装・監査共通)

常設のカスタムエージェント(`.agents/agents/...`)は使わない。以下の形で都度呼び出す(`--add-dir`を付けるとそのディレクトリの`AGENTS.md`が自動で読み込まれ、基本方針・品質基準が反映される)。プロンプト冒頭に`[実装]`/`[監査]`で役割を明示する:

```
agy --model <model> --add-dir "C:\Users\ryoga\Documents\Bang's-Edge" -p "[実装|監査] <対象の指定>"
```

モデル・effortは`docs/model-experiment-log.md`の実績を見て都度選ぶ。Vertex AI経由のモデルを使った場合は`docs/cost-log.md`にも記録する。

## 技術スタック(前提・未確定なら変更可)

素のHTML/CSS/JS(Canvas API)。ビルドステップ・外部フレームワーク依存なしを想定(GitHub Pagesにそのまま置ける形)。
