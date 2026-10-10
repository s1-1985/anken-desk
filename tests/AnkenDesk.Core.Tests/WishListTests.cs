using System;
using System.IO;
using System.Linq;
using Xunit;

namespace AnkenDesk.Core.Tests
{
    public class NotesAndPricingDbTests
    {
        private sealed class Env : IDisposable
        {
            public TempDir Dir = new TempDir();
            public AnkenDb Db;
            public Client Client;
            public Supplier A, B;

            public Env()
            {
                Db = new AnkenDb(Path.Combine(Dir.Path, "a.db"));
                Client = Db.AddClient("ダミーC");
                A = Db.AddSupplier("ダミー甲", "甲", "k@example.com", "");
                B = Db.AddSupplier("ダミー乙", "乙", "o@example.com", "");
            }

            public AnkenRecord Make(string part, DateTime req, params Supplier[] suppliers)
            {
                var input = new AnkenInput { ClientId = Client.Id, RequestDate = req, PartNumber = part, ReplyDueDate = req.AddDays(7) };
                input.Quantities.Add(new QuantityPattern { Kind = "試作", Quantity = 3, Unit = "個/Lot" });
                input.Quantities.Add(new QuantityPattern { Kind = "量産", Quantity = 3000, Unit = "個/Lot" });
                var a = Db.InsertAnken(input, Path.Combine("c", part + req.ToString("yyyyMMdd")));
                foreach (var s in suppliers)
                {
                    Db.AddSupplierToAnken(a.Id, s.Id);
                }

                return a;
            }

            public void Dispose()
            {
                Db.Dispose();
                Dir.Dispose();
            }
        }

        [Fact]
        public void Notes_add_list_newest_first_update_delete_count()
        {
            using (var e = new Env())
            {
                var a = e.Make("P1", new DateTime(2026, 1, 5), e.A);
                Assert.Equal(0, e.Db.CountNotes(a.Id));
                var n1 = e.Db.AddNote(a.Id, null, "  最初のメモ  ");
                System.Threading.Thread.Sleep(1100); // 作成日時は秒単位
                var n2 = e.Db.AddNote(a.Id, e.A.Id, "甲との価格交渉", "mail");
                var list = e.Db.ListNotes(a.Id);
                Assert.Equal(new[] { n2.Id, n1.Id }, list.Select(n => n.Id).ToArray());
                Assert.Equal("最初のメモ", list[1].Text);
                Assert.Equal(e.A.Id, list[0].SupplierId);
                Assert.Equal("mail", list[0].Source);

                e.Db.UpdateNote(n1.Id, "直したメモ");
                Assert.Equal("直したメモ", e.Db.ListNotes(a.Id).Single(n => n.Id == n1.Id).Text);
                Assert.Throws<ArgumentException>(() => e.Db.AddNote(a.Id, null, "   "));
                Assert.Throws<ArgumentException>(() => e.Db.UpdateNote(n1.Id, ""));

                e.Db.DeleteNote(n2.Id);
                Assert.Equal(1, e.Db.CountNotes(a.Id));
            }
        }

        [Fact]
        public void Breakdown_replaces_per_pattern_and_follows_supplier_removal_and_restore()
        {
            using (var e = new Env())
            {
                var a = e.Make("P1", new DateTime(2026, 1, 5), e.A, e.B);
                var pats = e.Db.ListQuantities(a.Id);

                e.Db.SaveBreakdown(a.Id, e.A.Id, pats[0].Id, new[]
                {
                    new BreakdownLine { Item = "材料費", Amount = 1200.5m }, new BreakdownLine { Item = "加工費", Amount = 800 },
                    new BreakdownLine { Item = "  ", Amount = 5 }, // 項目名が空の行は保存しない
                });
                e.Db.SaveBreakdown(a.Id, e.A.Id, pats[1].Id, new[] { new BreakdownLine { Item = "表面処理;費|用", Amount = 30 } });
                e.Db.SaveBreakdown(a.Id, e.B.Id, pats[0].Id, new[] { new BreakdownLine { Item = "材料費", Amount = 999 } });

                var all = e.Db.ListBreakdowns(a.Id);
                var aFirst = all[new System.Collections.Generic.KeyValuePair<long, long>(e.A.Id, pats[0].Id)];
                Assert.Equal(new[] { "材料費", "加工費" }, aFirst.Select(l => l.Item).ToArray());
                Assert.Equal(1200.5m, aFirst[0].Amount);

                // 置き換え
                e.Db.SaveBreakdown(a.Id, e.A.Id, pats[0].Id, new[] { new BreakdownLine { Item = "加工費", Amount = 2000 } });
                Assert.Single(e.Db.ListBreakdowns(a.Id)[new System.Collections.Generic.KeyValuePair<long, long>(e.A.Id, pats[0].Id)]);

                // 調達先を外すと消え、戻すと（区切り文字を含む項目名も）戻る。他社のものは残る
                e.Db.RemoveSupplierFromAnken(a.Id, e.A.Id);
                var afterRemove = e.Db.ListBreakdowns(a.Id);
                Assert.Single(afterRemove);
                Assert.True(afterRemove.ContainsKey(new System.Collections.Generic.KeyValuePair<long, long>(e.B.Id, pats[0].Id)));
                Assert.True(e.Db.RestoreRemovedSupplier(e.Db.ListRemovedSuppliers(a.Id).Single().Id));
                var restored = e.Db.ListBreakdowns(a.Id);
                Assert.Equal(3, restored.Count);
                Assert.Equal(2000m, restored[new System.Collections.Generic.KeyValuePair<long, long>(e.A.Id, pats[0].Id)].Single().Amount);
                Assert.Equal("表面処理；費｜用", restored[new System.Collections.Generic.KeyValuePair<long, long>(e.A.Id, pats[1].Id)].Single().Item);
            }
        }

        [Fact]
        public void Breakdown_for_a_supplier_not_in_the_anken_is_rejected()
        {
            using (var e = new Env())
            {
                var a = e.Make("P1", new DateTime(2026, 1, 5), e.A);
                var pats = e.Db.ListQuantities(a.Id);
                Assert.Throws<InvalidOperationException>(() =>
                    e.Db.SaveBreakdown(a.Id, e.B.Id, pats[0].Id, new[] { new BreakdownLine { Item = "材料費", Amount = 1 } }));
            }
        }

        [Fact]
        public void Client_prices_roundtrip_and_replace()
        {
            using (var e = new Env())
            {
                var a = e.Make("P1", new DateTime(2026, 1, 5), e.A);
                var pats = e.Db.ListQuantities(a.Id);
                e.Db.SaveClientPrices(a.Id, new[]
                {
                    new ClientPrice { PatternId = pats[0].Id, SellingPrice = 5500.25m, AdoptedSupplierId = e.A.Id },
                    new ClientPrice { PatternId = pats[1].Id }, // 何も入っていない行は保存しない
                });
                var list = e.Db.ListClientPrices(a.Id);
                Assert.Single(list);
                Assert.Equal(5500.25m, list[0].SellingPrice);
                Assert.Equal(e.A.Id, list[0].AdoptedSupplierId);

                e.Db.SaveClientPrices(a.Id, new[] { new ClientPrice { PatternId = pats[1].Id, SellingPrice = 900m } });
                Assert.Equal(pats[1].Id, e.Db.ListClientPrices(a.Id).Single().PatternId);
            }
        }

        [Fact]
        public void Result_sets_status_and_adopted_supplier_only_for_won()
        {
            using (var e = new Env())
            {
                var a = e.Make("P1", new DateTime(2026, 1, 5), e.A);
                e.Db.SetAnkenResult(a.Id, AnkenStatus.Won, new DateTime(2026, 2, 1), "  価格で決まった  ", e.A.Id);
                var won = e.Db.GetAnken(a.Id)!;
                Assert.Equal(AnkenStatus.Won, won.Status);
                Assert.Equal(new DateTime(2026, 2, 1), won.ResultDate);
                Assert.Equal("価格で決まった", won.ResultNote);
                Assert.Equal(e.A.Id, won.AdoptedSupplierId);

                e.Db.SetAnkenResult(a.Id, AnkenStatus.Lost, null, "他社に決まった", e.A.Id);
                var lost = e.Db.GetAnken(a.Id)!;
                Assert.Null(lost.AdoptedSupplierId);
                Assert.Null(lost.ResultDate);
                Assert.Throws<ArgumentException>(() => e.Db.SetAnkenResult(a.Id, "なぞ", null, "", null));
            }
        }

        [Fact]
        public void Supplier_specialty_is_saved_separately()
        {
            using (var e = new Env())
            {
                e.Db.SetSupplierSpecialty(e.A.Id, " 切削 ステンレス ");
                Assert.Equal("切削 ステンレス", e.Db.ListSuppliers().Single(s => s.Id == e.A.Id).Specialty);
                Assert.Equal("", e.Db.ListSuppliers().Single(s => s.Id == e.B.Id).Specialty);
            }
        }

        [Fact]
        public void Real_v5_database_migrates_to_v6()
        {
            using (var t = new TempDir())
            {
                var path = Path.Combine(t.Path, "v5.db");
                using (var db = new AnkenDb(path))
                {
                    var c = db.AddClient("ダミーC");
                    db.AddSupplier("ダミー甲", "甲", "", "");
                    db.InsertAnken(new AnkenInput { ClientId = c.Id, RequestDate = new DateTime(2026, 1, 5), PartNumber = "P1", ReplyDueDate = new DateTime(2026, 1, 9) }, "x");
                }

                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                using (var conn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + path))
                {
                    conn.Open();
                    foreach (var sql in new[]
                    {
                        "ALTER TABLE anken DROP COLUMN result_date", "ALTER TABLE anken DROP COLUMN result_note", "ALTER TABLE anken DROP COLUMN adopted_supplier_id",
                        "ALTER TABLE supplier DROP COLUMN specialty", "ALTER TABLE removed_supplier DROP COLUMN breakdowns",
                        "DROP TABLE anken_note", "DROP TABLE quote_breakdown", "DROP TABLE client_price", "PRAGMA user_version = 5",
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
                    Assert.Null(a.AdoptedSupplierId);
                    Assert.Equal("", db.ListSuppliers().Single().Specialty);
                    db.AddNote(a.Id, null, "移行後のメモ");
                    Assert.Equal(1, db.CountNotes(a.Id));
                }
            }
        }

        [Theory]
        [InlineData("DUMMY-001", "dummy 001", SimilarKind.Same)]
        [InlineData("DUMMY-001", "DUMMY-001-A", SimilarKind.Contains)]
        [InlineData("ABCDEF-12", "ABCDEF-99", SimilarKind.Prefix)]
        public void Part_numbers_are_compared_ignoring_separators_and_case(string a, string b, SimilarKind expected)
        {
            Assert.Equal(expected, SimilarAnkens.Compare(a, b));
        }

        [Theory]
        [InlineData("DUMMY-001", "OTHER-001")]
        [InlineData("AB", "AB")] // 短すぎる品番でも同じなら一致する
        [InlineData("", "X")]
        [InlineData("ABC12", "ABC34")]
        public void Unrelated_part_numbers_do_not_match(string a, string b)
        {
            var r = SimilarAnkens.Compare(a, b);
            if (a == "AB")
            {
                Assert.Equal(SimilarKind.Same, r);
            }
            else
            {
                Assert.Null(r);
            }
        }

        [Fact]
        public void Similar_ankens_come_nearest_first_with_price_difference_to_the_current_one()
        {
            using (var e = new Env())
            {
                var past = e.Make("DUMMY-001", new DateTime(2025, 6, 1), e.A);
                var near = e.Make("DUMMY-001-A", new DateTime(2025, 9, 1), e.A);
                e.Make("OTHER-777", new DateTime(2025, 10, 1), e.A);
                var cur = e.Make("DUMMY001", new DateTime(2026, 1, 5), e.A);

                void Quote(AnkenRecord anken, decimal price)
                {
                    var pat = e.Db.ListQuantities(anken.Id)[0];
                    var ans = e.Db.ListAnkenSuppliers(anken.Id).Single();
                    e.Db.SaveSupplierAnswer(ans, new[] { new Quote { SupplierId = e.A.Id, PatternId = pat.Id, UnitPrice = price, LeadTimeDays = 10 } });
                }

                Quote(past, 100m);
                Quote(near, 80m);
                Quote(cur, 110m);

                var found = SimilarAnkens.Find(e.Db, e.Db.GetAnken(cur.Id)!);
                Assert.Equal(new[] { "DUMMY-001", "DUMMY-001-A" }, found.Select(f => f.Anken.PartNumber).ToArray());
                Assert.Equal(SimilarKind.Same, found[0].Kind);
                var p = found[0].Prices.Single();
                Assert.Equal(100m, p.Price);
                Assert.Equal(110m, p.CurrentPrice);
                Assert.Equal(10.0m, p.DiffPercent);
                Assert.Equal("試作 3個/Lot", p.PatternText);
                Assert.Equal(37.5m, found[1].Prices.Single().DiffPercent); // (110-80)/80
            }
        }

        [Fact]
        public void Margin_math()
        {
            Assert.Equal(120m, Margin.SellingFromMarkup(100m, 20m));
            Assert.Equal(100m, Margin.SellingFromGrossMargin(80m, 20m));
            Assert.Null(Margin.SellingFromGrossMargin(80m, 100m));
            Assert.Equal(20m, Margin.GrossProfit(100m, 80m));
            Assert.Equal(20.0m, Margin.GrossMarginPercent(100m, 80m));
            Assert.Equal(25.0m, Margin.MarkupPercent(100m, 80m));
            Assert.Null(Margin.GrossMarginPercent(0m, 80m));
            Assert.Null(Margin.MarkupPercent(100m, 0m));
            Assert.Equal(33.33m, Margin.Round(33.3333m));
            Assert.Equal(0.01m, Margin.Round(0.005m)); // 四捨五入（0に向けない）
            Assert.Equal(-5.0m, Margin.GrossMarginPercent(100m, 105m)); // 赤字
        }
    }

    public class FileGroupDescriptorTests
    {
        private static byte[] Build(params string[] names)
        {
            var bytes = new byte[4 + names.Length * 592];
            BitConverter.GetBytes(names.Length).CopyTo(bytes, 0);
            for (var i = 0; i < names.Length; i++)
            {
                System.Text.Encoding.Unicode.GetBytes(names[i]).CopyTo(bytes, 4 + i * 592 + 72);
            }

            return bytes;
        }

        [Fact]
        public void Reads_names_of_each_virtual_file()
        {
            var names = FileGroupDescriptor.FileNames(Build("御見積書【DUMMY-001】.msg", "二通目.MSG"));
            Assert.Equal(new[] { "御見積書【DUMMY-001】.msg", "二通目.MSG" }, names.ToArray());
            Assert.True(FileGroupDescriptor.HasMsg(Build("a.pdf", "b.msg")));
            Assert.False(FileGroupDescriptor.HasMsg(Build("a.pdf")));
        }

        [Fact]
        public void Broken_or_empty_data_gives_nothing_and_does_not_throw()
        {
            Assert.Empty(FileGroupDescriptor.FileNames(new byte[0]));
            Assert.Empty(FileGroupDescriptor.FileNames(new byte[10]));
            var lying = Build("a.msg");
            BitConverter.GetBytes(99999).CopyTo(lying, 0); // 件数が大きすぎる
            Assert.Single(FileGroupDescriptor.FileNames(lying));
            Assert.False(FileGroupDescriptor.HasMsg(null!));
        }
    }

    public class InboundNoteTests
    {
        private static InboundMail Mail()
        {
            var m = new InboundMail { EntryId = "E1", Subject = "御見積書【DUMMY-001】", SenderName = "甲の担当", SenderAddress = "k@example.com", ReceivedAt = new DateTime(2026, 10, 6, 8, 30, 0) };
            m.Attachments.Add(new InboundAttachment { Index = 1, FileName = "見積書.pdf", Size = 1 });
            return m;
        }

        private static void Setup(TempDir t, out AnkenDb db, out AnkenRecord anken, out Supplier sup, out string full)
        {
            db = new AnkenDb(Path.Combine(t.Path, "a.db"));
            var c = db.AddClient("ダミーC");
            anken = db.InsertAnken(new AnkenInput { ClientId = c.Id, RequestDate = new DateTime(2026, 10, 5), PartNumber = "DUMMY-001", ReplyDueDate = new DateTime(2026, 10, 12) }, Path.Combine("c", "x"));
            full = Path.Combine(t.Path, "ws", anken.FolderPath);
            Directory.CreateDirectory(Path.Combine(full, FolderNames.Subfolders[4]));
            sup = db.AddSupplier("ダミー甲", "甲", "k@example.com", "");
            db.AddSupplierToAnken(anken.Id, sup.Id);
        }

        [Fact]
        public void Body_is_recorded_as_a_mail_note_linked_to_the_supplier()
        {
            using (var t = new TempDir())
            using (var inbox = new FakeMailInbox { Body = "お世話になっております。見積を添付します。" })
            {
                AnkenDb db; AnkenRecord a; Supplier s; string full;
                Setup(t, out db, out a, out s, out full);
                using (db)
                {
                    var outcome = InboundImporter.Import(inbox, db, a, full, s, Mail(), new[] { 1 }, false, new DateTime(2026, 10, 6), new DateTime(2026, 10, 6, 9, 0, 0), recordBodyAsNote: true);
                    Assert.True(outcome.NoteRecorded);
                    var note = db.ListNotes(a.Id).Single();
                    Assert.Equal("mail", note.Source);
                    Assert.Equal(s.Id, note.SupplierId);
                    Assert.Contains("見積を添付します", note.Text);
                    Assert.Contains("御見積書【DUMMY-001】", note.Text);
                    Assert.Contains("k@example.com", note.Text);
                    Assert.True(db.ListAnkenSuppliers(a.Id).Single().IsAnswered);
                }
            }
        }

        [Fact]
        public void Note_only_import_with_no_attachments_still_logs_and_marks_received()
        {
            using (var t = new TempDir())
            using (var inbox = new FakeMailInbox { Body = "今回は辞退します" })
            {
                AnkenDb db; AnkenRecord a; Supplier s; string full;
                Setup(t, out db, out a, out s, out full);
                using (db)
                {
                    var outcome = InboundImporter.Import(inbox, db, a, full, s, Mail(), new int[0], false, new DateTime(2026, 10, 6), DateTime.Now, recordBodyAsNote: true);
                    Assert.True(outcome.NoteRecorded);
                    Assert.False(outcome.AnythingSaved);
                    Assert.True(outcome.ReceivedDateMarked);
                    Assert.Single(db.ListMailLog(a.Id));
                }
            }
        }

        [Fact]
        public void Body_failure_does_not_stop_the_attachments()
        {
            using (var t = new TempDir())
            using (var inbox = new FakeMailInbox { FailBody = true })
            {
                AnkenDb db; AnkenRecord a; Supplier s; string full;
                Setup(t, out db, out a, out s, out full);
                using (db)
                {
                    var outcome = InboundImporter.Import(inbox, db, a, full, s, Mail(), new[] { 1 }, false, null, DateTime.Now, recordBodyAsNote: true);
                    Assert.False(outcome.NoteRecorded);
                    Assert.Single(outcome.SavedFiles);
                    Assert.Single(outcome.Errors);
                    Assert.Empty(db.ListNotes(a.Id));
                }
            }
        }

        [Fact]
        public void Long_body_is_cut_with_a_pointer_to_the_msg()
        {
            var text = InboundImporter.MailNoteText(Mail(), new string('あ', 5000));
            Assert.Contains("以降は省略", text);
            Assert.True(text.Length < 4400);
        }

        [Fact]
        public void Selected_mails_come_from_the_selection()
        {
            using (var inbox = new FakeMailInbox())
            {
                inbox.Selected.Add(Mail());
                inbox.Selected.Add(Mail());
                Assert.Single(inbox.GetSelected(1));
                Assert.Equal(2, inbox.GetSelected(10).Count);
            }
        }
    }
}
