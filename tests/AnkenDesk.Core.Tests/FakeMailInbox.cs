using System;
using System.Collections.Generic;
using System.IO;
using AnkenDesk.Core;

namespace AnkenDesk.Core.Tests
{
    /// <summary>テスト用の偽の受信箱。</summary>
    internal sealed class FakeMailInbox : IMailInbox
    {
        public readonly List<InboundMail> Mails = new List<InboundMail>();
        public readonly HashSet<int> FailAttachments = new HashSet<int>();
        public bool FailMsg;
        public readonly List<InboundMail> Selected = new List<InboundMail>();
        public string Body = "本文のダミー";
        public bool FailBody;

        public IReadOnlyList<InboundMail> ListRecent(int days, int maxCount)
        {
            return Mails;
        }

        public void SaveAttachment(string entryId, int attachmentIndex, string path)
        {
            if (FailAttachments.Contains(attachmentIndex))
            {
                throw new IOException("保存できません");
            }

            File.WriteAllText(path, "attachment" + attachmentIndex);
        }

        public void SaveAsMsg(string entryId, string path)
        {
            if (FailMsg)
            {
                throw new IOException("msgを保存できません");
            }

            File.WriteAllText(path, "msg");
        }

        public IReadOnlyList<InboundMail> GetSelected(int maxCount)
        {
            return Selected.GetRange(0, Math.Min(maxCount, Selected.Count));
        }

        public string ReadBody(string entryId, int maxChars)
        {
            if (FailBody)
            {
                throw new IOException("本文を読めません");
            }

            return Body.Length > maxChars ? Body.Substring(0, maxChars) : Body;
        }

        public void Dispose()
        {
        }
    }
}
