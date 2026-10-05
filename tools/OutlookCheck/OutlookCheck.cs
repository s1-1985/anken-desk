// Outlook COM 検証ツール（HANDOFF.md §6.2 の5点を会社PCで確認する）
// C# 5 互換で書いている（Windows標準の csc.exe でそのままビルドできるように）。
// PIA参照なし。レイトバインド(dynamic)でOutlookを操作する。
// 取引先のデータは使わない。送信先は引数で指定したテスト用アドレスだけ。
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

internal static class Program
{
    private const int OlMailItem = 0;
    private const int OlFolderOutbox = 4;
    private const int OlFolderSentMail = 5;
    private const int OlMsg = 3;

    private static readonly StringBuilder Report = new StringBuilder();

    private static void Log(string line)
    {
        Console.WriteLine(line);
        Report.AppendLine(line);
    }

    private static int Main(string[] args)
    {
        if (args.Length != 1 || args[0].IndexOf('@') < 0)
        {
            Console.WriteLine("使い方: OutlookCheck.exe <テスト送信先メールアドレス>");
            Console.WriteLine("  送信先は自分のアドレスなど、無関係な第三者に届かないものを指定すること。");
            return 2;
        }
        string to = args[0];
        string outDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "out");
        Directory.CreateDirectory(outDir);

        Log("=== Outlook COM 検証 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===");
        Log("OS: " + Environment.OSVersion + " / 64bitプロセス: " + Environment.Is64BitProcess);

        Console.WriteLine();
        Console.WriteLine("テストメールを " + to + " へ【実際に送信】します。続けますか? (y/N)");
        string answer = Console.ReadLine();
        if (answer == null || answer.Trim().ToLowerInvariant() != "y")
        {
            Console.WriteLine("中止しました。");
            return 1;
        }

        dynamic app = null;
        bool startedByUs = false;
        try
        {
            // [検証1] 画面を出さずに作成・送信できるか。プロフィール選択ダイアログが出るかは目視で確認する。
            Log("");
            Log("[検証1] Outlookへの接続");
            Console.WriteLine("  ※ Outlookが未起動の状態で実行すると、プロフィール選択ダイアログの有無を確認できる。");
            try
            {
                app = Marshal.GetActiveObject("Outlook.Application");
                Log("  既に起動中のOutlookに接続した（自分では起動していない）");
            }
            catch (COMException)
            {
                Type t = Type.GetTypeFromProgID("Outlook.Application");
                if (t == null) { Log("  NG: Outlook.Application が見つからない（Outlook未インストール?）"); return 1; }
                app = Activator.CreateInstance(t);
                startedByUs = true;
                Log("  Outlookを自分で起動した（終了時に自分でQuitする）");
            }
            Log("  Outlookのバージョン: " + (string)app.Version);

            string marker = "OutlookCheck-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string subject = "[検証] " + marker;

            dynamic mail = app.CreateItem(OlMailItem);
            mail.To = to;
            mail.Subject = subject;

            // [検証5] 署名の扱い。本文を設定する前の HTMLBody に署名が入っているか。
            string initialHtml = (string)mail.HTMLBody ?? "";
            Log("");
            Log("[検証5] 署名");
            Log("  新規メール作成直後のHTMLBodyの長さ: " + initialHtml.Length
                + (initialHtml.IndexOf("<body", StringComparison.OrdinalIgnoreCase) >= 0 ? "（<body>あり）" : "（<body>なし）"));
            Log("  → 署名が自動で入っているかは、受信側で届いたメール末尾を見て判断する。");

            mail.Body = "これはOutlook COM検証ツールの自動テストメールです。\r\n" + marker;

            dynamic ns = app.GetNamespace("MAPI");
            Log("");
            Log("[状態] Outlookのオフライン作業: " + DescribeOffline(ns)
                + "（オフラインだと、送信トレイに入ったまま外へ出ない）");

            // [検証4-a] 送信前のEntryID（下書き保存してから取る）
            mail.Save();
            string entryIdBefore = (string)mail.EntryID;
            Log("");
            Log("[検証4] .msg保存とEntryID");
            Log("  送信前(下書き保存後)のEntryID: " + Shorten(entryIdBefore));

            // [検証2] 自動送信時のセキュリティ確認ダイアログは目視で確認する。
            Console.WriteLine();
            Console.WriteLine("これから mail.Send() を呼びます。セキュリティ確認ダイアログが出たらメモすること。");
            mail.Send();
            Log("");
            Log("[検証2] mail.Send() は例外なく戻った（ダイアログが出たかは実機で目視。出なかった/出た を記録）");

            // [検証3] 送信済みアイテムに入るか
            Log("");
            Log("[検証3] 送信済みアイテム");
            // 送信トレイから出るまで待つ（Send()は送信トレイに入れるだけで、送り出しは非同期）
            bool leftOutbox = WaitUntilLeftOutbox(ns, subject, 60);
            Log("  送信トレイから出るまで待った結果: " + (leftOutbox ? "出た" : "60秒たっても残っている"));
            dynamic sent = FindInSent(ns, subject, 30);
            if (sent == null)
            {
                Log("  NG: 送信済みアイテムに見つからなかった（件名: " + subject + "）");
                Log("  送信トレイに残っているか: " + (OutboxContains(ns, subject) ? "残っている（未送信）" : "残っていない")
                    + " / オフライン作業: " + DescribeOffline(ns));
            }
            else
            {
                Log("  OK: 送信済みアイテムに見つかった。SentOn=" + ((DateTime)sent.SentOn).ToString("yyyy-MM-dd HH:mm:ss"));
                string entryIdAfter = (string)sent.EntryID;
                Log("  送信後のEntryID: " + Shorten(entryIdAfter));
                Log("  送信前後でEntryIDは " + (entryIdBefore == entryIdAfter ? "同じ" : "変わった"));

                string msgPath = Path.Combine(outDir, marker + ".msg");
                sent.SaveAs(msgPath, OlMsg);
                Log("  .msg保存: " + (File.Exists(msgPath) ? "OK " + new FileInfo(msgPath).Length + " bytes (" + msgPath + ")" : "NG ファイルができていない"));
                Marshal.ReleaseComObject(sent);
            }
            Log("  他端末のOutlook(送信済み)に同期されるかは、別の端末で件名「" + subject + "」を探して確認する。");
            Marshal.ReleaseComObject(ns);
            Marshal.ReleaseComObject(mail);
        }
        catch (Exception ex)
        {
            Log("");
            Log("例外: " + ex.GetType().Name + ": " + ex.Message);
            return 1;
        }
        finally
        {
            if (app != null)
            {
                try { if (startedByUs) app.Quit(); }
                catch (Exception ex) { Log("Quit失敗: " + ex.Message); }
                try { Marshal.ReleaseComObject(app); } catch { }
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            string reportPath = Path.Combine(outDir, "report-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
            File.WriteAllText(reportPath, Report.ToString(), new UTF8Encoding(true));
            Console.WriteLine();
            Console.WriteLine("結果を保存した: " + reportPath);
        }
        return 0;
    }

    // 既定ストアの送信済みフォルダから件名で探す（PDFsorterの知見: folder.Store.GetDefaultFolder 系を使う）
    private static dynamic FindInSent(dynamic ns, string subject, int timeoutSec)
    {
        DateTime limit = DateTime.Now.AddSeconds(timeoutSec);
        while (DateTime.Now < limit)
        {
            dynamic folder = ns.GetDefaultFolder(OlFolderSentMail);
            dynamic items = folder.Items;
            // 件名はクォートし、シングルクォートはエスケープする
            string filter = "[Subject] = '" + subject.Replace("'", "''") + "'";
            dynamic found = null;
            try { found = items.Find(filter); }
            catch (Exception ex) { Log("  Find失敗: " + ex.Message); }
            if (found != null) return found;
            Thread.Sleep(2000);
        }
        return null;
    }

    private static string DescribeOffline(dynamic ns)
    {
        try { return (bool)ns.Offline ? "オフライン" : "オンライン"; }
        catch (Exception ex) { return "取得失敗(" + ex.Message + ")"; }
    }

    // 送信トレイに件名のメールがあるか（件名は自前で照合する）
    private static bool OutboxContains(dynamic ns, string subject)
    {
        dynamic folder = ns.GetDefaultFolder(OlFolderOutbox);
        dynamic items = folder.Items;
        int n = (int)items.Count;
        for (int i = 1; i <= n; i++)
        {
            dynamic item = null;
            try
            {
                item = items[i];
                if ((string)item.Subject == subject) return true;
            }
            catch (Exception) { }
            finally { if (item != null) { try { Marshal.ReleaseComObject(item); } catch { } } }
        }
        return false;
    }

    private static bool WaitUntilLeftOutbox(dynamic ns, string subject, int timeoutSec)
    {
        DateTime limit = DateTime.Now.AddSeconds(timeoutSec);
        while (DateTime.Now < limit)
        {
            if (!OutboxContains(ns, subject)) return true;
            Thread.Sleep(2000);
        }
        return false;
    }

    private static string Shorten(string id)
    {
        if (string.IsNullOrEmpty(id)) return "(空)";
        return id.Length <= 16 ? id : id.Substring(0, 8) + "..." + id.Substring(id.Length - 8) + " (長さ" + id.Length + ")";
    }
}
