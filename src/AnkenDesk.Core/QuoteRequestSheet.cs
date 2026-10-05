using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using ClosedXML.Excel;

namespace AnkenDesk.Core
{
    /// <summary>見積依頼書に載せる項目の名前。「見積依頼」シートの並びのとおり。</summary>
    public static class QuoteRequestItems
    {
        /// <summary>案件登録の画面で、最初から出す項目。</summary>
        public static readonly string[] BasicNames = { "材質", "処理", "荷姿（一次）", "荷姿（二次）", "帳票", "検査内容" };

        /// <summary>案件登録の画面で、「ほか」にまとめる項目。</summary>
        public static readonly string[] ExtraNames = { "納期", "SOP", "形態", "客先", "適用", "納場", "履歴", "備考" };

        public static IEnumerable<string> AllNames
        {
            get { return BasicNames.Concat(ExtraNames); }
        }

        /// <summary>既定値を、設定として保存するときのキーの頭。</summary>
        public const string DefaultSettingPrefix = "item_default:";
    }

    /// <summary>
    /// 見積依頼書（Excel）を作る。現行の「見積依頼」シートをテンプレートにして、値を埋める（HANDOFF.md §8 #12）。
    /// テンプレートの罫線・結合・注意書き・文字の装飾はそのまま使う。1案件につき1枚（品番の列は1つ）。
    /// </summary>
    public static class QuoteRequestSheet
    {
        private const string ResourceName = "AnkenDesk.Core.QuoteRequestTemplate.xlsx";

        // テンプレートの行（B列が見出し、C〜Eが値）。
        private const int RowPartNumber = 6;
        private const int RowPartName = 7;
        private const int FirstTrialRow = 8;
        private const int LastTrialRow = 11;
        private const int FirstProductionRow = 12;
        private const int LastProductionRow = 17;
        private const int AnnualRow = 18;
        private const int RowDueDate = 37;
        private const int RowFooter = 38;
        private const int FirstDataRow = 6;

        private static readonly Dictionary<string, int> ItemRows = new Dictionary<string, int>
        {
            { "材質", 20 }, { "処理", 22 }, { "荷姿（一次）", 23 }, { "荷姿（二次）", 24 },
            { "帳票", 26 }, { "検査内容", 27 }, { "納期", 29 }, { "SOP", 30 }, { "形態", 31 },
            { "客先", 32 }, { "適用", 33 }, { "納場", 34 }, { "履歴", 35 }, { "備考", 36 },
        };

        /// <summary>「YYYYMMDD　見積依頼.xlsx」。品番は入れない（現行の運用。HANDOFF.md §2.2）。</summary>
        public static string FileName(DateTime date)
        {
            return date.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + FolderNames.Separator + "見積依頼.xlsx";
        }

        /// <summary>テンプレートに入っている、項目の初期の文章（帳票、検査内容、納期、履歴など）。案件登録の入力欄の初期値に使う。</summary>
        public static IReadOnlyDictionary<string, string> TemplateDefaults()
        {
            var result = new Dictionary<string, string>();
            using (var wb = OpenTemplate())
            {
                var ws = wb.Worksheet(1);
                foreach (var kv in ItemRows)
                {
                    result[kv.Key] = ws.Cell(kv.Value, 3).GetString();
                }
            }

            return result;
        }

        /// <summary>見積依頼書を作って保存する。入りきらない数量パターンがあれば <see cref="ArgumentException"/>（何も書かない）。</summary>
        public static void Write(string path, AnkenInput input)
        {
            using (var wb = Build(input))
            {
                wb.SaveAs(path);
            }
        }

        /// <summary>見積依頼書を作る（保存はしない）。</summary>
        public static XLWorkbook Build(AnkenInput input)
        {
            var slots = AssignQuantities(input.Quantities);

            var wb = OpenTemplate();
            try
            {
                var ws = wb.Worksheet(1);
                RemoveExtraBlocks(ws);

                ws.Cell(RowPartNumber, 3).Value = input.PartNumber.Trim();
                ws.Cell(RowPartName, 3).Value = input.PartName.Trim();

                for (var row = FirstTrialRow; row <= AnnualRow; row++)
                {
                    QuantityPattern? q;
                    if (slots.TryGetValue(row, out q))
                    {
                        ws.Cell(row, 3).Value = q.Kind;
                        ws.Cell(row, 4).Value = (double)q.Quantity;
                        var unit = q.Unit.Trim();
                        ws.Cell(row, 5).Value = unit.Length > 0 ? unit : (row == AnnualRow ? "個/年" : "個/Lot");
                    }
                    else
                    {
                        // 使わない枠は、種別と単位も消して、空欄にする。
                        ws.Cell(row, 3).Clear(XLClearOptions.Contents);
                        ws.Cell(row, 4).Clear(XLClearOptions.Contents);
                        ws.Cell(row, 5).Clear(XLClearOptions.Contents);
                    }
                }

                foreach (var kv in ItemRows)
                {
                    string value;
                    if (!input.Items.TryGetValue(kv.Key, out value) || string.IsNullOrWhiteSpace(value))
                    {
                        ws.Cell(kv.Value, 3).Clear(XLClearOptions.Contents);
                    }
                    else
                    {
                        ws.Cell(kv.Value, 3).Value = value.Trim();
                    }
                }

                ws.Cell(RowDueDate, 3).Value = FormatDate(input.ReplyDueDate);

                SetupPage(ws);
                wb.Properties.Author = "";
                return wb;
            }
            catch
            {
                wb.Dispose();
                throw;
            }
        }

        // 数量パターンを、テンプレートの行に割り当てる。試作は4行、量産は6行、年間見込数は1行。
        private static Dictionary<int, QuantityPattern> AssignQuantities(IEnumerable<QuantityPattern> quantities)
        {
            var slots = new Dictionary<int, QuantityPattern>();
            var trial = new Queue<int>(Enumerable.Range(FirstTrialRow, LastTrialRow - FirstTrialRow + 1));
            var production = new Queue<int>(Enumerable.Range(FirstProductionRow, LastProductionRow - FirstProductionRow + 1));
            var annual = new Queue<int>(new[] { AnnualRow });

            foreach (var q in quantities)
            {
                var kind = q.Kind.Trim();
                Queue<int> queue;
                if (kind == "試作")
                {
                    queue = trial;
                }
                else if (kind == "量産")
                {
                    queue = production;
                }
                else if (kind == "年間見込数")
                {
                    queue = annual;
                }
                else
                {
                    throw new ArgumentException("見積依頼書に載せられない数量の種別です: 「" + kind + "」（「試作」「量産」「年間見込数」のどれかにしてください）");
                }

                if (queue.Count == 0)
                {
                    var capacity = kind == "試作" ? 4 : (kind == "量産" ? 6 : 1);
                    throw new ArgumentException("「" + kind + "」の数量が多すぎます（見積依頼書には" + capacity + "行まで載せられます）。");
                }

                slots[queue.Dequeue()] = q;
            }

            return slots;
        }

        // テンプレートは品番の列が3つ（C〜E、F〜H、I〜K）。1案件につき1枚なので、2つ目と3つ目を取り除く。
        private static void RemoveExtraBlocks(IXLWorksheet ws)
        {
            var footer = ws.Cell(RowFooter, 2).GetString();
            var footerHeight = ws.Row(RowFooter).Height;

            foreach (var m in ws.MergedRanges.ToList())
            {
                var addr = m.RangeAddress;
                if (addr.LastAddress.ColumnNumber >= 6)
                {
                    ws.Range(addr).Unmerge();
                }
            }

            ws.Columns("F:K").Delete();
            ws.DataValidations.Delete(dv => true);

            // 1〜5行目は、番号とプルダウンの元の値。いらないので空にして、行を低くする。
            ws.Range("A1:E5").Clear(XLClearOptions.Contents);
            for (var r = 1; r <= 5; r++)
            {
                ws.Row(r).Height = 6;
            }

            // 末尾の注意書きは、全部の列にまたがっていたので、B〜Eに結び直す。幅が狭くなる分、行を高くする。
            var range = ws.Range(RowFooter, 2, RowFooter, 5);
            range.Merge();
            ws.Cell(RowFooter, 2).Value = footer;
            ws.Row(RowFooter).Height = Math.Max(footerHeight, 175);
        }

        private static void SetupPage(IXLWorksheet ws)
        {
            ws.PageSetup.PrintAreas.Clear();
            ws.PageSetup.PrintAreas.Add(FirstDataRow, 2, RowFooter, 5);
            ws.PageSetup.PageOrientation = XLPageOrientation.Portrait;
            ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
            ws.PageSetup.FitToPages(1, 1);
        }

        private static string FormatDate(DateTime d)
        {
            return d.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) + "（" + "日月火水木金土"[(int)d.DayOfWeek] + "）";
        }

        private static XLWorkbook OpenTemplate()
        {
            var asm = typeof(QuoteRequestSheet).GetTypeInfo().Assembly;
            using (var stream = asm.GetManifestResourceStream(ResourceName))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException("見積依頼書のテンプレートが見つかりません。");
                }

                // ClosedXML は読み込み後にストリームを使わないが、念のため複製から開く。
                var ms = new MemoryStream();
                stream.CopyTo(ms);
                ms.Position = 0;
                return new XLWorkbook(ms);
            }
        }
    }
}
