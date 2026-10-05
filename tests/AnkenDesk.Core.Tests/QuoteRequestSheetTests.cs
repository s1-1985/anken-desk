using System;
using System.IO;
using System.Linq;
using AnkenDesk.Core;
using ClosedXML.Excel;
using Xunit;

namespace AnkenDesk.Core.Tests
{
    public class QuoteRequestSheetTests
    {
        private static AnkenInput Input()
        {
            var input = new AnkenInput
            {
                PartNumber = "TEST-001",
                PartName = "ダミー部品",
                ReplyDueDate = new DateTime(2026, 10, 12),
            };
            input.Quantities.Add(new QuantityPattern { Kind = "試作", Quantity = 3, Unit = "個/Lot" });
            input.Quantities.Add(new QuantityPattern { Kind = "試作", Quantity = 10, Unit = "個/Lot" });
            input.Quantities.Add(new QuantityPattern { Kind = "量産", Quantity = 3000, Unit = "個/Lot" });
            input.Quantities.Add(new QuantityPattern { Kind = "年間見込数", Quantity = 36000, Unit = "個/年" });
            input.Items["材質"] = "ダミー材";
            input.Items["処理"] = "ダミー処理";
            input.Items["荷姿（一次）"] = "ダミー一次";
            input.Items["検査内容"] = "ダミー検査\n2行目";
            input.Items["納場"] = "ダミー納場";
            return input;
        }

        private static IXLWorksheet Reopen(string path, out XLWorkbook wb)
        {
            wb = new XLWorkbook(path);
            return wb.Worksheet(1);
        }

        [Fact]
        public void ファイル名は日付と全角スペースと見積依頼()
        {
            Assert.Equal("20261005　見積依頼.xlsx", QuoteRequestSheet.FileName(new DateTime(2026, 10, 5)));
        }

        [Fact]
        public void 品番と品名と項目と回答希望期日が入る()
        {
            using (var dir = new TempDir())
            {
                var path = Path.Combine(dir.Path, "a.xlsx");

                QuoteRequestSheet.Write(path, Input());

                XLWorkbook wb;
                var ws = Reopen(path, out wb);
                using (wb)
                {
                    Assert.Equal("見積依頼", ws.Name);
                    Assert.Equal("TEST-001", ws.Cell("C6").GetString());
                    Assert.Equal("ダミー部品", ws.Cell("C7").GetString());
                    Assert.Equal("ダミー材", ws.Cell("C20").GetString());
                    Assert.Equal("ダミー処理", ws.Cell("C22").GetString());
                    Assert.Equal("ダミー一次", ws.Cell("C23").GetString());
                    Assert.Equal("ダミー検査\n2行目", ws.Cell("C27").GetString());
                    Assert.Equal("ダミー納場", ws.Cell("C34").GetString());
                    Assert.Equal("2026/10/12（月）", ws.Cell("C37").GetString());
                }
            }
        }

        [Fact]
        public void 数量は種別ごとの行に入り使わない行は空になる()
        {
            using (var dir = new TempDir())
            {
                var path = Path.Combine(dir.Path, "a.xlsx");

                QuoteRequestSheet.Write(path, Input());

                XLWorkbook wb;
                var ws = Reopen(path, out wb);
                using (wb)
                {
                    Assert.Equal("試作", ws.Cell("C8").GetString());
                    Assert.Equal(3d, ws.Cell("D8").GetDouble());
                    Assert.Equal("個/Lot", ws.Cell("E8").GetString());
                    Assert.Equal(10d, ws.Cell("D9").GetDouble());

                    Assert.True(ws.Cell("C10").IsEmpty());
                    Assert.True(ws.Cell("E10").IsEmpty());
                    Assert.True(ws.Cell("C11").IsEmpty());

                    Assert.Equal("量産", ws.Cell("C12").GetString());
                    Assert.Equal(3000d, ws.Cell("D12").GetDouble());
                    Assert.True(ws.Cell("C13").IsEmpty());

                    Assert.Equal("年間見込数", ws.Cell("C18").GetString());
                    Assert.Equal(36000d, ws.Cell("D18").GetDouble());
                    Assert.Equal("個/年", ws.Cell("E18").GetString());
                }
            }
        }

        [Fact]
        public void 入力の無い項目は空にする()
        {
            using (var dir = new TempDir())
            {
                var path = Path.Combine(dir.Path, "a.xlsx");

                QuoteRequestSheet.Write(path, Input());

                XLWorkbook wb;
                var ws = Reopen(path, out wb);
                using (wb)
                {
                    // テンプレートにあった既定の文章（帳票・納期・履歴）も、入力が無ければ残さない。
                    Assert.True(ws.Cell("C26").IsEmpty());
                    Assert.True(ws.Cell("C29").IsEmpty());
                    Assert.True(ws.Cell("C35").IsEmpty());
                    Assert.True(ws.Cell("C36").IsEmpty());
                }
            }
        }

        [Fact]
        public void 品番の列は1つだけで補助の値も残らない()
        {
            using (var dir = new TempDir())
            {
                var path = Path.Combine(dir.Path, "a.xlsx");

                QuoteRequestSheet.Write(path, Input());

                XLWorkbook wb;
                var ws = Reopen(path, out wb);
                using (wb)
                {
                    Assert.True(ws.LastColumnUsed().ColumnNumber() <= 5);
                    Assert.True(ws.Cell("B1").IsEmpty());
                    Assert.True(ws.Cell("B2").IsEmpty());
                    Assert.True(ws.Cell("C4").IsEmpty());
                    Assert.Empty(ws.DataValidations);
                }
            }
        }

        [Fact]
        public void 見出しと注意書きは残る()
        {
            using (var dir = new TempDir())
            {
                var path = Path.Combine(dir.Path, "a.xlsx");

                QuoteRequestSheet.Write(path, Input());

                XLWorkbook wb;
                var ws = Reopen(path, out wb);
                using (wb)
                {
                    Assert.Equal("品番", ws.Cell("B6").GetString());
                    Assert.Equal("数量", ws.Cell("B8").GetString());
                    Assert.Contains("各パターン毎にお願いします", ws.Cell("C19").GetString());
                    Assert.Contains("材料自達", ws.Cell("C21").GetString());
                    Assert.Equal("希望回答期日：", ws.Cell("B37").GetString());
                    Assert.StartsWith("※図面管理値等に緩和要望がある場合は", ws.Cell("B38").GetString());
                    Assert.Contains("概算見積", ws.Cell("B38").GetString());
                    Assert.True(ws.Cell("B38").IsMerged());
                }
            }
        }

        [Fact]
        public void 印刷は縦向きのA4で品番の行から注意書きまで()
        {
            using (var dir = new TempDir())
            {
                var path = Path.Combine(dir.Path, "a.xlsx");

                QuoteRequestSheet.Write(path, Input());

                XLWorkbook wb;
                var ws = Reopen(path, out wb);
                using (wb)
                {
                    Assert.Equal(XLPageOrientation.Portrait, ws.PageSetup.PageOrientation);
                    Assert.Equal(XLPaperSize.A4Paper, ws.PageSetup.PaperSize);
                    var area = ws.PageSetup.PrintAreas.Single().RangeAddress.ToStringRelative();
                    Assert.Equal("B6:E38", area);
                }
            }
        }

        [Fact]
        public void 数量が枠に入りきらなければ何も書かずに例外()
        {
            using (var dir = new TempDir())
            {
                var path = Path.Combine(dir.Path, "a.xlsx");
                var input = Input();
                for (var i = 0; i < 4; i++)
                {
                    input.Quantities.Add(new QuantityPattern { Kind = "試作", Quantity = 1, Unit = "個/Lot" });
                }

                Assert.Throws<ArgumentException>(() => QuoteRequestSheet.Write(path, input));
                Assert.False(File.Exists(path));
            }
        }

        [Fact]
        public void 載せられない種別なら例外()
        {
            var input = Input();
            input.Quantities.Add(new QuantityPattern { Kind = "その他", Quantity = 1, Unit = "個" });

            var ex = Assert.Throws<ArgumentException>(() => QuoteRequestSheet.Write(Path.Combine(Path.GetTempPath(), "x.xlsx"), input));
            Assert.Contains("その他", ex.Message);
        }

        [Fact]
        public void 単位が空ならテンプレートの単位になる()
        {
            using (var dir = new TempDir())
            {
                var path = Path.Combine(dir.Path, "a.xlsx");
                var input = Input();
                input.Quantities.Clear();
                input.Quantities.Add(new QuantityPattern { Kind = "量産", Quantity = 5, Unit = "" });
                input.Quantities.Add(new QuantityPattern { Kind = "年間見込数", Quantity = 60, Unit = " " });

                QuoteRequestSheet.Write(path, input);

                XLWorkbook wb;
                var ws = Reopen(path, out wb);
                using (wb)
                {
                    Assert.Equal("個/Lot", ws.Cell("E12").GetString());
                    Assert.Equal("個/年", ws.Cell("E18").GetString());
                }
            }
        }

        [Fact]
        public void テンプレートの既定の文章を読める()
        {
            var d = QuoteRequestSheet.TemplateDefaults();

            Assert.Equal("検査成績書、ミルシート", d["帳票"]);
            Assert.Contains("初回・試作は全特性", d["検査内容"]);
            Assert.Equal("新規", d["履歴"]);
            Assert.Equal("", d["納場"]);
            Assert.Equal("", d["材質"]);
        }

        [Fact]
        public void テンプレートに個人名や取引先名は入っていない()
        {
            using (var dir = new TempDir())
            {
                var path = Path.Combine(dir.Path, "a.xlsx");
                QuoteRequestSheet.Write(path, Input());

                var asm = typeof(QuoteRequestSheet).Assembly;
                using (var s = asm.GetManifestResourceStream("AnkenDesk.Core.QuoteRequestTemplate.xlsx"))
                using (var zip = new System.IO.Compression.ZipArchive(s!))
                {
                    foreach (var entry in zip.Entries.Where(e => e.FullName.EndsWith(".xml")))
                    {
                        using (var r = new StreamReader(entry.Open()))
                        {
                            var text = r.ReadToEnd();
                            Assert.DoesNotContain("曽根", text);
                            Assert.DoesNotContain("北関東", text);
                        }
                    }
                }
            }
        }
    }
}
