using System;
using System.Collections.Generic;

namespace AnkenDesk.Core
{
    /// <summary>得意先・種別。名前はWork spaceの直下のフォルダ名にもなる（例: 得意先と種別をつないだ名前）。</summary>
    public sealed class Client
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
    }

    /// <summary>見積を取る数量の1行。種別は「試作」「量産」「年間見込数」など、単位は「個」「Lot」「個/年」など。</summary>
    public sealed class QuantityPattern
    {
        /// <summary>DBのID。登録前は0。</summary>
        public long Id { get; set; }
        public string Kind { get; set; } = "";
        public decimal Quantity { get; set; }
        public string Unit { get; set; } = "";
    }

    /// <summary>案件登録の入力。</summary>
    public sealed class AnkenInput
    {
        public long ClientId { get; set; }
        public DateTime RequestDate { get; set; }
        public string PartNumber { get; set; } = "";
        public string PartName { get; set; } = "";
        public string Note { get; set; } = "";
        public DateTime ReplyDueDate { get; set; }
        public List<QuantityPattern> Quantities { get; } = new List<QuantityPattern>();

        /// <summary>見積依頼書に載せる項目（材質、処理、荷姿など）。項目名と内容。</summary>
        public Dictionary<string, string> Items { get; } = new Dictionary<string, string>();
    }

    /// <summary>登録済みの案件。</summary>
    public sealed class AnkenRecord
    {
        public long Id { get; set; }
        public long ClientId { get; set; }
        public string ClientName { get; set; } = "";
        public DateTime RequestDate { get; set; }
        public string PartNumber { get; set; } = "";
        public string PartName { get; set; } = "";
        public string Note { get; set; } = "";
        public DateTime ReplyDueDate { get; set; }

        /// <summary>Work spaceからの相対パス（得意先・種別のフォルダ名\案件フォルダ名）。</summary>
        public string FolderPath { get; set; } = "";

        /// <summary>案件の進み具合（<see cref="AnkenStatus"/>）。</summary>
        public string Status { get; set; } = AnkenStatus.InProgress;
    }

    /// <summary>
    /// 案件の進み具合。現行ExcelのSTATUSの選択肢が未確認（HANDOFF §8 #3）なので、仮の5つ【仮置き】。
    /// 受注・失注・保留は「終わった／止めた」案件で、ホームの「要対応」に出さない。
    /// </summary>
    public static class AnkenStatus
    {
        public const string InProgress = "見積中";
        public const string Submitted = "客先提出済み";
        public const string Won = "受注";
        public const string Lost = "失注";
        public const string OnHold = "保留";

        public static readonly string[] All = { InProgress, Submitted, Won, Lost, OnHold };

        public static bool IsClosed(string status)
        {
            return status == Won || status == Lost || status == OnHold;
        }
    }

    /// <summary>調達先（マスター）。略称は見積書PDFのファイル名に使う。</summary>
    public sealed class Supplier
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public string ShortName { get; set; } = "";
        public string Email { get; set; } = "";
        public string Address { get; set; } = "";
    }

    /// <summary>案件に対して見積を依頼した調達先と、その回答のうち調達先ごとに1つの項目。</summary>
    public sealed class AnkenSupplier
    {
        public long AnkenId { get; set; }
        public long SupplierId { get; set; }
        public string SupplierName { get; set; } = "";
        public string ShortName { get; set; } = "";
        public DateTime? SentAt { get; set; }
        public DateTime? ReceivedAt { get; set; }

        /// <summary>別費用（型・治具など）。仮置き（HANDOFF.md §8 #7）。</summary>
        public string ExtraCost { get; set; } = "";
        public string Relaxation { get; set; } = "";
        public string Note { get; set; } = "";

        public bool IsAnswered
        {
            get { return ReceivedAt.HasValue; }
        }
    }

    /// <summary>調達先の回答のうち、数量パターンごとの単価とリードタイム。</summary>
    public sealed class Quote
    {
        public long SupplierId { get; set; }
        public long PatternId { get; set; }
        public decimal? UnitPrice { get; set; }
        public int? LeadTimeDays { get; set; }
    }

    /// <summary>出し直しの前の回答（旧版）。履歴として残したもの。</summary>
    public sealed class AnswerVersion
    {
        public long Id { get; set; }
        public long AnkenId { get; set; }
        public long SupplierId { get; set; }

        /// <summary>1から数える。新しい版ほど大きい。</summary>
        public int Version { get; set; }
        public DateTime ArchivedAt { get; set; }
        public DateTime? SentAt { get; set; }
        public DateTime? ReceivedAt { get; set; }
        public string ExtraCost { get; set; } = "";
        public string Relaxation { get; set; } = "";
        public string Note { get; set; } = "";

        /// <summary>この版の見積書ファイル名（旧版フォルダに移したもの）。改行で区切る。</summary>
        public string Files { get; set; } = "";
        public List<Quote> Quotes { get; } = new List<Quote>();
    }
}
