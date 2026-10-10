using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using AnkenDesk.Core;
using AnkenDesk.OutlookAccess;

namespace AnkenDesk.App
{
    /// <summary>
    /// 客先の依頼メールを、案件の登録の前に、一時フォルダへ取り出したもの（添付・.msg・本文）。
    /// 登録して案件フォルダができたら、そこへコピーする。画面を閉じたら、一時フォルダは消す。
    /// </summary>
    internal sealed class StagedClientMail : IDisposable
    {
        public InboundMail Mail { get; private set; } = new InboundMail();
        public string TempDir { get; private set; } = "";
        public List<string> AttachmentPaths { get; } = new List<string>();
        public string? MsgPath { get; private set; }
        public string? Body { get; private set; }
        public List<string> Errors { get; } = new List<string>();

        /// <summary>Outlookからメールを取り出す（別スレッド・待ち画面つき）。失敗した項目は Errors に入れて続ける。</summary>
        public static StagedClientMail Stage(IWin32Window owner, InboundMail mail)
        {
            var staged = new StagedClientMail
            {
                Mail = mail,
                TempDir = Path.Combine(Path.GetTempPath(), "AnkenDesk-" + Guid.NewGuid().ToString("N")),
            };
            Directory.CreateDirectory(staged.TempDir);
            var dir = staged.TempDir;
            var result = Background.Run<StagedClientMail>(owner, "Outlookからメールを取り出しています。\r\nしばらくお待ちください。", () =>
            {
                using (var inbox = new OutlookInbox())
                {
                    foreach (var a in mail.Attachments)
                    {
                        try
                        {
                            var path = QuoteFiles.UniquePath(dir, Sanitizer.FileName(a.FileName));
                            inbox.SaveAttachment(mail.EntryId, a.Index, path);
                            if (File.Exists(path))
                            {
                                staged.AttachmentPaths.Add(path);
                            }
                        }
                        catch (Exception ex) when (!(ex is OutOfMemoryException))
                        {
                            staged.Errors.Add(a.FileName + ": " + ex.Message);
                        }
                    }

                    try
                    {
                        var msg = Path.Combine(dir, "mail.msg");
                        inbox.SaveAsMsg(mail.EntryId, msg);
                        if (File.Exists(msg))
                        {
                            staged.MsgPath = msg;
                        }
                    }
                    catch (Exception ex) when (!(ex is OutOfMemoryException))
                    {
                        staged.Errors.Add("メール(.msg): " + ex.Message);
                    }

                    try
                    {
                        staged.Body = inbox.ReadBody(mail.EntryId, InboundImporter.NoteBodyMaxChars);
                    }
                    catch (Exception ex) when (!(ex is OutOfMemoryException))
                    {
                        staged.Errors.Add("本文: " + ex.Message);
                    }
                }

                return staged;
            });
            return result;
        }

        public void Dispose()
        {
            try
            {
                if (TempDir.Length > 0 && Directory.Exists(TempDir))
                {
                    Directory.Delete(TempDir, true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
