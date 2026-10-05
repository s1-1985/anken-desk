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
    }
}
