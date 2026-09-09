# AGENTS.md — Bang's-Edge

このファイルはAntigravity CLI(`agy`)がセッション開始時に自動的に読む共通コンテキスト。実装そのものはClaude Codeが担当し、**Antigravityはこのプロジェクトではレビュー・監査役として使う**。常設のカスタムエージェント(`.agents/agents/...`)は使わない方針(`--new-project`によるプロジェクト一覧肥大化を避けるため)。監査を依頼する際は、都度のプロンプト(`agy --add-dir "<絶対パス>" -p "..."`)で観点を指定する。詳細は`docs/workflow.md` §3。

## Project shape

- ジャンル: マウスのみで遊ぶブラウザゲーム。最終的にGitHub Pagesで静的サイトとして公開。
- スタック: 素のHTML/CSS/JS(Canvas API)を想定。ビルドステップ・外部フレームワーク依存なし(要確認・未確定なら変更可)。
- 仕様の正本: `docs/game-concept.md`

## 文書体系

- `docs/game-concept.md` — ゲームコンセプト(仕様の正本)。**⚠️この内容はOpenAIデータ共有モデル(discord-ai-hub経由の`gpt-5.6-*`)へ送らないこと**(`ask`/`compare`いずれも)。
- `docs/workflow.md` — 開発・監査フロー、モデル別役割分担、データ共有方針
- `docs/cost-log.md` — discord-ai-hub経由Vertex AI(月$10共有枠)の利用記録
- `docs/concept-ai-hub-integration.md` — 本プロジェクトの位置づけ(discord-ai-hub MCP統合構想の実地検証)

## レビュー観点(このプロジェクトにおける規範。N1〜N40のような大規模体系はないため、以下がそれに代わる基準)

1. `docs/game-concept.md`との整合性(操作方法・スコアリング・ラウンド構造)
2. コード品質(不要な複雑化・デッドコード・命名の明確さ)
3. パフォーマンス(Canvas描画、パーティクル数増加時のフレームレート低下の可能性)
4. GitHub Pages上の静的サイトとして動作する上での依存関係の問題

## 基本方針

- コードを読んで上記レビュー観点に照らして評価するのが役割。指摘は具体的なファイル名・行番号を挙げ、重大度(必須修正/推奨/任意)を付けて列挙する。
- 本プロジェクトはdiscord-ai-hub MCP統合構想の実地検証でもある(`docs/concept-ai-hub-integration.md`参照)。監査フローの往復回数や気づいた点があれば、`docs/concept-ai-hub-integration.md` §1.2/§4への追記候補として報告する。
