using System;
using System.Collections.Generic;

namespace AnkenDesk.Core
{
    public enum MailKind
    {
        /// <summary>見積依頼。</summary>
        Request,

        /// <summary>催促（未回答の調達先へ）。</summary>
        Reminder,

        /// <summary>調達先から届いた回答（受信メールの取り込み）。</summary>
        Answer,

        /// <summary>客先とのやり取りのメール（依頼メール、問い合わせなど。受信・送信どちらも）。</summary>
        Client,
    }

    /// <summary>送るメール1通。調達先ごとに別々のメールにする（調達先同士のアドレスを見せない。HANDOFF.md §3.4）。</summary>
    public sealed class MailRequest
    {
        /// <summary>宛先。複数のときは「;」で区切る。</summary>
        public string ToAddress { get; set; } = "";
        public string Subject { get; set; } = "";

        /// <summary>本文（テキスト）。</summary>
        public string Body { get; set; } = "";
        public List<string> Attachments { get; } = new List<string>();

        /// <summary>Outlookの署名（テキスト版）を本文の末尾に付ける。</summary>
        public bool IncludeSignature { get; set; }
    }

    public enum MailOutcome
    {
        /// <summary>送信済みアイテムに入った。</summary>
        Sent,

        /// <summary>送信トレイに残っている（オフライン作業中などで、まだ送り出されていない）。</summary>
        Queued,

        /// <summary>送信できなかった。</summary>
        Failed,
    }

    public sealed class MailResult
    {
        public MailOutcome Outcome { get; set; }
        public string Message { get; set; } = "";

        /// <summary>送信済みのメールのEntryID（送信前後で変わるので、送信済みで探し直した値）。</summary>
        public string? EntryId { get; set; }
        public DateTime? SentAt { get; set; }

        /// <summary>送信したメールを.msgとして保存できたか。保存先のフルパスは、依頼したパスと同じ。</summary>
        public bool MsgSaved { get; set; }
    }

    /// <summary>Outlookの操作（実体は OutlookAccess）。画面やテストから差し替えられるようにしてある。</summary>
    public interface IMailGateway : IDisposable
    {
        /// <summary>Outlookが「オフライン作業中」か。オフラインだと、送信しても送信トレイに残る。</summary>
        bool IsOffline();

        /// <summary>
        /// メールを作って送り、送信済みに入るまで待って、.msgとして msgPath に保存する。
        /// 失敗は例外ではなく Failed で返す（1通の失敗で全体を止めないため）。
        /// </summary>
        MailResult SendAndArchive(MailRequest request, string msgPath);

        /// <summary>メールを作って、Outlookの画面で開く（送信は人がする）。</summary>
        void OpenDraft(MailRequest request);
    }

    /// <summary>メールの記録1件。</summary>
    public sealed class MailLogEntry
    {
        public long Id { get; set; }
        public long AnkenId { get; set; }
        public long? SupplierId { get; set; }
        public MailKind Kind { get; set; }
        public string ToAddress { get; set; } = "";
        public string Subject { get; set; } = "";

        /// <summary>送信済み／送信トレイに残っている／失敗。</summary>
        public string Status { get; set; } = "";
        public string? EntryId { get; set; }
        public DateTime? SentAt { get; set; }

        /// <summary>.msgのWork spaceからの相対パス。</summary>
        public string? MsgPath { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
