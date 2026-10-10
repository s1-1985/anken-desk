using System;
using System.IO;
using System.Linq;
using Xunit;

namespace AnkenDesk.Core.Tests
{
    public class ExtrasDbTests
    {
        private static AnkenRecord Make(AnkenDb db, long client, string part, DateTime req)
        {
            var input = new AnkenInput { ClientId = client, RequestDate = req, PartNumber = part, ReplyDueDate = req.AddDays(7) };
            input.Quantities.Add(new QuantityPattern { Kind = "試作", Quantity = 1, Unit = "個/Lot" });
            input.Quantities.Add(new QuantityPattern { Kind = "量産", Quantity = 1000, Unit = "個/Lot" });
            return db.InsertAnken(input, Path.Combine("c" + client, part));
        }

        [Fact]
        public void Status_defaults_and_can_change_and_rejects_unknown()
        {
            using (var t = new TempDir())
            using (var db = new AnkenDb(Path.Combine(t.Path, "a.db")))
            {
                var c = db.AddClient("ダミーC");
                var a = Make(db, c.Id, "P1", new DateTime(2026, 1, 5));
                Assert.Equal(AnkenStatus.InProgress, db.GetAnken(a.Id)!.Status);
                db.SetAnkenStatus(a.Id, AnkenStatus.Won);
                Assert.Equal(AnkenStatus.Won, db.GetAnken(a.Id)!.Status);
                Assert.True(AnkenStatus.IsClosed(AnkenStatus.Won));
                Assert.False(AnkenStatus.IsClosed(AnkenStatus.Submitted));
                Assert.Throws<ArgumentException>(() => db.SetAnkenStatus(a.Id, "なぞ"));
            }
        }

        [Fact]
        public void Removed_supplier_can_be_restored_with_answer_and_quotes()
        {
            using (var t = new TempDir())
            using (var db = new AnkenDb(Path.Combine(t.Path, "a.db")))
            {
                var c = db.AddClient("ダミーC");
                var s = db.AddSupplier("ダミー甲", "甲", "k@example.com", "");
                var a = Make(db, c.Id, "P1", new DateTime(2026, 1, 5));
                db.AddSupplierToAnken(a.Id, s.Id);
                var pats = db.ListQuantities(a.Id);
                var ans = db.ListAnkenSuppliers(a.Id).Single();
                ans.SentAt = new DateTime(2026, 1, 6); ans.ReceivedAt = new DateTime(2026, 1, 8); ans.ExtraCost = "治具費"; ans.Note = "メモ";
                db.SaveSupplierAnswer(ans, new[]
                {
                    new Quote { SupplierId = s.Id, PatternId = pats[0].Id, UnitPrice = 4850.5m, LeadTimeDays = 14 },
                    new Quote { SupplierId = s.Id, PatternId = pats[1].Id, UnitPrice = 900m },
                });

                db.RemoveSupplierFromAnken(a.Id, s.Id);
                Assert.Empty(db.ListAnkenSuppliers(a.Id));
                Assert.Empty(db.ListQuotes(a.Id));
                var removed = db.ListRemovedSuppliers(a.Id).Single();
                Assert.True(removed.HadAnswer);
                Assert.Equal("ダミー甲", removed.SupplierName);

                Assert.True(db.RestoreRemovedSupplier(removed.Id));
                var back = db.ListAnkenSuppliers(a.Id).Single();
                Assert.Equal(new DateTime(2026, 1, 8), back.ReceivedAt);
                Assert.Equal("治具費", back.ExtraCost);
                var qs = db.ListQuotes(a.Id).OrderBy(q => q.PatternId).ToList();
                Assert.Equal(4850.5m, qs[0].UnitPrice);
                Assert.Equal(14, qs[0].LeadTimeDays);
                Assert.Equal(900m, qs[1].UnitPrice);
                Assert.Null(qs[1].LeadTimeDays);
                Assert.Empty(db.ListRemovedSuppliers(a.Id));
                Assert.False(db.RestoreRemovedSupplier(removed.Id)); // 二重には戻せない
            }
        }

        [Fact]
        public void Restore_does_nothing_if_supplier_was_added_again()
        {
            using (var t = new TempDir())
            using (var db = new AnkenDb(Path.Combine(t.Path, "a.db")))
            {
                var c = db.AddClient("ダミーC");
                var s = db.AddSupplier("ダミー甲", "甲", "", "");
                var a = Make(db, c.Id, "P1", new DateTime(2026, 1, 5));
                db.AddSupplierToAnken(a.Id, s.Id);
                db.RemoveSupplierFromAnken(a.Id, s.Id);
                db.AddSupplierToAnken(a.Id, s.Id);
                Assert.False(db.RestoreRemovedSupplier(db.ListRemovedSuppliers(a.Id).Single().Id));
                Assert.Single(db.ListAnkenSuppliers(a.Id));
            }
        }

        [Fact]
        public void Recent_suppliers_come_from_same_client_newest_first_excluding_self()
        {
            using (var t = new TempDir())
            using (var db = new AnkenDb(Path.Combine(t.Path, "a.db")))
            {
                var c1 = db.AddClient("ダミーC1");
                var c2 = db.AddClient("ダミーC2");
                var s1 = db.AddSupplier("甲社", "甲", "", "");
                var s2 = db.AddSupplier("乙社", "乙", "", "");
                var s3 = db.AddSupplier("丙社", "丙", "", "");
                var old = Make(db, c1.Id, "OLD", new DateTime(2026, 1, 1));
                var recent = Make(db, c1.Id, "NEW", new DateTime(2026, 2, 1));
                var other = Make(db, c2.Id, "OTH", new DateTime(2026, 3, 1));
                var self = Make(db, c1.Id, "SELF", new DateTime(2026, 4, 1));
                db.AddSupplierToAnken(old.Id, s1.Id);
                db.AddSupplierToAnken(recent.Id, s2.Id);
                db.AddSupplierToAnken(other.Id, s3.Id);
                db.AddSupplierToAnken(self.Id, s3.Id);

                var ids = db.RecentSupplierIdsForClient(c1.Id, self.Id);
                Assert.Equal(new[] { s2.Id, s1.Id }, ids.ToArray());
                Assert.Equal("NEW", db.LatestAnkenOfClient(c1.Id, self.Id)!.PartNumber);
                Assert.Null(db.LatestAnkenOfClient(c2.Id, other.Id));
            }
        }

        [Fact]
        public void UpdateAnkenFolder_changes_path_and_rejects_duplicates()
        {
            using (var t = new TempDir())
            using (var db = new AnkenDb(Path.Combine(t.Path, "a.db")))
            {
                var c = db.AddClient("ダミーC");
                var a = Make(db, c.Id, "P1", new DateTime(2026, 1, 5));
                var b = Make(db, c.Id, "P2", new DateTime(2026, 1, 6));
                db.UpdateAnkenFolder(a.Id, "新しい場所");
                Assert.Equal("新しい場所", db.GetAnken(a.Id)!.FolderPath);
                Assert.Throws<InvalidOperationException>(() => db.UpdateAnkenFolder(b.Id, "新しい場所"));
            }
        }

        [Fact]
        public void Real_v4_database_migrates_to_v5_keeping_data()
        {
            using (var t = new TempDir())
            {
                var path = Path.Combine(t.Path, "old.db");
                using (var db = new AnkenDb(path))
                {
                    var c = db.AddClient("ダミーC");
                    Make(db, c.Id, "P1", new DateTime(2026, 1, 5));
                }

                // v5で足したものを外して、v4の状態に戻す。
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                using (var conn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + path))
                {
                    conn.Open();
                    foreach (var sql in new[]
                    {
                        "ALTER TABLE anken DROP COLUMN calendar_entry_id", "ALTER TABLE anken DROP COLUMN status", "ALTER TABLE anken DROP COLUMN result_date", "ALTER TABLE anken DROP COLUMN result_note",
                        "ALTER TABLE anken DROP COLUMN adopted_supplier_id", "ALTER TABLE supplier DROP COLUMN specialty",
                        "DROP TABLE removed_supplier", "DROP TABLE anken_note", "DROP TABLE quote_breakdown", "DROP TABLE client_price",
                        "PRAGMA user_version = 4",
                    })
                    {
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = sql;
                            cmd.ExecuteNonQuery();
                        }
                    }
                }

                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                using (var db = new AnkenDb(path))
                {
                    var a = db.ListAnkens().Single();
                    Assert.Equal("P1", a.PartNumber);
                    Assert.Equal(AnkenStatus.InProgress, a.Status);
                    Assert.Empty(db.ListRemovedSuppliers(a.Id));
                }
            }
        }
    }
}
