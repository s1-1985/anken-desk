using System;
using System.Collections.Generic;
using System.IO;
using AnkenDesk.Core;

namespace AnkenDesk.Core.Tests
{
    /// <summary>テスト用の偽のOutlook。送ったメールを覚えておき、指定した結果を返す。</summary>
    internal sealed class FakeMailGateway : IMailGateway
    {
        public readonly List<MailRequest> Sent = new List<MailRequest>();
        public readonly List<MailRequest> Drafts = new List<MailRequest>();
        public bool Offline;

        /// <summary>n通目（0から）の結果を変える。指定が無ければ Sent。</summary>
        public readonly Dictionary<int, MailOutcome> Outcomes = new Dictionary<int, MailOutcome>();
        public readonly Dictionary<int, Exception> Throws = new Dictionary<int, Exception>();

        public bool IsOffline()
        {
            return Offline;
        }

        public MailResult SendAndArchive(MailRequest request, string msgPath)
        {
            var n = Sent.Count;
            Sent.Add(request);

            Exception ex;
            if (Throws.TryGetValue(n, out ex))
            {
                throw ex;
            }

            MailOutcome outcome;
            if (!Outcomes.TryGetValue(n, out outcome))
            {
                outcome = MailOutcome.Sent;
            }

            var saved = false;
            if (outcome == MailOutcome.Sent)
            {
                File.WriteAllText(msgPath, "msg");
                saved = true;
            }

            return new MailResult
            {
                Outcome = outcome,
                Message = outcome.ToString(),
                EntryId = outcome == MailOutcome.Sent ? "ENTRY" + n : null,
                SentAt = outcome == MailOutcome.Sent ? new DateTime(2026, 10, 5, 14, 12, 3) : (DateTime?)null,
                MsgSaved = saved,
            };
        }

        public void OpenDraft(MailRequest request)
        {
            Drafts.Add(request);
        }

        public void Dispose()
        {
        }
    }
}
