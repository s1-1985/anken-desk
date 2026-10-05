using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AnkenDesk.Core;
using Xunit;

namespace AnkenDesk.Core.Tests
{
    public class HomeTests
    {
        private static readonly DateTime Today = new DateTime(2026, 10, 5);

        private static AnkenRecord Anken(long id, string part, DateTime due, string client = "得意先A", string note = "")
        {
            return new AnkenRecord
            {
                Id = id, ClientName = client, PartNumber = part, Note = note, PartName = "ダミー品名",
                RequestDate = new DateTime(2026, 9, 29), ReplyDueDate = due, FolderPath = "x" + id,
            };
        }

        private static AnkenSupplier Sup(string name, bool answered)
        {
            return new AnkenSupplier { SupplierName = name, ShortName = name, ReceivedAt = answered ? new DateTime(2026, 10, 3) : (DateTime?)null };
        }

        private static IReadOnlyDictionary<long, IReadOnlyList<AnkenSupplier>> Map(params KeyValuePair<long, AnkenSupplier[]>[] items)
        {
            var d = new Dictionary<long, IReadOnlyList<AnkenSupplier>>();
            foreach (var kv in items)
            {
                d[kv.Key] = kv.Value;
            }

            return d;
        }

        private static KeyValuePair<long, AnkenSupplier[]> S(long id, params AnkenSupplier[] sups) =>
            new KeyValuePair<long, AnkenSupplier[]>(id, sups);

        [Fact]
        public void 要対応は未回答の調達先がある案件で期限の近い順に並ぶ()
        {
            var rows = Home.Build(
                new[] { Anken(1, "P1", Today.AddDays(5)), Anken(2, "P2", Today.AddDays(-3)), Anken(3, "P3", Today.AddDays(1)) },
                Map(S(1, Sup("A", false)), S(2, Sup("A", false), Sup("B", true)), S(3, Sup("A", false))),
                Today);

            var ids = Home.Filter(rows, HomeFilter.NeedsAction, null).Select(r => r.Anken.Id).ToArray();

            Assert.Equal(new long[] { 2, 3, 1 }, ids);
        }

        [Fact]
        public void 全社回答済みは要対応に入らず回答済みタブに入る()
        {
            var rows = Home.Build(new[] { Anken(1, "P1", Today.AddDays(5)) }, Map(S(1, Sup("A", true), Sup("B", true))), Today);

            Assert.Empty(Home.Filter(rows, HomeFilter.NeedsAction, null));
            Assert.Single(Home.Filter(rows, HomeFilter.Answered, null));
            Assert.Equal("回答済み", rows[0].StatusText);
        }

        [Fact]
        public void 調達先が無い案件は要対応でも回答済みでもない()
        {
            var rows = Home.Build(new[] { Anken(1, "P1", Today) }, Map(), Today);

            Assert.Empty(Home.Filter(rows, HomeFilter.NeedsAction, null));
            Assert.Empty(Home.Filter(rows, HomeFilter.Answered, null));
            Assert.Single(Home.Filter(rows, HomeFilter.All, null));
            Assert.Equal("依頼先なし", rows[0].StatusText);
        }

        [Fact]
        public void 状態は文字で返す()
        {
            var rows = Home.Build(
                new[] { Anken(1, "P1", Today.AddDays(-3)), Anken(2, "P2", Today), Anken(3, "P3", Today.AddDays(2)) },
                Map(S(1, Sup("A", false)), S(2, Sup("A", false)), S(3, Sup("A", false))),
                Today);

            var text = rows.ToDictionary(r => r.Anken.Id, r => r.StatusText);

            Assert.Equal("期限超過 3日", text[1]);
            Assert.Equal("本日期限", text[2]);
            Assert.Equal("あと2日", text[3]);
        }

        [Fact]
        public void 件数タイルは要対応の案件の中で数える()
        {
            var rows = Home.Build(
                new[]
                {
                    Anken(1, "P1", Today.AddDays(-3)), Anken(2, "P2", Today), Anken(3, "P3", Today.AddDays(2)),
                    Anken(4, "P4", Today.AddDays(3)), Anken(5, "P5", Today.AddDays(9)), Anken(6, "P6", Today.AddDays(-9)),
                },
                Map(S(1, Sup("A", false)), S(2, Sup("A", false)), S(3, Sup("A", false)), S(4, Sup("A", false)), S(5, Sup("A", false)),
                    S(6, Sup("A", true))),
                Today);

            var sum = Home.Summarize(rows);

            Assert.Equal(1, sum.Overdue);
            Assert.Equal(1, sum.Today);
            Assert.Equal(2, sum.Within3Days);
            Assert.Equal(5, sum.Waiting);
        }

        [Fact]
        public void 未回答の調達先の名前を持つ()
        {
            var rows = Home.Build(new[] { Anken(1, "P1", Today) }, Map(S(1, Sup("甲", true), Sup("乙", false))), Today);

            Assert.Equal("乙", Assert.Single(rows[0].PendingNames));
            Assert.Equal(1, rows[0].Answered);
            Assert.Equal(2, rows[0].Total);
        }

        [Theory]
        [InlineData("TEST", true)]
        [InlineData("test-001", true)]
        [InlineData("20260929", true)]
        [InlineData("2026/09/29", true)]
        [InlineData("調達先甲", true)]
        [InlineData("得意先A", true)]
        [InlineData("TEST　調達先甲", true)]
        [InlineData("TEST 無い語", false)]
        [InlineData("無い語", false)]
        [InlineData("", true)]
        [InlineData("   ", true)]
        public void 検索は品番_日付_調達先などを探す(string query, bool expected)
        {
            var rows = Home.Build(new[] { Anken(1, "TEST-001", Today) }, Map(S(1, Sup("調達先甲", false))), Today);

            Assert.Equal(expected, Home.Matches(rows[0], query));
        }

        [Fact]
        public void 案件名は日付と品番と備考()
        {
            Assert.Equal("20260929　TEST-001　Lot5", Home.Title(Anken(1, "TEST-001", Today, note: "Lot5")));
        }

        [Fact]
        public void 全案件の調達先をまとめて取れる()
        {
            using (var dir = new TempDir())
            using (var db = new AnkenDb(Path.Combine(dir.Path, "a.db")))
            {
                var client = db.AddClient("得意先A");
                var a1 = db.InsertAnken(new AnkenInput { ClientId = client.Id, RequestDate = Today, PartNumber = "P1", ReplyDueDate = Today }, "x1");
                var a2 = db.InsertAnken(new AnkenInput { ClientId = client.Id, RequestDate = Today, PartNumber = "P2", ReplyDueDate = Today }, "x2");
                var s1 = db.AddSupplier("甲", "甲", "", "");
                var s2 = db.AddSupplier("乙", "乙", "", "");
                db.AddSupplierToAnken(a1.Id, s1.Id);
                db.AddSupplierToAnken(a1.Id, s2.Id);

                var all = db.ListAllAnkenSuppliers();

                Assert.Equal(2, all[a1.Id].Count);
                Assert.False(all.ContainsKey(a2.Id));
            }
        }
    }
}
