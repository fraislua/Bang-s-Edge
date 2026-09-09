# CLAUDE.md — Bang's-Edge

マウスのみで遊ぶブラウザゲーム(GitHub Pages公開予定)。Claude Codeが実装を担当し、Antigravity CLI(`agy`)がレビュー・監査役、`discord-ai-hub` MCPは狭い用途の補助として使う。本プロジェクトは同時に、discord-ai-hub MCP統合構想(`docs/concept-ai-hub-integration.md`)の実地検証でもある。

## 最優先で守る制約

**`docs/game-concept.md`の内容(ゲームのアイデアの種)を、discord-ai-hub経由のOpenAIデータ共有モデル(`gpt-5.6-sol/terra/luna`)に送らない。** `ask`・`compare`いずれも対象。引用・要約・言い換えも含む。`compare`の`models`配列に`gpt-5.6-*`を入れる場合は、送信するプロンプトが該当しないか必ず確認する。それ以外(実装コード・定型作業)は共有可。詳細: `docs/workflow.md` §1。

## 読むべきドキュメント

- `docs/game-concept.md` — ゲーム仕様の正本(非公開情報を含む)
- `docs/workflow.md` — データ共有方針・モデル別役割分担・監査呼び出し方法
- `docs/cost-log.md` — discord-ai-hub経由Vertex AI(`xai/grok-4.6`等、月$10共有枠)を呼ぶたびに追記する
- `AGENTS.md` — agy(Antigravity)がセッション開始時に自動で読む共通コンテキスト。監査ペルソナ・レビュー観点はここに定義してあり、`docs/workflow.md`と役割分担している

## 監査の依頼方法

常設のカスタムエージェント(`.agents/agents/...`)は使わない。以下の形で都度呼び出す(`--add-dir`を付けるとそのディレクトリの`AGENTS.md`が自動で読み込まれ、監査ペルソナ・観点が反映される):

```
agy --model <model> --add-dir "C:\Users\ryoga\Documents\Bang's-Edge" -p "<監査対象の指定>"
```

モデルは都度選ぶ(固定しない)。Vertex AI経由のモデルを使った場合は`docs/cost-log.md`に記録する。

## 技術スタック(前提・未確定なら変更可)

素のHTML/CSS/JS(Canvas API)。ビルドステップ・外部フレームワーク依存なしを想定(GitHub Pagesにそのまま置ける形)。
