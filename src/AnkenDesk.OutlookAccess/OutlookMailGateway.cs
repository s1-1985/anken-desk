using System;
using System.Runtime.InteropServices;
using System.Threading;
using AnkenDesk.Core;

namespace AnkenDesk.OutlookAccess
{
    /// <summary>
    /// クラシック版Outlookを、COM（レイトバインド、PIA参照なし）で操作する。
    /// 動き方は、検証ツール(tools/OutlookCheck)で会社PCで確かめた内容に合わせてある（HANDOFF.md §8.1）:
    /// ・起動中のOutlookがあれば接続し、無ければ自分で起動する（自分で起動したときだけ、終了時にQuitする）
    /// ・送信は、送信トレイに入って、それから送り出される。オフライン作業中だと、送信トレイに残る
    /// ・送信後はEntryIDが変わるので、送信済みで探し直して .msg に保存する
    /// COM参照は必ず解放する。1つのインスタンスは、1つのスレッド（STA）の中で使い切る。
    /// </summary>
    public sealed class OutlookMailGateway : IMailGateway
    {
        private const int OlMailItem = 0;
        private const int OlFolderOutbox = 4;
        private const int OlFolderSentMail = 5;
        private const int OlMsg = 3;

        private dynamic? _app;
        private dynamic? _ns;
        private bool _startedByUs;
        private bool _keepOpen;

        public OutlookMailGateway()
        {
            try
            {
                _app = Marshal.GetActiveObject("Outlook.Application");
            }
            catch (COMException)
            {
                var type = Type.GetTypeFromProgID("Outlook.Application");
                if (type == null)
                {
                    throw new InvalidOperationException("Outlook（デスクトップ版）が見つかりません。");
                }

                _app = Activator.CreateInstance(type);
                _startedByUs = true;
            }

            _ns = _app!.GetNamespace("MAPI");
        }

        public bool IsOffline()
        {
            try
            {
                return (bool)_ns!.Offline;
            }
            catch (COMException)
            {
                return false;
            }
        }

        public MailResult SendAndArchive(MailRequest request, string msgPath)
        {
            var startedAt = DateTime.Now;
            int before;
            dynamic? mail = null;
            try
            {
                mail = CreateMail(request, true);
                if (!(bool)mail.Recipients.ResolveAll())
                {
                    return new MailResult { Outcome = MailOutcome.Failed, Message = "宛先を解決できません: " + request.ToAddress };
                }

                mail.Save();
                before = CountInFolder(OlFolderOutbox, request.Subject);
                mail.Send();
            }
            catch (COMException ex)
            {
                return new MailResult { Outcome = MailOutcome.Failed, Message = "Outlookでメールを送れませんでした: " + ex.Message };
            }
            finally
            {
                Release(mail);
            }

            // Send()は送信トレイに入れるだけ。送り出されるまで待つ（最大60秒）。
            if (!WaitLeftOutbox(request.Subject, before, 60))
            {
                // オフライン作業中などで、送信トレイに残った。Outlookは閉じない（閉じても、残ったメールは次回に送られる）。
                _keepOpen = true;
                return new MailResult
                {
                    Outcome = MailOutcome.Queued,
                    Message = "送信トレイに残っています。Outlookが「オフライン作業中」でないか確認してください。",
                };
            }

            dynamic? sent = FindInSent(request.Subject, startedAt, 30);
            if (sent == null)
            {
                return new MailResult
                {
                    Outcome = MailOutcome.Sent,
                    Message = "送信しましたが、送信済みアイテムに見つかりませんでした（.msgは保存していません）。",
                };
            }

            try
            {
                var result = new MailResult { Outcome = MailOutcome.Sent, Message = "送信しました。" };
                result.EntryId = (string)sent.EntryId;
                result.SentAt = (DateTime)sent.SentOn;
                try
                {
                    sent.SaveAs(msgPath, OlMsg);
                    result.MsgSaved = System.IO.File.Exists(msgPath);
                }
                catch (COMException ex)
                {
                    result.Message = "送信しましたが、.msgを保存できませんでした: " + ex.Message;
                }

                return result;
            }
            finally
            {
                Release(sent);
            }
        }

        public void OpenDraft(MailRequest request)
        {
            dynamic? mail = null;
            try
            {
                // 署名は、Outlookが下書きを開くときに入れる設定ならそれに任せる（二重にしない）。
                mail = CreateMail(request, false);
                mail.Display();
                _keepOpen = true;
            }
            finally
            {
                Release(mail);
            }
        }

        public void Dispose()
        {
            var app = _app;
            var ns = _ns;
            _app = null;
            _ns = null;

            if (app != null && _startedByUs && !_keepOpen)
            {
                try
                {
                    app!.Quit();
                }
                catch (COMException)
                {
                }
            }

            Release(ns);
            Release(app);
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        // ---- 内部 ----

        private dynamic CreateMail(MailRequest request, bool withSignature)
        {
            dynamic mail = _app!.CreateItem(OlMailItem);
            try
            {
                mail.To = request.ToAddress;
                mail.Subject = request.Subject;

                var body = request.Body.Replace("\r\n", "\n").Replace("\n", "\r\n");
                if (withSignature && request.IncludeSignature)
                {
                    var signature = OutlookSignature.TryReadText();
                    if (!string.IsNullOrEmpty(signature))
                    {
                        body += "\r\n\r\n" + signature;
                    }
                }

                mail.Body = body;
                foreach (var path in request.Attachments)
                {
                    mail.Attachments.Add(path);
                }

                return mail;
            }
            catch
            {
                Release(mail);
                throw;
            }
        }

        // フォルダの中で、件名が同じメールの数（送信トレイの確認に使う）。1通の失敗で全体を止めない。
        private int CountInFolder(int folderId, string subject)
        {
            var count = 0;
            dynamic? folder = null;
            dynamic? items = null;
            try
            {
                folder = _ns!.GetDefaultFolder(folderId);
                items = folder.Items;
                int n = items.Count;
                for (var i = 1; i <= n; i++)
                {
                    dynamic? item = null;
                    try
                    {
                        item = items[i];
                        if ((string)item.Subject == subject)
                        {
                            count++;
                        }
                    }
                    catch (COMException)
                    {
                    }
                    finally
                    {
                        Release(item);
                    }
                }
            }
            catch (COMException)
            {
            }
            finally
            {
                Release(items);
                Release(folder);
            }

            return count;
        }

        private bool WaitLeftOutbox(string subject, int before, int timeoutSec)
        {
            var limit = DateTime.Now.AddSeconds(timeoutSec);
            while (DateTime.Now < limit)
            {
                if (CountInFolder(OlFolderOutbox, subject) <= before)
                {
                    return true;
                }

                Thread.Sleep(1500);
            }

            return false;
        }

        // 送信済みの新しい方から数十通を見て、件名が同じで、送信日時が今回の送信以降のものを探す。
        // 1通ずつ順に送るので、これで今送ったメールが特定できる。
        private dynamic? FindInSent(string subject, DateTime startedAt, int timeoutSec)
        {
            var limit = DateTime.Now.AddSeconds(timeoutSec);
            while (DateTime.Now < limit)
            {
                dynamic? folder = null;
                dynamic? items = null;
                try
                {
                    folder = _ns!.GetDefaultFolder(OlFolderSentMail);
                    items = folder.Items;
                    items.Sort("[SentOn]", true);
                    int n = Math.Min((int)items.Count, 40);
                    for (var i = 1; i <= n; i++)
                    {
                        dynamic? item = null;
                        try
                        {
                            item = items[i];
                            if ((string)item.Subject == subject && (DateTime)item.SentOn >= startedAt.AddSeconds(-5))
                            {
                                var found = item;
                                item = null;
                                return found;
                            }
                        }
                        catch (COMException)
                        {
                        }
                        finally
                        {
                            Release(item);
                        }
                    }
                }
                catch (COMException)
                {
                }
                finally
                {
                    Release(items);
                    Release(folder);
                }

                Thread.Sleep(1500);
            }

            return null;
        }

        private static void Release(object? com)
        {
            if (com != null && Marshal.IsComObject(com))
            {
                try
                {
                    Marshal.ReleaseComObject(com);
                }
                catch (ArgumentException)
                {
                }
            }
        }
    }
}
