using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ClosedXML.Excel;

namespace AnkenDesk.Core
{
    /// <summary>Excelへの書き出し（案件の比較表、案件の一覧）。ClosedXMLで作る。</summary>
    public static class ExcelExports
    {
        public static string PatternText(QuantityPattern p)
        {
            return p.Kind + " " + p.Quantity.ToString("#,##0.####", CultureInfo.InvariantCulture) + p.Unit;
        }

        /// <summary>
        /// 案件の比較表（行=数量パターン、列=調達先）を1枚のシートに書く。
        /// 最安のセルは、太字・色つき・「最安」の文字つき。単価は値のまま（数値）で入れる。
        /// 客先へ出すものではなく、見積計算（社内）の元にする表【仮置き】。
        /// </summary>
        public static void WriteComparison(string path, AnkenRecord anken, IReadOnlyList<QuantityPattern> patterns,
            IReadOnlyList<AnkenSupplier> suppliers, IReadOnlyList<Quote> quotes)
        {
            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("比較表");
                ws.Cell(1, 1).Value = Home.Title(anken);
                ws.Cell(1, 1).Style.Font.Bold = true;
                ws.Cell(1, 1).Style.Font.FontSize = 14;
                ws.Cell(2, 1).Value = anken.ClientName + "　回答期限 " + anken.ReplyDueDate.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture)
                    + (string.IsNullOrWhiteSpace(anken.PartName) ? "" : "　品名 " + anken.PartName);
                ws.Cell(3, 1).Value = "作成 " + DateTime.Now.ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture);

                const int head = 5;
                ws.Cell(head, 1).Value = "数量";
                for (var i = 0; i < suppliers.Count; i++)
                {
                    var col = 2 + i * 2;
                    ws.Cell(head, col).Value = suppliers[i].SupplierName;
                    ws.Range(head, col, head, col + 1).Merge();
                    ws.Cell(head + 1, col).Value = "単価";
                    ws.Cell(head + 1, col + 1).Value = "リードタイム(日)";
                }

                ws.Range(head, 1, head + 1, 1 + suppliers.Count * 2).Style.Font.Bold = true;
                ws.Range(head, 1, head + 1, 1 + suppliers.Count * 2).Style.Fill.BackgroundColor = XLColor.FromHtml("#E8EEF1");
                ws.Range(head, 1, head + 1, 1 + suppliers.Count * 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                var cheapest = Comparison.CheapestQuotes(quotes);
                var row = head + 2;
                foreach (var p in patterns)
                {
                    ws.Cell(row, 1).Value = PatternText(p);
                    for (var i = 0; i < suppliers.Count; i++)
                    {
                        var col = 2 + i * 2;
                        var q = quotes.FirstOrDefault(x => x.SupplierId == suppliers[i].SupplierId && x.PatternId == p.Id);
                        if (q == null)
                        {
                            continue;
                        }

                        var isBest = q.UnitPrice.HasValue && cheapest.Any(c => c.SupplierId == q.SupplierId && c.PatternId == q.PatternId);
                        if (q.UnitPrice.HasValue)
                        {
                            ws.Cell(row, col).Value = q.UnitPrice.Value;
                            ws.Cell(row, col).Style.NumberFormat.Format = "#,##0.####";
                        }

                        if (q.LeadTimeDays.HasValue)
                        {
                            ws.Cell(row, col + 1).Value = q.LeadTimeDays.Value;
                        }

                        if (isBest)
                        {
                            ws.Range(row, col, row, col + 1).Style.Font.Bold = true;
                            ws.Range(row, col, row, col + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#DDEFD9");
                            // 色が出ない印刷でも分かるよう、最安の調達先名を右の列に文字でも書く。
                            var bestCell = ws.Cell(row, 2 + suppliers.Count * 2);
                            bestCell.Value = bestCell.GetString() + suppliers[i].ShortName + " ";
                        }
                    }

                    row++;
                }

                var bestCol = 2 + suppliers.Count * 2;
                ws.Cell(head, bestCol).Value = "最安の調達先";
                ws.Cell(head, bestCol).Style.Font.Bold = true;
                ws.Cell(head, bestCol).Style.Fill.BackgroundColor = XLColor.FromHtml("#E8EEF1");

                row++;
                AddTextRow(ws, ref row, suppliers, "別費用（型・治具）", s => s.ExtraCost);
                AddTextRow(ws, ref row, suppliers, "緩和条件", s => s.Relaxation);
                AddTextRow(ws, ref row, suppliers, "備考", s => s.Note);
                AddTextRow(ws, ref row, suppliers, "依頼送付日", s => s.SentAt.HasValue ? s.SentAt.Value.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) : "");
                AddTextRow(ws, ref row, suppliers, "回答受領日", s => s.ReceivedAt.HasValue ? s.ReceivedAt.Value.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) : "");

                ws.Column(1).Width = 24;
                for (var c = 2; c <= bestCol; c++)
                {
                    ws.Column(c).Width = c == bestCol ? 18 : (c % 2 == 0 ? 16 : 14);
                }

                ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
                ws.PageSetup.FitToPages(1, 0);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                wb.SaveAs(path);
            }
        }

        private static void AddTextRow(IXLWorksheet ws, ref int row, IReadOnlyList<AnkenSupplier> suppliers, string label, Func<AnkenSupplier, string> get)
        {
            ws.Cell(row, 1).Value = label;
            ws.Cell(row, 1).Style.Font.Bold = true;
            for (var i = 0; i < suppliers.Count; i++)
            {
                var col = 2 + i * 2;
                ws.Cell(row, col).Value = get(suppliers[i]);
                ws.Range(row, col, row, col + 1).Merge();
                ws.Cell(row, col).Style.Alignment.WrapText = true;
                ws.Cell(row, col).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
            }

            row++;
        }

        /// <summary>ホームの一覧（今の絞り込み結果）を、1行1案件で書く。</summary>
        public static void WriteAnkenList(string path, IEnumerable<HomeRow> rows)
        {
            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("案件一覧");
                var heads = new[] { "状態", "回答期限", "得意先・種別", "依頼日", "品番", "品名", "備考", "回答", "依頼先", "未回答の調達先", "案件フォルダ" };
                for (var i = 0; i < heads.Length; i++)
                {
                    ws.Cell(1, i + 1).Value = heads[i];
                }

                ws.Range(1, 1, 1, heads.Length).Style.Font.Bold = true;
                ws.Range(1, 1, 1, heads.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#E8EEF1");

                var r = 2;
                foreach (var row in rows)
                {
                    var a = row.Anken;
                    ws.Cell(r, 1).Value = row.StatusText;
                    ws.Cell(r, 2).Value = a.ReplyDueDate.Date;
                    ws.Cell(r, 2).Style.DateFormat.Format = "yyyy/MM/dd";
                    ws.Cell(r, 3).Value = a.ClientName;
                    ws.Cell(r, 4).Value = a.RequestDate.Date;
                    ws.Cell(r, 4).Style.DateFormat.Format = "yyyy/MM/dd";
                    ws.Cell(r, 5).Value = a.PartNumber;
                    ws.Cell(r, 6).Value = a.PartName;
                    ws.Cell(r, 7).Value = a.Note;
                    ws.Cell(r, 8).Value = row.Answered;
                    ws.Cell(r, 9).Value = row.Total;
                    ws.Cell(r, 10).Value = string.Join("、", row.PendingNames);
                    ws.Cell(r, 11).Value = a.FolderPath;
                    r++;
                }

                ws.SheetView.FreezeRows(1);
                ws.Columns().AdjustToContents(1, Math.Min(r, 200));
                if (r > 2)
                {
                    ws.Range(1, 1, r - 1, heads.Length).SetAutoFilter();
                }

                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                wb.SaveAs(path);
            }
        }
    }
}
