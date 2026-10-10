using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AnkenDesk.Core
{
    /// <summary>受信箱のメールの添付ファイル1つ。</summary>
    public sealed class InboundAttachment
    {
        /// <summary>Outlookの添付ファイルの番号（1から）。取り出すときに使う。</summary>
        public int Index { get; set; }
        public string FileName { get; set; } = "";
        public long Size { get; set; }
    }

    /// <summary>受信箱のメール1通（一覧に出す分だけ。本文は持たない）。</summary>
    public sealed class InboundMail
    {
        /// <summary>OutlookのEntryID。メールを取り出すときに使う（受信箱の中にある間は変わらない）。</summary>
        public string EntryId { get; set; } = "";
        public string Subject { get; set; } = "";
        public string SenderName { get; set; } = "";
        public string SenderAddress { get; set; } = "";
        public DateTime ReceivedAt { get; set; }

        /// <summary>送信済みアイテムのメール（受信日時は送信日時）。</summary>
        public bool IsSent { get; set; }
        public List<InboundAttachment> Attachments { get; } = new List<InboundAttachment>();
    }

    /// <summary>Outlookの受信箱（実体は OutlookAccess）。画面やテストから差し替えられるようにしてある。</summary>
    public interface IMailInbox : IDisposable
    {
        /// <summary>受信箱（sent=true なら送信済みアイテム）の、新しい方から。days日以内、最大 maxCount 通。</summary>
        IReadOnlyList<InboundMail> ListRecent(int days, int maxCount, bool sent = false);

        /// <summary>メールの添付ファイルを、指定のパスに保存する。</summary>
        void SaveAttachment(string entryId, int attachmentIndex, string path);

        /// <summary>メールを.msgとして保存する。</summary>
        void SaveAsMsg(string entryId, string path);

        /// <summary>
        /// Outlookの画面で今選ばれているメール（ドラッグ&ドロップしたメール）。最大 maxCount 通。
        /// ドラッグすると、そのメールが選ばれた状態になっているので、選択を読む。メール以外の項目は除く。
        /// </summary>
        IReadOnlyList<InboundMail> GetSelected(int maxCount);

        /// <summary>メールの本文（テキスト）。長いときは maxChars 文字で切る。</summary>
        string ReadBody(string entryId, int maxChars);
    }

    /// <summary>取り込みの結果。</summary>
    public sealed class ImportOutcome
    {
        /// <summary>保存した見積書などのフルパス。</summary>
        public List<string> SavedFiles { get; } = new List<string>();
        public string? MsgPath { get; set; }
        public List<string> Errors { get; } = new List<string>();
        public bool ReceivedDateMarked { get; set; }

        /// <summary>メールの内容をメモに残したか。</summary>
        public bool NoteRecorded { get; set; }

        public bool AnythingSaved
        {
            get { return SavedFiles.Count > 0 || MsgPath != null; }
        }
    }

    public static class InboundImporter
    {
        /// <summary>旧版でも依頼でもない、調達先からの回答メール（.msg）の置き場所。「5.調達先見積もり」の中の、見積書と別のフォルダ。</summary>
        public const string MailFolder = "メール";

        /// <summary>
        /// 受信メールの添付ファイルを、「5.調達先見積もり」へ「略称　元のファイル名」で保存する（上書きしない）。
        /// 回答のメール自体も、.msg として「5.調達先見積もり\メール」へ保存できる。
        /// 保存できたものがあれば、記録（mail_log）に残し、markReceivedDate が指定されていれば「回答受領日」を入れる。
        /// 1つの保存の失敗で、ほかを止めない（失敗は Errors に入れる）。
        /// </summary>
        /// <summary>メモに残す本文の最大の長さ。全文は .msg に残る。</summary>
        public const int NoteBodyMaxChars = 4000;

        /// <summary>メモに残す文面: 受信日時・差出人・件名・本文（長ければ切る）。</summary>
        public static string MailNoteText(InboundMail mail, string body)
        {
            var text = (body ?? "").Replace("\r\n", "\n").Trim();
            if (text.Length > NoteBodyMaxChars)
            {
                text = text.Substring(0, NoteBodyMaxChars) + "\n…（以降は省略。全文は.msgを見てください）";
            }

            return "【メール】" + mail.ReceivedAt.ToString("yyyy/MM/dd HH:mm", System.Globalization.CultureInfo.InvariantCulture)
                + "　" + (string.IsNullOrEmpty(mail.SenderName) ? mail.SenderAddress : mail.SenderName + " <" + mail.SenderAddress + ">")
                + "\n件名: " + mail.Subject + "\n\n" + text;
        }

        public static ImportOutcome Import(
            IMailInbox inbox, AnkenDb db, AnkenRecord anken, string ankenFullPath, Supplier supplier, InboundMail mail,
            IReadOnlyCollection<int> attachmentIndexes, bool saveMsg, DateTime? markReceivedDate, DateTime now,
            bool recordBodyAsNote = false)
        {
            if (!Directory.Exists(ankenFullPath))
            {
                throw new DirectoryNotFoundException("案件フォルダが見つかりません: " + ankenFullPath);
            }

            var outcome = new ImportOutcome();
            var quoteDir = QuoteFiles.QuoteDir(ankenFullPath);

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
                    Directory.CreateDirectory(quoteDir);
                    var path = QuoteFiles.UniquePath(quoteDir, QuoteFiles.BuildFileName(supplier.ShortName, att.FileName));
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

            if (saveMsg)
            {
                try
                {
                    var dir = Path.Combine(quoteDir, MailFolder);
                    Directory.CreateDirectory(dir);
                    var name = mail.ReceivedAt.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture)
                        + FolderNames.Separator + Sanitizer.FileName(supplier.ShortName)
                        + FolderNames.Separator + Sanitizer.FileName(mail.Subject) + ".msg";
                    var path = QuoteFiles.UniquePath(dir, name);
                    inbox.SaveAsMsg(mail.EntryId, path);
                    if (File.Exists(path))
                    {
                        outcome.MsgPath = path;
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

            if (recordBodyAsNote)
            {
                try
                {
                    var body = inbox.ReadBody(mail.EntryId, NoteBodyMaxChars);
                    db.AddNote(anken.Id, supplier.Id, MailNoteText(mail, body), "mail");
                    outcome.NoteRecorded = true;
                }
                catch (Exception ex) when (!(ex is OutOfMemoryException))
                {
                    outcome.Errors.Add("メールの内容のメモ: " + ex.Message);
                }
            }

            if (outcome.AnythingSaved || outcome.NoteRecorded)
            {
                db.AddMailLog(new MailLogEntry
                {
                    AnkenId = anken.Id,
                    SupplierId = supplier.Id,
                    Kind = MailKind.Answer,
                    ToAddress = mail.SenderAddress,
                    Subject = mail.Subject,
                    Status = outcome.Errors.Count == 0 ? "取り込み済み" : "一部だけ取り込み済み",
                    EntryId = mail.EntryId,
                    SentAt = mail.ReceivedAt,
                    MsgPath = outcome.MsgPath == null ? null : Path.Combine(anken.FolderPath, FolderNames.Subfolders[4], MailFolder, Path.GetFileName(outcome.MsgPath)),
                    CreatedAt = now,
                });

                if (markReceivedDate.HasValue)
                {
                    db.MarkAnswerReceived(anken.Id, supplier.Id, markReceivedDate.Value.Date);
                    outcome.ReceivedDateMarked = true;
                }
            }

            return outcome;
        }
    }

    /// <summary>取り込み先の候補（案件と調達先）。</summary>
    public sealed class InboundCandidate
    {
        public AnkenRecord Anken { get; set; } = new AnkenRecord();
        public AnkenSupplier? Supplier { get; set; }
        public int Score { get; set; }
    }

    public static class InboundMatcher
    {
        /// <summary>
        /// 受信メールから、取り込み先の案件・調達先の候補を推定する（推定なので、人が確認して選ぶ）。
        /// 件名に品番が入っていれば +100、差出人のアドレスが案件の調達先のアドレスと同じなら +20、その調達先がまだ未回答なら +1。
        /// 点数のあるものだけ、点数の高い順（同点は回答期限の近い順）に返す。
        /// </summary>
        public static IReadOnlyList<InboundCandidate> Suggest(
            InboundMail mail,
            IEnumerable<AnkenRecord> ankens,
            IReadOnlyDictionary<long, IReadOnlyList<AnkenSupplier>> ankenSuppliers,
            IEnumerable<Supplier> suppliers)
        {
            var emailsBySupplier = suppliers.ToDictionary(s => s.Id, s => SplitAddresses(s.Email));
            var sender = (mail.SenderAddress ?? "").Trim();
            var result = new List<InboundCandidate>();

            foreach (var a in ankens)
            {
                var score = 0;
                var part = (a.PartNumber ?? "").Trim();
                if (part.Length >= 3 && (mail.Subject ?? "").IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    score += 100;
                }

                AnkenSupplier? matched = null;
                IReadOnlyList<AnkenSupplier>? list;
                if (sender.Length > 0 && ankenSuppliers.TryGetValue(a.Id, out list))
                {
                    foreach (var s in list)
                    {
                        List<string>? addrs;
                        if (emailsBySupplier.TryGetValue(s.SupplierId, out addrs) && addrs.Any(x => string.Equals(x, sender, StringComparison.OrdinalIgnoreCase)))
                        {
                            matched = s;
                            break;
                        }
                    }
                }

                if (matched != null)
                {
                    score += 20;
                    if (!matched.IsAnswered)
                    {
                        score += 1;
                    }
                }

                if (score > 0)
                {
                    result.Add(new InboundCandidate { Anken = a, Supplier = matched, Score = score });
                }
            }

            return result.OrderByDescending(c => c.Score).ThenBy(c => c.Anken.ReplyDueDate).ThenByDescending(c => c.Anken.Id).ToList();
        }

        private static List<string> SplitAddresses(string text)
        {
            return (text ?? "").Split(new[] { ';', '；' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        }
    }
}
