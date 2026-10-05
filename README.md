# anken-desk

見積・案件管理のWindowsデスクトップアプリ（個人用）。仕様と決定事項は `HANDOFF.md`、作業のルールは `CLAUDE.md`。

## 構成
- `src/AnkenDesk.Core` — UI・COMに依存しない業務ロジック（netstandard2.0）
- `src/AnkenDesk.OutlookAccess` — Outlook COM（net48、レイトバインド）
- `src/AnkenDesk.App` — 画面（WinForms、net48）
- `tests/AnkenDesk.Core.Tests` — 単体テスト（xUnit、net8.0）
- `tools/OutlookCheck` — Outlook COM の検証ツール（使い捨て）

## ビルド
.NET 8 SDK が要る（クラウド環境では `.claude/hooks/session-start.sh` が入れる）。

```
dotnet build AnkenDesk.sln
dotnet test AnkenDesk.sln
dotnet publish src/AnkenDesk.App -c Release -r win-x64 --self-contained false
```

配布物は `src/AnkenDesk.App/bin/Release/net48/win-x64/publish/` の中身一式。zip にして会社PCで解凍し、`AnkenDesk.exe` を実行する。
