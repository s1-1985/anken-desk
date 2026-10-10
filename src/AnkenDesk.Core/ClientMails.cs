using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace AnkenDesk.Core
{
    /// <summary>メールの件名から、品番らしい文字列の候補を拾う（案件登録の入力の手間を減らす）。推定なので、人が確認して選ぶ。</summary>
    public static class PartNumberGuess
    {
        private static readonly Regex Token = new Regex(@"[A-Za-z0-9][A-Za-z0-9\-_./]{3,}", RegexOptions.Compiled);
        private static readonly Regex DateLike = new Regex(@"^(\d{8}|\d{4}[/.\-]\d{1,2}[/.\-]\d{1,2})$", RegexOptions.Compiled);

        /// <summary>
        /// 英数字・ハイフン・ピリオドなどが4文字以上つながった、数字を含む語を、出てきた順に（最大5つ）。
        /// 日付（20261005、2026/10/06）は除く。
        /// </summary>
        public static IReadOnlyList<string> FromSubject(string subject)
        {
            var result = new List<string>();
            foreach (Match m in Token.Matches(subject ?? ""))
            {
                var t = m.Value.Trim('-', '_', '.', '/');
                if (t.Length < 5 || !t.Any(char.IsDigit) || DateLike.IsMatch(t))
                {
                    continue;
                }

                if (!result.Contains(t, StringComparer.OrdinalIgnoreCase))
                {
                    result.Add(t);
                }

                if (result.Count >= 5)
                {
                    break;
                }
            }

            return result;
        }
    }

    /// <summary>客先とのやり取りのメール（依頼メールなど）を、案件へ取り込む。調達先の回答とは別の置き場所・別の記録にする。</summary>
    public static class ClientMailImporter
    {
        /// <summary>メールの .msg の名前: 「YYYYMMDD　客先　件名.msg」。</summary>
        public static string MsgFileName(InboundMail mail)
        {
            return mail.ReceivedAt.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
                + FolderNames.Separator + "客先"
                + FolderNames.Separator + Sanitizer.FileName(mail.Subject) + ".msg";
        }

        /// <summary>
        /// 客先のメールの添付を、種類で「1.客先見積依頼内容」「2.図面」へ振り分けて保存し（元の名前のまま、同名は連番）、
        /// .msg を「1.客先見積依頼内容」へ、内容をメモ（調達先なし）へ。保存できたものは記録（mail_log。種別「客先」）に残す。
        /// 1つの失敗で、ほかを止めない。
        /// </summary>
        public static ImportOutcome Import(
            IMailInbox inbox, AnkenDb db, AnkenRecord anken, string ankenFullPath, InboundMail mail,
            IReadOnlyCollection<int> attachmentIndexes, bool saveMsg, bool recordNote, DateTime now)
        {
            if (!Directory.Exists(ankenFullPath))
            {
                throw new DirectoryNotFoundException("案件フォルダが見つかりません: " + ankenFullPath);
            }

            var outcome = new ImportOutcome();
            foreach (var index in attachmentIndexes)
            {
                var att = mail.Attachments.FirstOrDefault(a => a.Index == index);
                if (att == null)
                {
                    outcome.Errors.Add("添付ファイル（番号" + index + "）が見つかりません。");
                    continue;
                }

                try
                {
                    var dir = Path.Combine(ankenFullPath, RequestFiles.TargetSubfolder(att.FileName));
                    Directory.CreateDirectory(dir);
                    var path = QuoteFiles.UniquePath(dir, Sanitizer.FileName(att.FileName));
                    inbox.SaveAttachment(mail.EntryId, att.Index, path);
                    if (File.Exists(path))
                    {
                        outcome.SavedFiles.Add(path);
                    }
                    else
                    {
                        outcome.Errors.Add(att.FileName + ": 保存できませんでした。");
                    }
                }
                catch (Exception ex) when (!(ex is OutOfMemoryException))
                {
                    outcome.Errors.Add(att.FileName + ": " + ex.Message);
                }
            }

            string? msgSaved = null;
            if (saveMsg)
            {
                try
                {
                    var dir = Path.Combine(ankenFullPath, FolderNames.Subfolders[0]);
                    Directory.CreateDirectory(dir);
                    var path = QuoteFiles.UniquePath(dir, MsgFileName(mail));
                    inbox.SaveAsMsg(mail.EntryId, path);
                    if (File.Exists(path))
                    {
                        outcome.MsgPath = path;
                        msgSaved = path;
                    }
                    else
                    {
                        outcome.Errors.Add("メール(.msg): 保存できませんでした。");
                    }
                }
                catch (Exception ex) when (!(ex is OutOfMemoryException))
                {
                    outcome.Errors.Add("メール(.msg): " + ex.Message);
                }
            }

            string? body = null;
            if (recordNote)
            {
                try
                {
                    body = inbox.ReadBody(mail.EntryId, InboundImporter.NoteBodyMaxChars);
                }
                catch (Exception ex) when (!(ex is OutOfMemoryException))
                {
                    outcome.Errors.Add("メールの内容のメモ: " + ex.Message);
                }
            }

            Finish(db, anken, mail, msgSaved, body, outcome, now);
            return outcome;
        }

        /// <summary>
        /// 案件の登録のときのように、メールの .msg と本文を先に取り出してある場合の記録（添付は、呼び出し側が振り分けて保存済み）。
        /// <paramref name="stagedMsgPath"/> は、取り出し済みの .msg の場所。コピーして「1.客先見積依頼内容」へ入れる。
        /// </summary>
        public static ImportOutcome RecordStaged(
            AnkenDb db, AnkenRecord anken, string ankenFullPath, InboundMail mail, string? stagedMsgPath, string? body, DateTime now)
        {
            var outcome = new ImportOutcome();
            string? msgSaved = null;
            if (stagedMsgPath != null)
            {
                try
                {
                    var dir = Path.Combine(ankenFullPath, FolderNames.Subfolders[0]);
                    Directory.CreateDirectory(dir);
                    var path = QuoteFiles.UniquePath(dir, MsgFileName(mail));
                    File.Copy(stagedMsgPath, path, false);
                    outcome.MsgPath = path;
                    msgSaved = path;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    outcome.Errors.Add("メール(.msg): " + ex.Message);
                }
            }

            Finish(db, anken, mail, msgSaved, body, outcome, now);
            return outcome;
        }

        private static void Finish(AnkenDb db, AnkenRecord anken, InboundMail mail, string? msgSaved, string? body, ImportOutcome outcome, DateTime now)
        {
            if (body != null)
            {
                try
                {
                    db.AddNote(anken.Id, null, InboundImporter.MailNoteText(mail, body), "mail");
                    outcome.NoteRecorded = true;
                }
                catch (ArgumentException ex)
                {
                    outcome.Errors.Add("メールの内容のメモ: " + ex.Message);
                }
            }

            if (outcome.AnythingSaved || outcome.NoteRecorded)
            {
                db.AddMailLog(new MailLogEntry
                {
                    AnkenId = anken.Id,
                    SupplierId = null,
                    Kind = MailKind.Client,
                    ToAddress = mail.SenderAddress,
                    Subject = mail.Subject,
                    Status = outcome.Errors.Count == 0 ? "取り込み済み" : "一部だけ取り込み済み",
                    EntryId = mail.EntryId,
                    SentAt = mail.ReceivedAt,
                    MsgPath = msgSaved == null ? null : Path.Combine(anken.FolderPath, FolderNames.Subfolders[0], Path.GetFileName(msgSaved)),
                    CreatedAt = now,
                });
            }
        }
    }

    /// <summary>受信メールの自動仕分けの1件。</summary>
    public sealed class TriageProposal
    {
        public InboundMail Mail { get; set; } = new InboundMail();
        public InboundCandidate Candidate { get; set; } = new InboundCandidate();

        /// <summary>品番が件名にあり、差出人がその案件の調達先と一致する（確かな候補）。確かでないものは、人が確認する。</summary>
        public bool Confident { get; set; }

        /// <summary>取り込む添付の初期値（PDFとExcel。画像は除く。無ければ全部）。</summary>
        public List<int> AttachmentIndexes { get; } = new List<int>();
    }

    public static class InboundTriage
    {
        /// <summary>確かな候補の点数（品番 100 + 調達先のアドレス 20）。</summary>
        public const int ConfidentScore = 120;

        /// <summary>件名に品番が入っている点数。これ未満は、仕分けの候補に出さない。</summary>
        public const int PartMatchScore = 100;

        private static readonly string[] Wanted = { ".pdf", ".xlsx", ".xlsm", ".xls" };

        /// <summary>
        /// 受信メールを、案件・調達先ごとに仕分ける。取り込み済み（EntryIDが記録にある）のメール、添付の無いメール、
        /// 終わった案件（受注・失注・保留）への候補、件名に品番が入っていないメールは出さない。確かな候補（<see cref="TriageProposal.Confident"/>）を先に、
        /// 受信日時の新しい順。
        /// </summary>
        public static IReadOnlyList<TriageProposal> Plan(
            IEnumerable<InboundMail> mails,
            IEnumerable<AnkenRecord> ankens,
            IReadOnlyDictionary<long, IReadOnlyList<AnkenSupplier>> ankenSuppliers,
            IEnumerable<Supplier> suppliers,
            ISet<string> alreadyImported)
        {
            var open = ankens.Where(a => !AnkenStatus.IsClosed(a.Status)).ToList();
            var sup = suppliers.ToList();
            var result = new List<TriageProposal>();
            foreach (var m in mails)
            {
                if (m.Attachments.Count == 0 || alreadyImported.Contains(m.EntryId))
                {
                    continue;
                }

                // 件名に案件の品番が入っているものだけ。差出人が調達先というだけでは、別件のメールも拾うので出さない。
                var cand = InboundMatcher.Suggest(m, open, ankenSuppliers, sup).FirstOrDefault();
                if (cand == null || cand.Score < PartMatchScore)
                {
                    continue;
                }

                var p = new TriageProposal { Mail = m, Candidate = cand, Confident = cand.Supplier != null && cand.Score >= ConfidentScore };
                foreach (var a in m.Attachments.Where(a => Wanted.Contains(Path.GetExtension(a.FileName).ToLowerInvariant())))
                {
                    p.AttachmentIndexes.Add(a.Index);
                }

                if (p.AttachmentIndexes.Count == 0)
                {
                    p.AttachmentIndexes.AddRange(m.Attachments.Select(a => a.Index));
                }

                result.Add(p);
            }

            return result.OrderByDescending(r => r.Confident).ThenByDescending(r => r.Mail.ReceivedAt).ToList();
        }
    }
}
