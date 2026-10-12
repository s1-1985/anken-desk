using System;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using Xunit;

namespace AnkenDesk.Core.Tests
{
    public class RestoreTests
    {
        [Fact]
        public void List_is_newest_first_and_restore_swaps_db_keeping_the_old_one()
        {
            using (var t = new TempDir())
            {
                var dbPath = Path.Combine(t.Path, "anken.db");
                var backup = Path.Combine(t.Path, "backup");
                using (var db = new AnkenDb(dbPath)) { db.AddClient("元のデータ"); }
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                DbBackup.CreateDaily(dbPath, backup, new DateTime(2026, 10, 1, 9, 0, 0));
                DbBackup.CreateDaily(dbPath, backup, new DateTime(2026, 10, 2, 9, 0, 0));
                using (var db = new AnkenDb(dbPath)) { db.AddClient("あとから足した"); }
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

                var list = DbBackup.List(backup);
                Assert.Equal(2, list.Count);
                Assert.True(list[0].At > list[1].At);

                DbBackup.RequestRestore(list[0].Path, dbPath);
                Assert.True(File.Exists(DbBackup.RestoreMarkerPath(dbPath)));
                Assert.True(DbBackup.ApplyPendingRestore(dbPath, backup, new DateTime(2026, 10, 3, 8, 0, 0)));
                Assert.False(File.Exists(DbBackup.RestoreMarkerPath(dbPath)));
                Assert.False(DbBackup.ApplyPendingRestore(dbPath, backup, DateTime.Now)); // 二度は入れ替えない

                using (var db = new AnkenDb(dbPath)) { Assert.Single(db.ListClients()); }
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                // 入れ替え前のDBが残っていて、控えの一覧には混ざらない
                Assert.Single(Directory.GetFiles(backup, "before-restore-*.db"));
                Assert.Equal(2, DbBackup.List(backup).Count);
            }
        }

        [Fact]
        public void Non_sqlite_file_is_refused()
        {
            using (var t = new TempDir())
            {
                var bad = Path.Combine(t.Path, "x.db");
                File.WriteAllText(bad, "これはDBではない");
                Assert.Throws<InvalidDataException>(() => DbBackup.RequestRestore(bad, Path.Combine(t.Path, "anken.db")));

                var db = Path.Combine(t.Path, "anken.db");
                File.WriteAllText(db, "keep");
                File.WriteAllText(DbBackup.RestoreMarkerPath(db), "壊れた予約");
                Assert.False(DbBackup.ApplyPendingRestore(db, Path.Combine(t.Path, "b"), DateTime.Now));
                Assert.Equal("keep", File.ReadAllText(db)); // 今のDBは守られる
            }
        }
    }

    public class SupplierStatsTests
    {
        [Fact]
        public void Counts_answers_reply_days_and_cheapest_only_when_contested()
        {
            var d = new DateTime(2026, 1, 10);
            AnkenSupplier S(long id, DateTime? sent, DateTime? recv) =>
                new AnkenSupplier { SupplierId = id, SupplierName = "社" + id, ShortName = "S" + id, SentAt = sent, ReceivedAt = recv };

            var by = new System.Collections.Generic.Dictionary<long, System.Collections.Generic.IReadOnlyList<AnkenSupplier>>
            {
                [1] = new[] { S(1, d, d.AddDays(2)), S(2, d, d.AddDays(4)) },
                [2] = new[] { S(1, d, null), S(3, null, null) },
                [3] = new[] { S(1, d, d.AddDays(1)) }, // 1社だけ → 最安に数えない
            };
            System.Collections.Generic.IReadOnlyList<Quote> Quotes(long anken)
            {
                if (anken == 1)
                {
                    return new[]
                    {
                        new Quote { SupplierId = 1, PatternId = 10, UnitPrice = 100 }, new Quote { SupplierId = 2, PatternId = 10, UnitPrice = 90 },
                        new Quote { SupplierId = 1, PatternId = 11, UnitPrice = 50 }, new Quote { SupplierId = 2, PatternId = 11, UnitPrice = 60 },
                    };
                }

                return anken == 3 ? new[] { new Quote { SupplierId = 1, PatternId = 20, UnitPrice = 10 } } : new Quote[0];
            }

            var stats = SupplierStats.Compute(by, Quotes);
            var s1 = stats.Single(x => x.SupplierId == 1);
            Assert.Equal(3, s1.Ankens);
            Assert.Equal(3, s1.Requested);
            Assert.Equal(2, s1.Answered);
            Assert.Equal(1.5, s1.AverageReplyDays);
            Assert.Equal(1, s1.CheapestAnkens); // 案件1の数量11だけ。案件3は1社だけなので数えない
            Assert.Equal(2.0 / 3, s1.AnswerRate!.Value, 6);
            var s2 = stats.Single(x => x.SupplierId == 2);
            Assert.Equal(1, s2.CheapestAnkens);
            var s3 = stats.Single(x => x.SupplierId == 3);
            Assert.Null(s3.AnswerRate);
            Assert.Null(s3.AverageReplyDays);
            Assert.Equal(1, stats[0].SupplierId); // 案件数の多い順
        }
    }

    public class ExcelExportsTests
    {
        [Fact]
        public void Comparison_sheet_has_prices_as_numbers_and_marks_cheapest_in_text()
        {
            using (var t = new TempDir())
            {
                var anken = new AnkenRecord { Id = 1, ClientName = "ダミー得意先", PartNumber = "DUMMY-001", RequestDate = new DateTime(2026, 1, 5), ReplyDueDate = new DateTime(2026, 1, 12) };
                var pats = new[]
                {
                    new QuantityPattern { Id = 10, Kind = "試作", Quantity = 3, Unit = "個/Lot" },
                    new QuantityPattern { Id = 11, Kind = "量産", Quantity = 3000, Unit = "個/Lot" },
                };
                var sups = new[]
                {
                    new AnkenSupplier { SupplierId = 1, SupplierName = "ダミー甲", ShortName = "甲", ExtraCost = "治具費", ReceivedAt = new DateTime(2026, 1, 8) },
                    new AnkenSupplier { SupplierId = 2, SupplierName = "ダミー乙", ShortName = "乙" },
                };
                var quotes = new[]
                {
                    new Quote { SupplierId = 1, PatternId = 10, UnitPrice = 4850.5m, LeadTimeDays = 14 },
                    new Quote { SupplierId = 2, PatternId = 10, UnitPrice = 5200m },
                    new Quote { SupplierId = 1, PatternId = 11, UnitPrice = 950m },
                    new Quote { SupplierId = 2, PatternId = 11, UnitPrice = 900m, LeadTimeDays = 30 },
                };
                var path = Path.Combine(t.Path, "out", "比較表.xlsx");
                ExcelExports.WriteComparison(path, anken, pats, sups, quotes);

                using (var wb = new XLWorkbook(path))
                {
                    var ws = wb.Worksheet("比較表");
                    Assert.Equal("試作 3個/Lot", ws.Cell(7, 1).GetString());
                    Assert.Equal(4850.5, ws.Cell(7, 2).GetDouble());
                    Assert.Equal(14, ws.Cell(7, 3).GetDouble());
                    Assert.Equal(900, ws.Cell(8, 4).GetDouble());
                    Assert.Equal("ダミー甲", ws.Cell(5, 2).GetString());
                    Assert.Contains("甲", ws.Cell(7, 6).GetString());
                    Assert.Contains("乙", ws.Cell(8, 6).GetString());
                    Assert.DoesNotContain("乙", ws.Cell(7, 6).GetString());
                    Assert.True(ws.Cell(7, 2).Style.Font.Bold);
                    Assert.False(ws.Cell(7, 4).Style.Font.Bold);
                }
            }
        }

        [Fact]
        public void Anken_list_export_writes_one_row_per_anken()
        {
            using (var t = new TempDir())
            {
                var rows = new[]
                {
                    new HomeRow { Anken = new AnkenRecord { Id = 1, ClientName = "ダミー", PartNumber = "P1", RequestDate = new DateTime(2026, 1, 5), ReplyDueDate = new DateTime(2026, 1, 12), FolderPath = "ダミー\\x" }, Total = 2, Answered = 1, PendingNames = new[] { "乙" } },
                    new HomeRow { Anken = new AnkenRecord { Id = 2, ClientName = "ダミー", PartNumber = "P2", RequestDate = new DateTime(2026, 1, 6), ReplyDueDate = new DateTime(2026, 1, 13) } },
                };
                var path = Path.Combine(t.Path, "list.xlsx");
                ExcelExports.WriteAnkenList(path, rows);
                using (var wb = new XLWorkbook(path))
                {
                    var ws = wb.Worksheet(1);
                    Assert.Equal("品番", ws.Cell(1, 5).GetString());
                    Assert.Equal("P1", ws.Cell(2, 5).GetString());
                    Assert.Equal(1, ws.Cell(2, 8).GetDouble());
                    Assert.Equal("乙", ws.Cell(2, 10).GetString());
                    Assert.Equal("P2", ws.Cell(3, 5).GetString());
                    Assert.True(string.IsNullOrEmpty(ws.Cell(4, 5).GetString()));
                }
            }
        }
    }

    public class HomeExtrasTests
    {
        private static readonly DateTime Today = new DateTime(2026, 10, 5);

        private static HomeRow Row(long id, string status, int daysToDue, bool remind, bool answered = false)
        {
            return new HomeRow
            {
                Anken = new AnkenRecord { Id = id, Status = status, ReplyDueDate = Today.AddDays(daysToDue) },
                DaysToDue = daysToDue,
                Total = 2,
                Answered = answered ? 2 : 1,
                RemindSupplierIds = remind ? new long[] { 1 } : new long[0],
            };
        }

        [Fact]
        public void Closed_ankens_never_need_action_and_have_their_own_filter()
        {
            var rows = new[] { Row(1, AnkenStatus.InProgress, 2, true), Row(2, AnkenStatus.Won, 2, true), Row(3, AnkenStatus.OnHold, -5, true) };
            Assert.Equal(new long[] { 1 }, Home.Filter(rows, HomeFilter.NeedsAction, null).Select(r => r.Anken.Id).ToArray());
            Assert.Equal(new long[] { 2, 3 }, Home.Filter(rows, HomeFilter.Closed, null).Select(r => r.Anken.Id).OrderBy(x => x).ToArray());
            Assert.Equal("受注", rows[1].StatusText);
            Assert.Equal(1, Home.Summarize(rows).Waiting);
            Assert.Empty(Home.RemindCandidates(rows.Skip(1)));
        }

        [Fact]
        public void DueThisWeek_filter_and_remind_candidates()
        {
            var rows = new[] { Row(1, AnkenStatus.InProgress, 7, true), Row(2, AnkenStatus.InProgress, 8, true), Row(3, AnkenStatus.InProgress, -2, true), Row(4, AnkenStatus.InProgress, 1, false), Row(5, AnkenStatus.InProgress, 0, true) };
            Assert.Equal(new long[] { 1, 3, 4, 5 }, Home.Filter(rows, HomeFilter.DueThisWeek, null).Select(r => r.Anken.Id).OrderBy(x => x).ToArray());
            // 催促の候補は「依頼済みで未回答あり、期限が明日以前」。期限の近い順
            Assert.Equal(new long[] { 3, 5 }, Home.RemindCandidates(rows).Select(r => r.Anken.Id).ToArray());
        }

        [Fact]
        public void Status_mark_distinguishes_states_by_symbol()
        {
            Assert.Equal("▲ ", Home.StatusMark(Row(1, AnkenStatus.InProgress, -1, true)));
            Assert.Equal("△ ", Home.StatusMark(Row(1, AnkenStatus.InProgress, 1, true)));
            Assert.Equal("□ ", Home.StatusMark(Row(1, AnkenStatus.InProgress, 5, true)));
            Assert.Equal("○ ", Home.StatusMark(Row(1, AnkenStatus.InProgress, 5, false, true)));
            Assert.Equal("－ ", Home.StatusMark(Row(1, AnkenStatus.Lost, 5, true)));
        }
    }
}
