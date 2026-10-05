using System;
using System.IO;
using AnkenDesk.Core;
using Xunit;

namespace AnkenDesk.Core.Tests
{
    public class AnkenDbTests
    {
        private static AnkenInput SampleInput(long clientId)
        {
            var input = new AnkenInput
            {
                ClientId = clientId,
                RequestDate = new DateTime(2026, 10, 5),
                PartNumber = "TEST-001",
                PartName = "ダミー部品",
                Note = "Lot5",
                ReplyDueDate = new DateTime(2026, 10, 12),
            };
            input.Quantities.Add(new QuantityPattern { Kind = "試作", Quantity = 3, Unit = "個" });
            input.Quantities.Add(new QuantityPattern { Kind = "試作", Quantity = 10, Unit = "個" });
            input.Items["材質"] = "ダミー材";
            input.Items["処理"] = "";
            return input;
        }

        [Fact]
        public void 新しいDBを作ると空で開ける()
        {
            using (var dir = new TempDir())
            using (var db = new AnkenDb(Path.Combine(dir.Path, "sub", "a.db")))
            {
                Assert.Empty(db.ListClients());
                Assert.Empty(db.ListAnkens());
                Assert.Null(db.GetSetting("workspace_root"));
            }
        }

        [Fact]
        public void 開き直しても内容が残る()
        {
            using (var dir = new TempDir())
            {
                var path = Path.Combine(dir.Path, "a.db");
                using (var db = new AnkenDb(path))
                {
                    db.AddClient("得意先A\u3000試作");
                    db.SetSetting("workspace_root", @"C:\x");
                }

                using (var db = new AnkenDb(path))
                {
                    Assert.Equal("得意先A\u3000試作", Assert.Single(db.ListClients()).Name);
                    Assert.Equal(@"C:\x", db.GetSetting("workspace_root"));
                }
            }
        }

        [Fact]
        public void 設定は上書きできる()
        {
            using (var dir = new TempDir())
            using (var db = new AnkenDb(Path.Combine(dir.Path, "a.db")))
            {
                db.SetSetting("k", "1");
                db.SetSetting("k", "2");

                Assert.Equal("2", db.GetSetting("k"));
            }
        }

        [Fact]
        public void 得意先は任意の名前で追加と名前変更ができる()
        {
            using (var dir = new TempDir())
            using (var db = new AnkenDb(Path.Combine(dir.Path, "a.db")))
            {
                var c = db.AddClient("  任意の名前  ");
                Assert.Equal("任意の名前", c.Name);

                db.RenameClient(c.Id, "別の名前");

                Assert.Equal("別の名前", db.GetClient(c.Id)!.Name);
            }
        }

        [Fact]
        public void 得意先の名前が重複か不正なら拒否する()
        {
            using (var dir = new TempDir())
            using (var db = new AnkenDb(Path.Combine(dir.Path, "a.db")))
            {
                db.AddClient("A");

                Assert.Throws<InvalidOperationException>(() => db.AddClient("A"));
                Assert.Throws<ArgumentException>(() => db.AddClient(""));
                Assert.Throws<ArgumentException>(() => db.AddClient("a/b"));
            }
        }

        [Fact]
        public void 案件を保存して読み戻せる()
        {
            using (var dir = new TempDir())
            using (var db = new AnkenDb(Path.Combine(dir.Path, "a.db")))
            {
                var c = db.AddClient("A");

                var rec = db.InsertAnken(SampleInput(c.Id), @"A\20261005　TEST-001　Lot5");

                var list = db.ListAnkens();
                var r = Assert.Single(list);
                Assert.Equal(rec.Id, r.Id);
                Assert.Equal("A", r.ClientName);
                Assert.Equal(new DateTime(2026, 10, 5), r.RequestDate);
                Assert.Equal(new DateTime(2026, 10, 12), r.ReplyDueDate);
                Assert.Equal("TEST-001", r.PartNumber);
                Assert.Equal("ダミー部品", r.PartName);
                Assert.Equal("Lot5", r.Note);

                var q = db.ListQuantities(rec.Id);
                Assert.Equal(2, q.Count);
                Assert.Equal(3m, q[0].Quantity);
                Assert.Equal("個", q[1].Unit);

                var items = db.GetItems(rec.Id);
                Assert.Equal("ダミー材", items["材質"]);
                Assert.False(items.ContainsKey("処理"));
            }
        }

        [Fact]
        public void 同じフォルダの案件は二重に登録できない()
        {
            using (var dir = new TempDir())
            using (var db = new AnkenDb(Path.Combine(dir.Path, "a.db")))
            {
                var c = db.AddClient("A");
                db.InsertAnken(SampleInput(c.Id), "A\\x");

                Assert.Throws<InvalidOperationException>(() => db.InsertAnken(SampleInput(c.Id), "A\\x"));
                Assert.Single(db.ListAnkens());
            }
        }

        [Fact]
        public void 案件がある得意先は削除できない()
        {
            using (var dir = new TempDir())
            using (var db = new AnkenDb(Path.Combine(dir.Path, "a.db")))
            {
                var used = db.AddClient("使用中");
                var unused = db.AddClient("未使用");
                db.InsertAnken(SampleInput(used.Id), "x");

                Assert.Throws<InvalidOperationException>(() => db.DeleteClient(used.Id));
                db.DeleteClient(unused.Id);

                Assert.Equal("使用中", Assert.Single(db.ListClients()).Name);
            }
        }
    }
}
