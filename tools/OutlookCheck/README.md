# OutlookCheck — Outlook COM 検証ツール

HANDOFF.md §6.2 の検証を会社PCで行う使い捨てのツール。**ビルドも実行も会社PCで行う**（Linuxの開発環境にはコンパイラがなく、未ビルド・未実行）。

## 手順
1. `build.bat` をダブルクリック（.NET SDK / Visual Studio は不要。Windows標準の csc.exe を使う）
2. 検証1のため、可能ならOutlookを**終了した状態**にしておく
3. コマンドプロンプトで `OutlookCheck.exe` に続けて自分のメールアドレスを入力する。例: `OutlookCheck.exe taro@example.com`（`< >` は打たない。cmd.exeがリダイレクト記号として解釈し「コマンドの構文が誤っています」になる）
4. 確認画面で `y` を入力すると、テストメールを**実際に送信**する

## 目視で確認すること（ツールでは判定できない）
| 検証 | 見ること |
|---|---|
| 1 | Outlook未起動時に、プロフィール選択ダイアログが出たか |
| 2 | `Send()` 時にセキュリティ確認ダイアログが出たか（Fortinet環境） |
| 3 | 他端末のOutlookの送信済みに、件名 `[検証] OutlookCheck-xxxxxxxx` が届いたか |
| 5 | 届いたメールの末尾に署名が入っているか |

ツールが自動で記録すること: Outlookへの接続方法、送信済みアイテムへの記録、送信前後のEntryID、`.msg`保存の成否。結果は `out/report-*.txt` に残る。
結果は HANDOFF.md §8 の #14 に追記する（`out/` は git に入れない）。
