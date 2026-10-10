using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using ClosedXML.Excel;

namespace AnkenDesk.App
{
    /// <summary>サムネイル画像（と、一言の説明）。</summary>
    internal sealed class ThumbnailResult
    {
        public Bitmap Image { get; set; } = new Bitmap(1, 1);

        /// <summary>タイルの下に添える短い説明（「3ページ」「簡易表示」など）。</summary>
        public string Note { get; set; } = "";
    }

    /// <summary>
    /// ファイルの中身を、小さな画像にする。PDFはWindows標準の描画で1ページ目、画像は縮小、
    /// Excel（.xlsx/.xlsm）は外部ソフトを使わずセルの文字と罫線を描いた<b>簡易表示</b>（見た目はExcelと同じではない）。
    /// それ以外、または失敗したときは、拡張子を書いた枠を返す（例外は外に出さない）。
    /// </summary>
    internal static class ThumbnailMaker
    {
        private const int MaxRows = 60;
        private const int MaxCols = 16;

        public static ThumbnailResult Make(string path, int width, int height)
        {
            var ext = (Path.GetExtension(path) ?? "").ToLowerInvariant();
            try
            {
                if (ext == ".pdf")
                {
                    return FromPdf(path, width, height);
                }

                if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".gif" || ext == ".bmp")
                {
                    return FromImage(path, width, height);
                }

                if (ext == ".xlsx" || ext == ".xlsm")
                {
                    return FromExcel(path, width, height);
                }
            }
            catch (Exception ex)
            {
                return Placeholder(ext, width, height, "表示できません: " + Short(ex.Message));
            }

            return Placeholder(ext, width, height, "プレビューなし（ダブルクリックで開く）");
        }

        private static ThumbnailResult FromPdf(string path, int width, int height)
        {
            using (var r = new PdfRenderer(path))
            {
                if (!r.Load())
                {
                    return Placeholder(".pdf", width, height, "開けません: " + Short(r.LoadError ?? ""));
                }

                // 縦長のページでも枠に収まる幅で描画する（描画は幅指定のみ）。
                var img = r.RenderPage(0, width * 2);
                if (img == null)
                {
                    return Placeholder(".pdf", width, height, "描画できません");
                }

                using (img)
                {
                    return new ThumbnailResult { Image = Fit(img, width, height), Note = r.PageCount + "ページ" };
                }
            }
        }

        private static ThumbnailResult FromImage(string path, int width, int height)
        {
            using (var ms = new MemoryStream(File.ReadAllBytes(path)))
            using (var img = Image.FromStream(ms))
            {
                return new ThumbnailResult { Image = Fit(img, width, height), Note = img.Width + "×" + img.Height };
            }
        }

        private static ThumbnailResult FromExcel(string path, int width, int height)
        {
            // Excelで開いていても読めるよう、共有を許可して一度メモリに読む。
            byte[] bytes;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var ms = new MemoryStream())
            {
                fs.CopyTo(ms);
                bytes = ms.ToArray();
            }

            using (var wb = new XLWorkbook(new MemoryStream(bytes)))
            {
                var ws = wb.Worksheets.FirstOrDefault(w => w.Visibility == XLWorksheetVisibility.Visible) ?? wb.Worksheet(1);
                IXLRange? range = null;
                if (ws.PageSetup.PrintAreas.Any())
                {
                    range = ws.PageSetup.PrintAreas.First();
                }

                if (range == null)
                {
                    range = ws.RangeUsed();
                }

                if (range == null)
                {
                    return Placeholder(".xlsx", width, height, "空のシートです");
                }

                var bmp = DrawSheet(ws, range, width, height);
                return new ThumbnailResult { Image = bmp, Note = "簡易表示　" + ws.Name };
            }
        }

        // セルの値と罫線だけを描く簡易表示。列幅・行の高さは元のシートに合わせ、全体を枠に収める。
        private static Bitmap DrawSheet(IXLWorksheet ws, IXLRange range, int width, int height)
        {
            var firstRow = range.FirstRow().RowNumber();
            var firstCol = range.FirstColumn().ColumnNumber();
            var lastRow = Math.Min(range.LastRow().RowNumber(), firstRow + MaxRows - 1);
            var lastCol = Math.Min(range.LastColumn().ColumnNumber(), firstCol + MaxCols - 1);

            var colW = new List<double>();
            for (var c = firstCol; c <= lastCol; c++)
            {
                var col = ws.Column(c);
                colW.Add(col.IsHidden ? 0 : Math.Max(4, col.Width * 7 + 5));
            }

            var rowH = new List<double>();
            for (var r = firstRow; r <= lastRow; r++)
            {
                var row = ws.Row(r);
                rowH.Add(row.IsHidden ? 0 : Math.Max(8, row.Height * 1.33));
            }

            var totalW = colW.Sum();
            var totalH = rowH.Sum();
            if (totalW <= 0 || totalH <= 0)
            {
                return Placeholder(".xlsx", width, height, "表示できる範囲がありません").Image;
            }

            var scale = Math.Min((width - 8) / totalW, (height - 8) / totalH);
            var bmp = new Bitmap(width, height);
            using (var g = Graphics.FromImage(bmp))
            using (var gridPen = new Pen(Color.FromArgb(200, 200, 200)))
            using (var textBrush = new SolidBrush(Color.FromArgb(30, 36, 40)))
            {
                g.Clear(Color.White);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                var fontSize = (float)Math.Max(4.5, 11 * scale);
                using (var font = new Font("BIZ UDPGothic", fontSize))
                {
                    double y = 4;
                    for (var ri = 0; ri < rowH.Count; ri++)
                    {
                        double x = 4;
                        var h = rowH[ri] * scale;
                        for (var ci = 0; ci < colW.Count; ci++)
                        {
                            var w = colW[ci] * scale;
                            if (w > 0 && h > 0)
                            {
                                var rect = new RectangleF((float)x, (float)y, (float)w, (float)h);
                                var cell = ws.Cell(firstRow + ri, firstCol + ci);
                                var fill = CellFill(cell);
                                if (fill.HasValue)
                                {
                                    using (var b = new SolidBrush(fill.Value))
                                    {
                                        g.FillRectangle(b, rect);
                                    }
                                }

                                g.DrawRectangle(gridPen, rect.X, rect.Y, rect.Width, rect.Height);
                                var text = CellText(cell);
                                if (text.Length > 0)
                                {
                                    // 右隣が空のセルなら、Excelと同じく文字をはみ出して見せる。
                                    var span = w;
                                    for (var k = ci + 1; k < colW.Count && CellText(ws.Cell(firstRow + ri, firstCol + k)).Length == 0; k++)
                                    {
                                        span += colW[k] * scale;
                                    }

                                    var clip = new RectangleF(rect.X, rect.Y, (float)span, rect.Height);
                                    g.SetClip(clip);
                                    g.DrawString(text, font, textBrush, new RectangleF(rect.X + 1, rect.Y, (float)span, rect.Height));
                                    g.ResetClip();
                                }
                            }

                            x += w;
                        }

                        y += h;
                    }
                }
            }

            return bmp;
        }

        private static string CellText(IXLCell cell)
        {
            try
            {
                var s = cell.GetFormattedString();
                return (s ?? "").Replace("\r", " ").Replace("\n", " ");
            }
            catch (Exception)
            {
                return "";
            }
        }

        private static Color? CellFill(IXLCell cell)
        {
            try
            {
                var f = cell.Style.Fill;
                if (f.PatternType == XLFillPatternValues.Solid && f.BackgroundColor.ColorType == XLColorType.Color)
                {
                    var c = f.BackgroundColor.Color;
                    if (c.A > 0 && !(c.R == 255 && c.G == 255 && c.B == 255))
                    {
                        return Color.FromArgb(c.R, c.G, c.B);
                    }
                }
            }
            catch (Exception)
            {
            }

            return null;
        }

        // 元の画像を、縦横比を保って枠に収め、白地に置く。
        private static Bitmap Fit(Image src, int width, int height)
        {
            var bmp = new Bitmap(width, height);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                var s = Math.Min((double)width / src.Width, (double)height / src.Height);
                var w = Math.Max(1, (int)(src.Width * s));
                var h = Math.Max(1, (int)(src.Height * s));
                g.DrawImage(src, (width - w) / 2, (height - h) / 2, w, h);
            }

            return bmp;
        }

        public static ThumbnailResult Placeholder(string ext, int width, int height, string note)
        {
            var bmp = new Bitmap(width, height);
            using (var g = Graphics.FromImage(bmp))
            using (var big = new Font("BIZ UDPGothic", Math.Max(10, width / 8f), FontStyle.Bold))
            using (var small = new Font("BIZ UDPGothic", Math.Max(8, width / 20f)))
            using (var brush = new SolidBrush(Color.FromArgb(90, 96, 100)))
            using (var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.Clear(Color.FromArgb(238, 239, 235));
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.DrawString(string.IsNullOrEmpty(ext) ? "ファイル" : ext.TrimStart('.').ToUpperInvariant(), big, brush,
                    new RectangleF(0, height * 0.25f, width, height * 0.25f), fmt);
                g.DrawString(note, small, brush, new RectangleF(8, height * 0.55f, width - 16, height * 0.35f), fmt);
            }

            return new ThumbnailResult { Image = bmp, Note = "" };
        }

        private static string Short(string s)
        {
            s = (s ?? "").Replace("\r", " ").Replace("\n", " ");
            return s.Length > 40 ? s.Substring(0, 40) + "…" : s;
        }
    }
}
