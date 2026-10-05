using System;
using System.Globalization;

namespace AnkenDesk.Core
{
    /// <summary>案件フォルダの名前と、その中に作る15個のサブフォルダの名前。</summary>
    public static class FolderNames
    {
        /// <summary>フォルダ名の区切り。全角スペース（Yasu確認、2026-10-05）。</summary>
        public const char Separator = '　';

        /// <summary>案件フォルダの中に作るサブフォルダ（現行のフォルダ構成。固定）。</summary>
        public static readonly string[] Subfolders =
        {
            "1.客先見積依頼内容",
            "2.図面",
            "3.図面要求確認シート",
            "4.調達先への見積依頼内容",
            "5.調達先見積もり",
            "6.見積計算",
            "7.客先提出見積もり",
            "8.調達先仕様図・提案図",
            "9.客先提出資料",
            "10.客先からの手配書",
            "11.手配書（社内）",
            "12.測定データ・帳票",
            "13.議事録",
            "14.流動実績",
            "15.その他資料・レター",
        };

        /// <summary>旧版の見積書PDFを移す先（「5.調達先見積もり」の中）。仮置き（HANDOFF.md §8 #6）。</summary>
        public const string OldVersionFolder = "_旧版";

        /// <summary>「YYYYMMDD　品番」、備考があれば「YYYYMMDD　品番　備考」。区切りは全角スペース1つ。</summary>
        public static string BuildAnkenFolderName(DateTime requestDate, string partNumber, string? note)
        {
            var part = Sanitizer.FileName(partNumber);
            if (part.Length == 0)
            {
                throw new ArgumentException("品番が空です。", nameof(partNumber));
            }

            var name = requestDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + Separator + part;
            var n = Sanitizer.FileName(note);
            if (n.Length > 0)
            {
                name += Separator + n;
            }

            return name;
        }
    }
}
