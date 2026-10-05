using System;
using System.IO;
using AnkenDesk.Core;
using Xunit;

namespace AnkenDesk.Core.Tests
{
    public class ItemDefaultsTests
    {
        [Fact]
        public void 既定値はテンプレートの文章で始まり設定で変えられる()
        {
            using (var dir = new TempDir())
            using (var db = new AnkenDb(Path.Combine(dir.Path, "a.db")))
            {
                var before = db.GetItemDefaults();
                Assert.Equal("検査成績書、ミルシート", before["帳票"]);
                Assert.Equal("", before["納場"]);

                db.SetItemDefault("納場", "ダミー納場");
                db.SetItemDefault("帳票", "");

                var after = db.GetItemDefaults();
                Assert.Equal("ダミー納場", after["納場"]);
                Assert.Equal("", after["帳票"]);
                Assert.Equal("新規", after["履歴"]);
            }
        }

        [Fact]
        public void 既定値をテンプレートの文章に戻せる()
        {
            using (var dir = new TempDir())
            using (var db = new AnkenDb(Path.Combine(dir.Path, "a.db")))
            {
                db.SetItemDefault("帳票", "別の帳票");

                db.ResetItemDefaults();

                Assert.Equal("検査成績書、ミルシート", db.GetItemDefaults()["帳票"]);
            }
        }

        [Fact]
        public void すべての項目名に既定値がある()
        {
            using (var dir = new TempDir())
            using (var db = new AnkenDb(Path.Combine(dir.Path, "a.db")))
            {
                var d = db.GetItemDefaults();

                foreach (var name in QuoteRequestItems.AllNames)
                {
                    Assert.True(d.ContainsKey(name), name);
                }

                Assert.Equal(14, d.Count);
            }
        }

        [Fact]
        public void 登録済みの案件を依頼書の入力として読み出せる()
        {
            using (var dir = new TempDir())
            using (var db = new AnkenDb(Path.Combine(dir.Path, "a.db")))
            {
                var client = db.AddClient("得意先A");
                var input = new AnkenInput { ClientId = client.Id, RequestDate = new DateTime(2026, 10, 5), PartNumber = "P1", PartName = "品名", Note = "Lot5", ReplyDueDate = new DateTime(2026, 10, 12) };
                input.Quantities.Add(new QuantityPattern { Kind = "試作", Quantity = 3, Unit = "個/Lot" });
                input.Items["材質"] = "ダミー材";
                var rec = db.InsertAnken(input, "x");

                var loaded = db.LoadAnkenInput(rec.Id);

                Assert.Equal("P1", loaded.PartNumber);
                Assert.Equal("品名", loaded.PartName);
                Assert.Equal(new DateTime(2026, 10, 12), loaded.ReplyDueDate);
                Assert.Equal("個/Lot", Assert.Single(loaded.Quantities).Unit);
                Assert.Equal("ダミー材", loaded.Items["材質"]);
            }
        }

        [Fact]
        public void 案件の品名と期日と項目を更新できる()
        {
            using (var dir = new TempDir())
            using (var db = new AnkenDb(Path.Combine(dir.Path, "a.db")))
            {
                var client = db.AddClient("得意先A");
                var input = new AnkenInput { ClientId = client.Id, RequestDate = new DateTime(2026, 10, 5), PartNumber = "P1", PartName = "旧品名", ReplyDueDate = new DateTime(2026, 10, 12) };
                input.Items["材質"] = "旧材";
                input.Items["処理"] = "旧処理";
                var rec = db.InsertAnken(input, "x");

                db.UpdateAnkenDetails(rec.Id, " 新品名 ", new DateTime(2026, 10, 20), new System.Collections.Generic.Dictionary<string, string>
                {
                    { "材質", "新材" }, { "処理", "" }, { "納場", "新納場" },
                });

                var loaded = db.LoadAnkenInput(rec.Id);
                Assert.Equal("新品名", loaded.PartName);
                Assert.Equal(new DateTime(2026, 10, 20), loaded.ReplyDueDate);
                Assert.Equal("新材", loaded.Items["材質"]);
                Assert.False(loaded.Items.ContainsKey("処理"));
                Assert.Equal("新納場", loaded.Items["納場"]);
            }
        }

        [Fact]
        public void 存在しない案件は更新も読み出しもできない()
        {
            using (var dir = new TempDir())
            using (var db = new AnkenDb(Path.Combine(dir.Path, "a.db")))
            {
                Assert.Throws<InvalidOperationException>(() => db.LoadAnkenInput(999));
                Assert.Throws<InvalidOperationException>(() => db.UpdateAnkenDetails(999, "", DateTime.Today, new System.Collections.Generic.Dictionary<string, string>()));
            }
        }
    }
}
