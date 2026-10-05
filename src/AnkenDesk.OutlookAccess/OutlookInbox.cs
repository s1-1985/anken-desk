using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using AnkenDesk.Core;

namespace AnkenDesk.OutlookAccess
{
    /// <summary>
    /// クラシック版Outlookの受信箱を読む（COM、レイトバインド）。読むだけで、メールを動かしたり消したりしない。
    /// 1つの失敗で全体を止めない（PDFsorterの MailScanner と同じ作法）。
    /// COM参照は必ず解放する。自分で起動したOutlookだけ、終了時にQuitする。1つのインスタンスは、1つのスレッド（STA）の中で使い切る。
    /// </summary>
    public sealed class OutlookInbox : IMailInbox
    {
        private const int OlFolderInbox = 6;
        private const int OlMailClass = 43;
        private const int OlByValue = 1;
        private const int OlMsg = 3;

        // 署名の画像などの、本文に埋め込まれた画像。取り込みの候補から外す。
        private static readonly Regex InlineImageName = new Regex(@"^image\d+\.(png|jpe?g|gif|bmp)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ImageExtension = new Regex(@"\.(png|jpe?g|gif|bmp)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private dynamic? _app;
        private dynamic? _ns;
        private bool _startedByUs;

        public OutlookInbox()
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

        public IReadOnlyList<InboundMail> ListRecent(int days, int maxCount)
        {
            var result = new List<InboundMail>();
            var since = DateTime.Now.AddDays(-days);
            dynamic? folder = null;
            dynamic? items = null;
            try
            {
                folder = _ns!.GetDefaultFolder(OlFolderInbox);
                items = folder.Items;
                items.Sort("[ReceivedTime]", true);
                int n = items.Count;
                for (var i = 1; i <= n && result.Count < maxCount; i++)
                {
                    dynamic? item = null;
                    try
                    {
                        item = items[i];
                        if ((int)item.Class != OlMailClass)
                        {
                            continue;
                        }

                        var received = (DateTime)item.ReceivedTime;
                        if (received < since)
                        {
                            break;
                        }

                        result.Add(ReadMail(item, received));
                    }
                    catch (COMException)
                    {
                        // 読めないメールは飛ばす。
                    }
                    finally
                    {
                        Release(item);
                    }
                }
            }
            finally
            {
                Release(items);
                Release(folder);
            }

            return result;
        }

        public void SaveAttachment(string entryId, int attachmentIndex, string path)
        {
            dynamic? item = null;
            dynamic? atts = null;
            dynamic? att = null;
            try
            {
                item = _ns!.GetItemFromID(entryId);
                atts = item.Attachments;
                att = atts[attachmentIndex];
                att.SaveAsFile(path);
            }
            finally
            {
                Release(att);
                Release(atts);
                Release(item);
            }
        }

        public void SaveAsMsg(string entryId, string path)
        {
            dynamic? item = null;
            try
            {
                item = _ns!.GetItemFromID(entryId);
                item.SaveAs(path, OlMsg);
            }
            finally
            {
                Release(item);
            }
        }

        public void Dispose()
        {
            var app = _app;
            var ns = _ns;
            _app = null;
            _ns = null;

            if (app != null && _startedByUs)
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

        private static InboundMail ReadMail(dynamic item, DateTime received)
        {
            var mail = new InboundMail
            {
                EntryId = (string)item.EntryID,
                Subject = SafeString(() => (string)item.Subject),
                SenderName = SafeString(() => (string)item.SenderName),
                SenderAddress = ReadSenderAddress(item),
                ReceivedAt = received,
            };

            dynamic? atts = null;
            try
            {
                atts = item.Attachments;
                int count = atts.Count;
                for (var i = 1; i <= count; i++)
                {
                    dynamic? att = null;
                    try
                    {
                        att = atts[i];
                        if ((int)att.Type != OlByValue)
                        {
                            continue;
                        }

                        var name = (string)att.FileName;
                        if (IsInlineImage(att, name))
                        {
                            continue;
                        }

                        long size = 0;
                        try
                        {
                            size = (int)att.Size;
                        }
                        catch (COMException)
                        {
                        }

                        mail.Attachments.Add(new InboundAttachment { Index = i, FileName = name, Size = size });
                    }
                    catch (COMException)
                    {
                    }
                    finally
                    {
                        Release(att);
                    }
                }
            }
            catch (COMException)
            {
            }
            finally
            {
                Release(atts);
            }

            return mail;
        }

        // 差出人のSMTPアドレス。Exchangeの内部アドレス形式のときは、プロパティから取り直す。
        private static string ReadSenderAddress(dynamic item)
        {
            try
            {
                var smtp = (string)item.PropertyAccessor.GetProperty("http://schemas.microsoft.com/mapi/proptag/0x5D01001F");
                if (!string.IsNullOrEmpty(smtp))
                {
                    return smtp;
                }
            }
            catch (COMException)
            {
            }

            return SafeString(() => (string)item.SenderEmailAddress);
        }

        private static bool IsInlineImage(dynamic att, string name)
        {
            if (InlineImageName.IsMatch(name))
            {
                return true;
            }

            if (!ImageExtension.IsMatch(name))
            {
                return false;
            }

            try
            {
                // 本文から参照されている画像には、Content-ID が付いている。
                var cid = (string)att.PropertyAccessor.GetProperty("http://schemas.microsoft.com/mapi/proptag/0x3712001F");
                return !string.IsNullOrEmpty(cid);
            }
            catch (COMException)
            {
                return false;
            }
        }

        private static string SafeString(Func<string> get)
        {
            try
            {
                return get() ?? "";
            }
            catch (COMException)
            {
                return "";
            }
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
