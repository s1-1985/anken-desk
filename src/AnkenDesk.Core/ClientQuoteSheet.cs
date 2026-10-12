using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ClosedXML.Excel;

namespace AnkenDesk.Core
{
    /// <summary>客先への見積書の明細1行（数量ごと）。</summary>
    public sealed class ClientQuoteLine
    {
        /// <summary>数量の表記（例: 「量産 3,000個/Lot」）。</summary>
        public string Pattern { get; set; } = "";
        public decimal UnitPrice { get; set; }

        /// <summary>金額（単価×数量）。数量が個数でない（「/年」など）ときは null（空欄）。</summary>
        public decimal? Amount { get; set; }
    }

    /// <summary>客先への見積書に載せる内容。</summary>
    public sealed class ClientQuoteInput
    {
        public AnkenRecord Anken { get; set; } = new AnkenRecord();

        /// <summary>宛名（「御中」は付けて出す）。初期値は得意先・種別の名前。</summary>
        public string ToName { get; set; } = "";
        public DateTime QuoteDate { get; set; }
        public DateTime? ValidUntil { get; set; }

        /// <summary>納期（文章。例: 「受注後 30日」）。</summary>
        public string Delivery { get; set; } = "";
        public string Remarks { get; set; } = "";
        public string CompanyName { get; set; } = "";
        public string PersonName { get; set; } = "";
        public string Contact { get; set; } = "";
        public List<ClientQuoteLine> Lines { get; } = new List<ClientQuoteLine>();
    }

    /// <summary>
    /// 客先への見積書（Excel）を作る。提出単価（「提出単価と粗利」で入れたもの）から。
    /// 汎用の書式（客先ごとの書式への対応はしていない）【仮置き】。仕入の情報（調達先名・仕入単価・別費用）は載せない。
    /// </summary>
    public static class ClientQuoteSheet
    {
        /// <summary>
        /// 提出単価が入っている数量から、明細を作る。金額は、数量が個数のとき（単位が「個」で始まり、「/年」ではない）だけ
        /// 単価×数量で出す【仮置き。「個/Lot」の単価が1個あたりの前提】。年間見込数は、見積の数量ではないので出さない。
        /// </summary>
        public static IReadOnlyList<ClientQuoteLine> BuildLines(IEnumerable<QuantityPattern> patterns, IEnumerable<ClientPrice> prices)
        {
            var list = new List<ClientQuoteLine>();
            var byPattern = prices.Where(p => p.SellingPrice.HasValue).ToDictionary(p => p.PatternId, p => p.SellingPrice!.Value);
            foreach (var p in patterns)
            {
                decimal price;
                if (p.Kind == "年間見込数" || !byPattern.TryGetValue(p.Id, out price))
                {
                    continue;
                }

                var perPiece = p.Unit.StartsWith("個", StringComparison.Ordinal) && !p.Unit.Contains("/年");
                list.Add(new ClientQuoteLine
                {
                    Pattern = ExcelExports.PatternText(p),
                    UnitPrice = price,
                    Amount = perPiece ? Margin.Round(price * p.Quantity) : (decimal?)null,
                });
            }

            return list;
        }

        /// <summary>納期の初期値: 選んだ調達先のリードタイムのうち、いちばん長いもの。「受注後 N日」。無ければ空。</summary>
        public static string DefaultDelivery(IEnumerable<ClientPrice> prices, IEnumerable<Quote> quotes)
        {
            var days = new List<int>();
            foreach (var c in prices.Where(c => c.AdoptedSupplierId.HasValue && c.SellingPrice.HasValue))
            {
                var q = quotes.FirstOrDefault(x => x.SupplierId == c.AdoptedSupplierId!.Value && x.PatternId == c.PatternId && x.LeadTimeDays.HasValue);
                if (q != null)
                {
                    days.Add(q.LeadTimeDays!.Value);
                }
            }

            return days.Count == 0 ? "" : "受注後 " + days.Max() + "日";
        }

        public static string SaveToAnkenFolder(string ankenFullPath, ClientQuoteInput input)
        {
            if (!Directory.Exists(ankenFullPath))
            {
                throw new DirectoryNotFoundException("案件フォルダが見つかりません: " + ankenFullPath);
            }

            var dir = Path.Combine(ankenFullPath, FolderNames.Subfolders[6]);
            Directory.CreateDirectory(dir);
            var name = input.QuoteDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + FolderNames.Separator
                + Sanitizer.FileName(input.Anken.PartNumber) + FolderNames.Separator + "見積書.xlsx";
            var path = QuoteFiles.UniquePath(dir, name);
            Save(path, input);
            return path;
        }

        public static void Save(string path, ClientQuoteInput input)
        {
            if (input.Lines.Count == 0)
            {
                throw new InvalidOperationException("提出単価が入っている数量がありません。先に「提出単価と粗利」で入れてください。");
            }

            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("見積書");
                ws.Style.Font.FontName = "BIZ UDPGothic";
                ws.Column(1).Width = 6;
                ws.Column(2).Width = 34;
                ws.Column(3).Width = 16;
                ws.Column(4).Width = 18;

                ws.Range(1, 1, 1, 4).Merge();
                ws.Cell(1, 1).Value = "御　見　積　書";
                ws.Cell(1, 1).Style.Font.FontSize = 20;
                ws.Cell(1, 1).Style.Font.Bold = true;
                ws.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                ws.Cell(3, 1).Value = (string.IsNullOrWhiteSpace(input.ToName) ? input.Anken.ClientName : input.ToName) + "　御中";
                ws.Cell(3, 1).Style.Font.FontSize = 13;
                ws.Cell(3, 1).Style.Font.Bold = true;
                ws.Cell(3, 4).Value = "見積日　" + input.QuoteDate.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
                ws.Cell(3, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                var row = 5;
                ws.Cell(row, 1).Value = "件名";
                ws.Cell(row, 2).Value = input.Anken.PartNumber + (string.IsNullOrWhiteSpace(input.Anken.PartName) ? "" : "　" + input.Anken.PartName) + " のお見積り";
                row++;
                ws.Cell(row, 1).Value = "有効期限";
                ws.Cell(row, 2).Value = input.ValidUntil.HasValue ? input.ValidUntil.Value.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) : "";
                row++;
                ws.Cell(row, 1).Value = "納期";
                ws.Cell(row, 2).Value = input.Delivery;
                ws.Range(5, 1, row, 1).Style.Font.Bold = true;

                row += 2;
                var head = row;
                ws.Cell(head, 1).Value = "No.";
                ws.Cell(head, 2).Value = "数量";
                ws.Cell(head, 3).Value = "単価（円）";
                ws.Cell(head, 4).Value = "金額（円）";
                ws.Range(head, 1, head, 4).Style.Font.Bold = true;
                ws.Range(head, 1, head, 4).Style.Fill.BackgroundColor = XLColor.FromHtml("#E8EEF1");
                ws.Range(head, 1, head, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                decimal total = 0;
                var hasAmount = false;
                var n = 1;
                foreach (var l in input.Lines)
                {
                    row++;
                    ws.Cell(row, 1).Value = n++;
                    ws.Cell(row, 2).Value = l.Pattern;
                    ws.Cell(row, 3).Value = l.UnitPrice;
                    ws.Cell(row, 3).Style.NumberFormat.Format = "#,##0.##";
                    if (l.Amount.HasValue)
                    {
                        ws.Cell(row, 4).Value = l.Amount.Value;
                        ws.Cell(row, 4).Style.NumberFormat.Format = "#,##0.##";
                        total += l.Amount.Value;
                        hasAmount = true;
                    }
                }

                if (hasAmount)
                {
                    row++;
                    ws.Cell(row, 2).Value = "合計（金額のある行）";
                    ws.Cell(row, 4).Value = total;
                    ws.Cell(row, 4).Style.NumberFormat.Format = "#,##0.##";
                    ws.Range(row, 1, row, 4).Style.Font.Bold = true;
                }

                var table = ws.Range(head, 1, row, 4);
                table.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                table.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

                row += 2;
                if (!string.IsNullOrWhiteSpace(input.Remarks))
                {
                    ws.Cell(row, 1).Value = "備考";
                    ws.Cell(row, 1).Style.Font.Bold = true;
                    foreach (var line in input.Remarks.Replace("\r\n", "\n").Split('\n'))
                    {
                        row++;
                        ws.Cell(row, 2).Value = line;
                    }

                    row += 2;
                }

                foreach (var line in new[] { input.CompanyName, input.PersonName, input.Contact }.Where(x => !string.IsNullOrWhiteSpace(x)))
                {
                    ws.Cell(row, 4).Value = line;
                    ws.Cell(row, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                    row++;
                }

                ws.PageSetup.PageOrientation = XLPageOrientation.Portrait;
                ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
                ws.PageSetup.FitToPages(1, 0);
                wb.SaveAs(path);
            }
        }
    }
}
