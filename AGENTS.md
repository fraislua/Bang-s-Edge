# AGENTS.md — Bang's-Edge

このファイルはAntigravity CLI(`agy`)がセッション開始時に自動的に読む共通コンテキスト。

**役割(2026-09-10更新)**: このプロジェクトはClaude Code(Brain)がタスクを分解し、実装をできるだけ他のモデルに委任する実験を兼ねている。Antigravityには**実装・監査のどちらも依頼される**。どちらの役割かは呼び出し時のプロンプトで明示されるので、それに従う。同じ呼び出し系統で実装も監査も行うため「実装者が自分の監査をする」形になる場面があるが、これは今回意図的に許容している実験設定(結果は`docs/model-experiment-log.md`に記録される)。

常設のカスタムエージェント(`.agents/agents/...`)は使わない方針(`--new-project`によるプロジェクト一覧肥大化を避けるため)。依頼する際は、都度のプロンプト(`agy --add-dir "<絶対パス>" -p "..."`)で役割・観点・対象を指定する。詳細は`docs/workflow.md` §2-3。

## Project shape

- ジャンル: マウスのみで遊ぶブラウザゲーム。最終的にGitHub Pagesで静的サイトとして公開。
- スタック: 素のHTML/CSS/JS(Canvas API)を想定。ビルドステップ・外部フレームワーク依存なし(要確認・未確定なら変更可)。
- 仕様の正本: `docs/game-concept.md`

## 文書体系

- `docs/game-concept.md` — ゲームコンセプト(仕様の正本)。**⚠️この内容はOpenAIデータ共有モデル(discord-ai-hub経由の`gpt-5.6-*`)へ送らないこと**(`ask`/`compare`いずれも)。
- `docs/workflow.md` — 開発・監査フロー、モデル別役割分担、データ共有方針
- `docs/cost-log.md` — discord-ai-hub経由Vertex AI(月$10共有枠)の利用記録
- `docs/concept-ai-hub-integration.md` — 本プロジェクトの位置づけ(discord-ai-hub MCP統合構想の実地検証)

## 品質基準(このプロジェクトにおける規範。N1〜N40のような大規模体系はないため、以下がそれに代わる基準——実装時の自己チェック・監査時のレビューの両方に使う)

1. `docs/game-concept.md`との整合性(操作方法・スコアリング・ラウンド構造)
2. コード品質(不要な複雑化・デッドコード・命名の明確さ)
3. パフォーマンス(Canvas描画、パーティクル数増加時のフレームレート低下の可能性)
4. GitHub Pages上の静的サイトとして動作する上での依存関係の問題

## 基本方針

- **実装を依頼された場合**: `docs/game-concept.md`の仕様と素のHTML/CSS/JS(Canvas API、ビルド無し)という技術スタック前提に従い実装する。返答時に上記品質基準に照らした自己チェック結果を一言添える。
- **監査を依頼された場合**: コードを読んで上記品質基準に照らして評価する。指摘は具体的なファイル名・行番号を挙げ、重大度(必須修正/推奨/任意)を付けて列挙する。
- 本プロジェクトはdiscord-ai-hub MCP統合構想の実地検証でもある(`docs/concept-ai-hub-integration.md`参照)。作業の往復回数・モデルやeffortによる出来の差など気づいた点があれば、依頼元(Claude Code / Brain)に報告する——それが`docs/model-experiment-log.md`への記録に使われる。
