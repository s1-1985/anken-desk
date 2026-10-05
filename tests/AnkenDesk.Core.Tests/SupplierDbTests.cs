using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;
using AnkenDesk.Core;
using Xunit;

namespace AnkenDesk.Core.Tests
{
    public class SupplierDbTests
    {
        private static (AnkenDb db, AnkenRecord anken, IReadOnlyList<QuantityPattern> patterns) Setup(TempDir dir)
        {
            var db = new AnkenDb(Path.Combine(dir.Path, "a.db"));
            var client = db.AddClient("得意先A");
            var input = new AnkenInput { ClientId = client.Id, RequestDate = new DateTime(2026, 10, 5), PartNumber = "TEST-001", ReplyDueDate = new DateTime(2026, 10, 12) };
            input.Quantities.Add(new QuantityPattern { Kind = "試作", Quantity = 3, Unit = "個" });
            input.Quantities.Add(new QuantityPattern { Kind = "試作", Quantity = 10, Unit = "個" });
            var rec = db.InsertAnken(input, "x");
            return (db, rec, db.ListQuantities(rec.Id));
        }

        [Fact]
        public void 調達先は略称が空なら名前を略称にして登録できる()
        {
            using (var dir = new TempDir())
            using (var db = new AnkenDb(Path.Combine(dir.Path, "a.db")))
            {
                var s = db.AddSupplier("調達先A", "", "a@example.com", "ダミー住所");

                Assert.Equal("調達先A", s.ShortName);
                var got = Assert.Single(db.ListSuppliers());
                Assert.Equal("a@example.com", got.Email);
            }
        }

        [Fact]
        public void 調達先の名前の重複と略称の不正文字は拒否する()
        {
            using (var dir = new TempDir())
            using (var db = new AnkenDb(Path.Combine(dir.Path, "a.db")))
            {
                db.AddSupplier("調達先A", "A", "", "");

                Assert.Throws<InvalidOperationException>(() => db.AddSupplier("調達先A", "B", "", ""));
                Assert.Throws<ArgumentException>(() => db.AddSupplier("", "", "", ""));
                Assert.Throws<ArgumentException>(() => db.AddSupplier("調達先B", "a/b", "", ""));
            }
        }

        [Fact]
        public void 調達先を更新できる()
        {
            using (var dir = new TempDir())
            using (var db = new AnkenDb(Path.Combine(dir.Path, "a.db")))
            {
                var s = db.AddSupplier("調達先A", "A", "", "");
                s.Email = "new@example.com";
                s.ShortName = "略称";

                db.UpdateSupplier(s);

                var got = Assert.Single(db.ListSuppliers());
                Assert.Equal("new@example.com", got.Email);
                Assert.Equal("略称", got.ShortName);
            }
        }

        [Fact]
        public void 数量パターンにはIDが付く()
        {
            using (var dir = new TempDir())
            {
                var (db, _, patterns) = Setup(dir);
                using (db)
                {
                    Assert.Equal(2, patterns.Count);
                    Assert.All(patterns, p => Assert.True(p.Id > 0));
                    Assert.NotEqual(patterns[0].Id, patterns[1].Id);
                }
            }
        }

        [Fact]
        public void 案件に調達先を加えて回答を保存し読み戻せる()
        {
            using (var dir = new TempDir())
            {
                var (db, anken, patterns) = Setup(dir);
                using (db)
                {
                    var s = db.AddSupplier("調達先A", "A", "", "");
                    db.AddSupplierToAnken(anken.Id, s.Id);
                    db.AddSupplierToAnken(anken.Id, s.Id);

                    var answer = db.ListAnkenSuppliers(anken.Id).Single();
                    answer.SentAt = new DateTime(2026, 10, 5);
                    answer.ReceivedAt = new DateTime(2026, 10, 7);
                    answer.ExtraCost = "治具費 30,000円";
                    answer.Relaxation = "公差の一部を緩和";
                    answer.Note = "備考ダミー";
                    db.SaveSupplierAnswer(answer, new[]
                    {
                        new Quote { SupplierId = s.Id, PatternId = patterns[0].Id, UnitPrice = 4850.5m, LeadTimeDays = 14 },
                        new Quote { SupplierId = s.Id, PatternId = patterns[1].Id, UnitPrice = null, LeadTimeDays = null },
                    });

                    var got = db.ListAnkenSuppliers(anken.Id).Single();
                    Assert.Equal(new DateTime(2026, 10, 5), got.SentAt);
                    Assert.Equal(new DateTime(2026, 10, 7), got.ReceivedAt);
                    Assert.True(got.IsAnswered);
                    Assert.Equal("治具費 30,000円", got.ExtraCost);
                    Assert.Equal("公差の一部を緩和", got.Relaxation);

                    var q = Assert.Single(db.ListQuotes(anken.Id));
                    Assert.Equal(patterns[0].Id, q.PatternId);
                    Assert.Equal(4850.5m, q.UnitPrice);
                    Assert.Equal(14, q.LeadTimeDays);
                }
            }
        }

        [Fact]
        public void 回答を保存し直すと置き換わる()
        {
            using (var dir = new TempDir())
            {
                var (db, anken, patterns) = Setup(dir);
                using (db)
                {
                    var s = db.AddSupplier("調達先A", "A", "", "");
                    db.AddSupplierToAnken(anken.Id, s.Id);
                    var answer = db.ListAnkenSuppliers(anken.Id).Single();
                    db.SaveSupplierAnswer(answer, new[] { new Quote { SupplierId = s.Id, PatternId = patterns[0].Id, UnitPrice = 100m, LeadTimeDays = 5 } });

                    db.SaveSupplierAnswer(answer, new[] { new Quote { SupplierId = s.Id, PatternId = patterns[1].Id, UnitPrice = 90m } });

                    var q = Assert.Single(db.ListQuotes(anken.Id));
                    Assert.Equal(patterns[1].Id, q.PatternId);
                    Assert.Null(q.LeadTimeDays);
                }
            }
        }

        [Fact]
        public void 案件から調達先を外すと回答も消える()
        {
            using (var dir = new TempDir())
            {
                var (db, anken, patterns) = Setup(dir);
                using (db)
                {
                    var s = db.AddSupplier("調達先A", "A", "", "");
                    db.AddSupplierToAnken(anken.Id, s.Id);
                    var answer = db.ListAnkenSuppliers(anken.Id).Single();
                    db.SaveSupplierAnswer(answer, new[] { new Quote { SupplierId = s.Id, PatternId = patterns[0].Id, UnitPrice = 100m } });

                    db.RemoveSupplierFromAnken(anken.Id, s.Id);

                    Assert.Empty(db.ListAnkenSuppliers(anken.Id));
                    Assert.Empty(db.ListQuotes(anken.Id));
                }
            }
        }

        [Fact]
        public void 案件で使われている調達先は削除できない()
        {
            using (var dir = new TempDir())
            {
                var (db, anken, _) = Setup(dir);
                using (db)
                {
                    var used = db.AddSupplier("使用中", "使", "", "");
                    var unused = db.AddSupplier("未使用", "未", "", "");
                    db.AddSupplierToAnken(anken.Id, used.Id);

                    Assert.Throws<InvalidOperationException>(() => db.DeleteSupplier(used.Id));
                    db.DeleteSupplier(unused.Id);

                    Assert.Equal("使用中", Assert.Single(db.ListSuppliers()).Name);
                }
            }
        }

        [Fact]
        public void 調達先に登録のない案件へは回答を保存できない()
        {
            using (var dir = new TempDir())
            {
                var (db, anken, _) = Setup(dir);
                using (db)
                {
                    var s = db.AddSupplier("調達先A", "A", "", "");
                    var answer = new AnkenSupplier { AnkenId = anken.Id, SupplierId = s.Id };

                    Assert.Throws<InvalidOperationException>(() => db.SaveSupplierAnswer(answer, new Quote[0]));
                }
            }
        }

        [Fact]
        public void 旧版のDBを新しい版へ移行しても案件が残る()
        {
            using (var dir = new TempDir())
            {
                var path = Path.Combine(dir.Path, "a.db");
                using (var db = new AnkenDb(path))
                {
                    db.AddClient("A");
                }

                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                using (var db2 = new AnkenDb(path))
                {
                    Assert.Single(db2.ListClients());
                    Assert.Empty(db2.ListSuppliers());
                }
            }
        }
    }
}
