using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AnkenDesk.Core
{
    /// <summary>1通ぶんの送信の計画と結果。</summary>
    public sealed class MailSendOutcome
    {
        public Supplier Supplier { get; set; } = new Supplier();
        public MailRequest Request { get; set; } = new MailRequest();
        public MailResult Result { get; set; } = new MailResult();
    }

    public static class MailSender
    {
        /// <summary>宛先のアドレス（「;」区切りで複数可）が、すべて正しい形か。</summary>
        public static bool IsValidAddressList(string? text)
        {
            var parts = (text ?? "").Split(new[] { ';', '；' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            if (parts.Count == 0)
            {
                return false;
            }

            foreach (var a in parts)
            {
                var at = a.IndexOf('@');
                if (at <= 0 || at != a.LastIndexOf('@') || at == a.Length - 1 || a.Any(char.IsWhiteSpace) || a.IndexOf('.', at) < 0)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>調達先ごとのメールを作る。件名・本文は雛形に差し込む。</summary>
        public static MailRequest BuildRequest(
            AnkenRecord anken, DateTime dueDate, Supplier supplier,
            string subjectTemplate, string bodyTemplate, IEnumerable<string> attachments, bool includeSignature)
        {
            var req = new MailRequest
            {
                ToAddress = supplier.Email.Trim(),
                Subject = MailTemplates.Apply(subjectTemplate, supplier, anken, dueDate),
                Body = MailTemplates.Apply(bodyTemplate, supplier, anken, dueDate),
                IncludeSignature = includeSignature,
            };
            req.Attachments.AddRange(attachments);
            return req;
        }

        /// <summary>
        /// 順に送る。送信済みになれなかった（送信トレイに残った・失敗した）時点で止める（残りは送らない）。
        /// 送信したメールは「4.調達先への見積依頼内容」へ .msg として保存し、記録（mail_log）に残す。
        /// 見積依頼が送信済みになったら、その調達先の「依頼送付日」を入れる。
        /// </summary>
        public static IReadOnlyList<MailSendOutcome> SendAll(
            IMailGateway gateway, AnkenDb db, AnkenRecord anken, string ankenFullPath, MailKind kind,
            IReadOnlyList<KeyValuePair<Supplier, MailRequest>> items, DateTime now)
        {
            var outcomes = new List<MailSendOutcome>();
            var dir = Path.Combine(ankenFullPath, FolderNames.Subfolders[3]);
            Directory.CreateDirectory(dir);

            foreach (var item in items)
            {
                var supplier = item.Key;
                var request = item.Value;
                var fileName = now.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture)
                    + FolderNames.Separator + Sanitizer.FileName(supplier.ShortName)
                    + FolderNames.Separator + Sanitizer.FileName(request.Subject) + ".msg";
                var msgPath = QuoteFiles.UniquePath(dir, fileName);

                MailResult result;
                try
                {
                    result = gateway.SendAndArchive(request, msgPath);
                }
                catch (Exception ex)
                {
                    result = new MailResult { Outcome = MailOutcome.Failed, Message = ex.Message };
                }

                outcomes.Add(new MailSendOutcome { Supplier = supplier, Request = request, Result = result });

                db.AddMailLog(new MailLogEntry
                {
                    AnkenId = anken.Id,
                    SupplierId = supplier.Id,
                    Kind = kind,
                    ToAddress = request.ToAddress,
                    Subject = request.Subject,
                    Status = StatusText(result.Outcome),
                    EntryId = result.EntryId,
                    SentAt = result.SentAt,
                    MsgPath = result.MsgSaved ? Path.Combine(anken.FolderPath, FolderNames.Subfolders[3], Path.GetFileName(msgPath)) : null,
                    CreatedAt = now,
                });

                if (result.Outcome == MailOutcome.Sent && kind == MailKind.Request)
                {
                    db.MarkRequestSent(anken.Id, supplier.Id, now.Date);
                }

                if (result.Outcome != MailOutcome.Sent)
                {
                    break;
                }
            }

            return outcomes;
        }

        public static string StatusText(MailOutcome outcome)
        {
            switch (outcome)
            {
                case MailOutcome.Sent:
                    return "送信済み";
                case MailOutcome.Queued:
                    return "送信トレイに残っている";
                default:
                    return "失敗";
            }
        }

        public static string KindText(MailKind kind)
        {
            return kind == MailKind.Request ? "依頼" : "催促";
        }
    }
}
