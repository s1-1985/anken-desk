using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AnkenDesk.Core;
using Xunit;

namespace AnkenDesk.Core.Tests
{
    public class AnswerHistoryTests
    {
        private sealed class Env : IDisposable
        {
            public TempDir Dir = new TempDir();
            public AnkenDb Db;
            public AnkenRecord Anken;
            public Supplier Supplier;
            public IReadOnlyList<QuantityPattern> Patterns;

            public Env()
            {
                Db = new AnkenDb(Path.Combine(Dir.Path, "a.db"));
                var client = Db.AddClient("得意先A");
                var input = new AnkenInput { ClientId = client.Id, RequestDate = new DateTime(2026, 10, 5), PartNumber = "TEST-001", ReplyDueDate = new DateTime(2026, 10, 12) };
                input.Quantities.Add(new QuantityPattern { Kind = "試作", Quantity = 3, Unit = "個" });
                input.Quantities.Add(new QuantityPattern { Kind = "試作", Quantity = 10, Unit = "個" });
                Anken = Db.InsertAnken(input, "x");
                Patterns = Db.ListQuantities(Anken.Id);
                Supplier = Db.AddSupplier("調達先A", "A", "", "");
                Db.AddSupplierToAnken(Anken.Id, Supplier.Id);
            }

            public AnkenSupplier Answer(DateTime? received, string note)
            {
                var a = Db.ListAnkenSuppliers(Anken.Id).Single();
                a.ReceivedAt = received;
                a.Note = note;
                return a;
            }

            public Quote Q(int pattern, decimal price, int lt) =>
                new Quote { SupplierId = Supplier.Id, PatternId = Patterns[pattern].Id, UnitPrice = price, LeadTimeDays = lt };

            public void Dispose()
            {
                Db.Dispose();
                Dir.Dispose();
            }
        }

        [Fact]
        public void 出し直しで旧版を残すと履歴に入り現在は新しい内容になる()
        {
            using (var e = new Env())
            {
                e.Db.SaveSupplierAnswer(e.Answer(new DateTime(2026, 10, 7), "初回"), new[] { e.Q(0, 5000m, 14), e.Q(1, 4000m, 14) });

                e.Db.SaveNewVersion(e.Answer(new DateTime(2026, 10, 9), "出し直し"), new[] { e.Q(0, 4800m, 10) }, true, "A　旧.pdf");

                var now = e.Db.ListAnkenSuppliers(e.Anken.Id).Single();
                Assert.Equal("出し直し", now.Note);
                Assert.Equal(new DateTime(2026, 10, 9), now.ReceivedAt);
                var q = Assert.Single(e.Db.ListQuotes(e.Anken.Id));
                Assert.Equal(4800m, q.UnitPrice);

                var h = Assert.Single(e.Db.ListAnswerHistory(e.Anken.Id, e.Supplier.Id));
                Assert.Equal(1, h.Version);
                Assert.Equal("初回", h.Note);
                Assert.Equal(new DateTime(2026, 10, 7), h.ReceivedAt);
                Assert.Equal("A　旧.pdf", h.Files);
                Assert.Equal(2, h.Quotes.Count);
                Assert.Equal(5000m, h.Quotes.Single(x => x.PatternId == e.Patterns[0].Id).UnitPrice);
            }
        }

        [Fact]
        public void 旧版を残さないなら履歴は作らず現在だけ置き換わる()
        {
            using (var e = new Env())
            {
                e.Db.SaveSupplierAnswer(e.Answer(new DateTime(2026, 10, 7), "初回"), new[] { e.Q(0, 5000m, 14) });

                e.Db.SaveNewVersion(e.Answer(new DateTime(2026, 10, 9), "出し直し"), new[] { e.Q(0, 4800m, 10) }, false, "");

                Assert.Empty(e.Db.ListAnswerHistory(e.Anken.Id, e.Supplier.Id));
                Assert.Equal(4800m, Assert.Single(e.Db.ListQuotes(e.Anken.Id)).UnitPrice);
            }
        }

        [Fact]
        public void 版の番号は出し直しのたびに増え新しい版が先に並ぶ()
        {
            using (var e = new Env())
            {
                e.Db.SaveSupplierAnswer(e.Answer(new DateTime(2026, 10, 7), "v1"), new[] { e.Q(0, 5000m, 14) });
                e.Db.SaveNewVersion(e.Answer(new DateTime(2026, 10, 8), "v2"), new[] { e.Q(0, 4900m, 14) }, true, "");
                e.Db.SaveNewVersion(e.Answer(new DateTime(2026, 10, 9), "v3"), new[] { e.Q(0, 4800m, 14) }, true, "");

                var h = e.Db.ListAnswerHistory(e.Anken.Id, e.Supplier.Id);

                Assert.Equal(new[] { 2, 1 }, h.Select(x => x.Version).ToArray());
                Assert.Equal(new[] { "v2", "v1" }, h.Select(x => x.Note).ToArray());
            }
        }

        [Fact]
        public void 何も入っていない回答は旧版として残さない()
        {
            using (var e = new Env())
            {
                e.Db.SaveNewVersion(e.Answer(new DateTime(2026, 10, 9), "初回"), new[] { e.Q(0, 4800m, 10) }, true, "");

                Assert.Empty(e.Db.ListAnswerHistory(e.Anken.Id, e.Supplier.Id));
            }
        }

        [Fact]
        public void 調達先を外すと履歴も消える()
        {
            using (var e = new Env())
            {
                e.Db.SaveSupplierAnswer(e.Answer(new DateTime(2026, 10, 7), "初回"), new[] { e.Q(0, 5000m, 14) });
                e.Db.SaveNewVersion(e.Answer(new DateTime(2026, 10, 9), "出し直し"), new[] { e.Q(0, 4800m, 10) }, true, "");

                e.Db.RemoveSupplierFromAnken(e.Anken.Id, e.Supplier.Id);

                Assert.Empty(e.Db.ListAnswerHistory(e.Anken.Id, e.Supplier.Id));
            }
        }

        [Fact]
        public void 版2のDBを開くと版3へ移行して履歴の表ができる()
        {
            using (var dir = new TempDir())
            {
                var path = Path.Combine(dir.Path, "a.db");
                using (var db = new AnkenDb(path))
                {
                    Assert.Empty(db.ListAnswerHistory(1, 1));
                }
            }
        }
    }
}
