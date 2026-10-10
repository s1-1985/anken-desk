using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace AnkenDesk.Core.Tests
{
    public class PartNumberGuessTests
    {
        [Theory]
        [InlineData("【見積依頼】ABC-12345 ブラケット", "ABC-12345")]
        [InlineData("Re: 20261005 DUMMY-001 の件", "DUMMY-001")]
        [InlineData("FW: RFQ 2026/10/06 XY12345A のお願い", "XY12345A")]
        [InlineData("図面送付 12345-678-9", "12345-678-9")]
        public void Picks_part_like_tokens_and_skips_dates(string subject, string expectedFirst)
        {
            Assert.Equal(expectedFirst, PartNumberGuess.FromSubject(subject)[0]);
        }

        [Fact]
        public void Nothing_when_no_candidates_and_dedupes_and_caps()
        {
            Assert.Empty(PartNumberGuess.FromSubject("お世話になっております"));
            Assert.Empty(PartNumberGuess.FromSubject("PDF xlsx 20261005"));
            Assert.Single(PartNumberGuess.FromSubject("AB-1234 ab-1234"));
            Assert.Equal(5, PartNumberGuess.FromSubject("A-1111 B-2222 C-3333 D-4444 E-5555 F-6666").Count);
        }
    }

    public class ClientMailTests
    {
        private sealed class Env : IDisposable
        {
            public TempDir Dir = new TempDir();
            public AnkenDb Db;
            public AnkenRecord Anken;
            public string Full;

            public Env()
            {
                Db = new AnkenDb(Path.Combine(Dir.Path, "a.db"));
                var c = Db.AddClient("ダミーC");
                Anken = Db.InsertAnken(new AnkenInput { ClientId = c.Id, RequestDate = new DateTime(2026, 10, 5), PartNumber = "DUMMY-001", ReplyDueDate = new DateTime(2026, 10, 12) }, Path.Combine("ダミーC", "x"));
                Full = Path.Combine(Dir.Path, "ws", Anken.FolderPath);
                foreach (var sub in FolderNames.Subfolders)
                {
                    Directory.CreateDirectory(Path.Combine(Full, sub));
                }
            }

            public static InboundMail Mail(string subject = "見積のお願い DUMMY-001")
            {
                var m = new InboundMail { EntryId = "C1", Subject = subject, SenderName = "客先担当", SenderAddress = "client@example.com", ReceivedAt = new DateTime(2026, 10, 5, 10, 0, 0) };
                m.Attachments.Add(new InboundAttachment { Index = 1, FileName = "依頼書.xlsx", Size = 1 });
                m.Attachments.Add(new InboundAttachment { Index = 2, FileName = "図面.pdf", Size = 1 });
                m.Attachments.Add(new InboundAttachment { Index = 3, FileName = "形状.zip", Size = 1 });
                return m;
            }

            public void Dispose()
            {
                Db.Dispose();
                Dir.Dispose();
            }
        }

        [Fact]
        public void Attachments_are_sorted_into_1_and_2_msg_goes_to_1_and_body_becomes_a_note_without_supplier()
        {
            using (var e = new Env())
            using (var inbox = new FakeMailInbox { Body = "図面を添付します。至急お願いします。" })
            {
                var outcome = ClientMailImporter.Import(inbox, e.Db, e.Anken, e.Full, Env.Mail(), new[] { 1, 2, 3 }, true, true, new DateTime(2026, 10, 6, 9, 0, 0));

                Assert.True(File.Exists(Path.Combine(e.Full, FolderNames.Subfolders[0], "依頼書.xlsx")));
                Assert.True(File.Exists(Path.Combine(e.Full, FolderNames.Subfolders[1], "図面.pdf")));
                Assert.True(File.Exists(Path.Combine(e.Full, FolderNames.Subfolders[0], "形状.zip")));
                Assert.NotNull(outcome.MsgPath);
                Assert.Equal("20261005　客先　見積のお願い DUMMY-001.msg", Path.GetFileName(outcome.MsgPath));
                Assert.Empty(outcome.Errors);

                var note = e.Db.ListNotes(e.Anken.Id).Single();
                Assert.Null(note.SupplierId);
                Assert.Contains("至急お願いします", note.Text);

                var log = e.Db.ListMailLog(e.Anken.Id).Single();
                Assert.Equal(MailKind.Client, log.Kind);
                Assert.Null(log.SupplierId);
                Assert.Equal(Path.Combine(e.Anken.FolderPath, FolderNames.Subfolders[0], "20261005　客先　見積のお願い DUMMY-001.msg"), log.MsgPath);
                Assert.Contains("C1", e.Db.ListImportedEntryIds());
                // 調達先の回答にはならない（回答受領日などは触らない）
                Assert.Empty(e.Db.ListAnkenSuppliers(e.Anken.Id));
            }
        }

        [Fact]
        public void Same_names_get_numbers_and_one_failure_does_not_stop_the_rest()
        {
            using (var e = new Env())
            using (var inbox = new FakeMailInbox())
            {
                inbox.FailAttachments.Add(2);
                ClientMailImporter.Import(inbox, e.Db, e.Anken, e.Full, Env.Mail(), new[] { 1 }, false, false, DateTime.Now);
                var second = ClientMailImporter.Import(inbox, e.Db, e.Anken, e.Full, Env.Mail(), new[] { 1, 2 }, false, false, DateTime.Now);
                Assert.True(File.Exists(Path.Combine(e.Full, FolderNames.Subfolders[0], "依頼書 (2).xlsx")));
                Assert.Single(second.Errors);
                Assert.Single(second.SavedFiles);
            }
        }

        [Fact]
        public void Staged_msg_is_copied_and_recorded_when_a_new_anken_is_registered_from_mail()
        {
            using (var e = new Env())
            {
                var staged = Path.Combine(e.Dir.Path, "staged.msg");
                File.WriteAllText(staged, "msg");
                var outcome = ClientMailImporter.RecordStaged(e.Db, e.Anken, e.Full, Env.Mail(), staged, "本文です", DateTime.Now);
                Assert.True(File.Exists(outcome.MsgPath));
                Assert.True(File.Exists(staged)); // 元は動かさない（コピー）
                Assert.Single(e.Db.ListNotes(e.Anken.Id));
                Assert.Equal(MailKind.Client, e.Db.ListMailLog(e.Anken.Id).Single().Kind);
            }
        }

        [Fact]
        public void Nothing_saved_means_no_log()
        {
            using (var e = new Env())
            using (var inbox = new FakeMailInbox())
            {
                var outcome = ClientMailImporter.Import(inbox, e.Db, e.Anken, e.Full, Env.Mail(), new int[0], false, false, DateTime.Now);
                Assert.False(outcome.AnythingSaved);
                Assert.Empty(e.Db.ListMailLog(e.Anken.Id));
            }
        }
    }

    public class TriageTests
    {
        private static InboundMail Mail(string id, string subject, string sender, params string[] files)
        {
            var m = new InboundMail { EntryId = id, Subject = subject, SenderAddress = sender, ReceivedAt = new DateTime(2026, 10, 6, 8, 0, 0).AddMinutes(id.GetHashCode() % 50) };
            var i = 1;
            foreach (var f in files)
            {
                m.Attachments.Add(new InboundAttachment { Index = i++, FileName = f });
            }

            return m;
        }

        [Fact]
        public void Confident_needs_part_number_and_the_suppliers_address_and_skips_imported_closed_and_attachmentless()
        {
            using (var t = new TempDir())
            using (var db = new AnkenDb(Path.Combine(t.Path, "a.db")))
            {
                var c = db.AddClient("ダミーC");
                var s = db.AddSupplier("調達先甲", "甲", "kou@example.com", "");
                var a1 = db.InsertAnken(new AnkenInput { ClientId = c.Id, RequestDate = new DateTime(2026, 10, 1), PartNumber = "DUMMY-001", ReplyDueDate = new DateTime(2026, 10, 9) }, "p1");
                var a2 = db.InsertAnken(new AnkenInput { ClientId = c.Id, RequestDate = new DateTime(2026, 10, 1), PartNumber = "DUMMY-002", ReplyDueDate = new DateTime(2026, 10, 9) }, "p2");
                db.AddSupplierToAnken(a1.Id, s.Id);
                db.AddSupplierToAnken(a2.Id, s.Id);
                db.SetAnkenStatus(a2.Id, AnkenStatus.Lost);

                var mails = new[]
                {
                    Mail("M1", "御見積書 DUMMY-001", "kou@example.com", "見積.pdf", "logo.png", "計算.xlsx"),
                    Mail("M2", "DUMMY-001について", "other@example.com", "x.pdf"),     // 品番だけ → 確かでない
                    Mail("M3", "DUMMY-002 見積", "kou@example.com", "x.pdf"),          // 失注の案件 → 出さない
                    Mail("M4", "御見積書 DUMMY-001", "kou@example.com"),                // 添付なし → 出さない
                    Mail("M5", "御見積書 DUMMY-001", "kou@example.com", "y.pdf"),      // 取り込み済み → 出さない
                    Mail("M6", "全然関係ない件", "kou@example.com", "z.pdf"),          // 候補なし → 出さない
                };
                var plan = InboundTriage.Plan(mails, db.ListAnkens(), db.ListAllAnkenSuppliers(), db.ListSuppliers(), new HashSet<string> { "M5" });

                Assert.Equal(new[] { "M1", "M2" }, plan.Select(p => p.Mail.EntryId).ToArray());
                Assert.True(plan[0].Confident);
                Assert.False(plan[1].Confident);
                Assert.Equal(new[] { 1, 3 }, plan[0].AttachmentIndexes.ToArray()); // PDFとExcelだけ。画像は除く
                Assert.Equal(a1.Id, plan[0].Candidate.Anken.Id);
                Assert.Equal(s.Id, plan[0].Candidate.Supplier!.SupplierId);
            }
        }

        [Fact]
        public void If_there_is_no_pdf_or_excel_all_attachments_are_default()
        {
            using (var t = new TempDir())
            using (var db = new AnkenDb(Path.Combine(t.Path, "a.db")))
            {
                var c = db.AddClient("ダミーC");
                var s = db.AddSupplier("調達先甲", "甲", "kou@example.com", "");
                var a = db.InsertAnken(new AnkenInput { ClientId = c.Id, RequestDate = new DateTime(2026, 10, 1), PartNumber = "DUMMY-001", ReplyDueDate = new DateTime(2026, 10, 9) }, "p1");
                db.AddSupplierToAnken(a.Id, s.Id);
                var plan = InboundTriage.Plan(new[] { Mail("M1", "DUMMY-001", "kou@example.com", "a.zip", "b.dwg") }, db.ListAnkens(), db.ListAllAnkenSuppliers(), db.ListSuppliers(), new HashSet<string>());
                Assert.Equal(new[] { 1, 2 }, plan.Single().AttachmentIndexes.ToArray());
            }
        }
    }

    public class FileRoutingTests
    {
        [Fact]
        public void CopyToSubfolder_keeps_names_numbers_duplicates_and_checks_index()
        {
            using (var t = new TempDir())
            {
                var src = Path.Combine(t.Path, "a.txt");
                File.WriteAllText(src, "x");
                var anken = Path.Combine(t.Path, "anken");
                var r1 = RequestFiles.CopyToSubfolder(anken, 8, new[] { src });
                var r2 = RequestFiles.CopyToSubfolder(anken, 8, new[] { src, Path.Combine(t.Path, "無い.txt") });
                Assert.True(File.Exists(Path.Combine(anken, FolderNames.Subfolders[8], "a.txt")));
                Assert.EndsWith("a (2).txt", r2[0].Destination);
                Assert.Null(r2[1].Destination);
                Assert.NotNull(r1[0].Destination);
                Assert.Throws<ArgumentOutOfRangeException>(() => RequestFiles.CopyToSubfolder(anken, 15, new[] { src }));
            }
        }

        [Fact]
        public void Supplier_is_guessed_only_when_exactly_one_matches()
        {
            var a = new AnkenSupplier { SupplierId = 1, SupplierName = "ダミー甲工業", ShortName = "甲" };
            var b = new AnkenSupplier { SupplierId = 2, SupplierName = "ダミー乙製作所", ShortName = "乙" };
            var both = new[] { a, b };
            Assert.Equal(1, QuoteFiles.GuessSupplier("甲_見積書.pdf", both)!.SupplierId);
            Assert.Equal(2, QuoteFiles.GuessSupplier("ダミー乙製作所 御見積.pdf", both)!.SupplierId);
            Assert.Null(QuoteFiles.GuessSupplier("甲乙まとめ.pdf", both));
            Assert.Null(QuoteFiles.GuessSupplier("見積書.pdf", both));
        }
    }

    public class CalendarAndSearchTests
    {
        private sealed class FakeCalendar : ICalendarGateway
        {
            public readonly Dictionary<string, string> Items = new Dictionary<string, string>();
            public int Next = 1;
            public string? LastBody;
            public int LastReminder;

            public string AddOrUpdateAllDay(string? existingEntryId, string subject, DateTime date, string body, int reminderMinutesBeforeStart)
            {
                LastBody = body;
                LastReminder = reminderMinutesBeforeStart;
                var id = existingEntryId != null && Items.ContainsKey(existingEntryId) ? existingEntryId : "ENT" + Next++;
                Items[id] = subject + "|" + date.ToString("yyyyMMdd");
                return id;
            }

            public void Delete(string entryId)
            {
                Items.Remove(entryId);
            }

            public void Dispose()
            {
            }
        }

        [Fact]
        public void Deadline_event_is_created_updated_in_place_and_removed()
        {
            using (var t = new TempDir())
            using (var db = new AnkenDb(Path.Combine(t.Path, "a.db")))
            using (var cal = new FakeCalendar())
            {
                var c = db.AddClient("ダミーC");
                var s = db.AddSupplier("調達先甲", "甲", "", "");
                var a = db.InsertAnken(new AnkenInput { ClientId = c.Id, RequestDate = new DateTime(2026, 10, 1), PartNumber = "DUMMY-001", PartName = "ブラケット", ReplyDueDate = new DateTime(2026, 10, 9) }, "p1");
                db.AddSupplierToAnken(a.Id, s.Id);

                var id = DeadlineCalendar.Sync(cal, db, db.GetAnken(a.Id)!);
                Assert.Equal(id, db.GetAnken(a.Id)!.CalendarEntryId);
                Assert.Equal("【回答期限】20261001　DUMMY-001　ブラケット|20261009", cal.Items[id]);
                Assert.Equal(15 * 60, cal.LastReminder);
                Assert.Contains("未回答: 甲", cal.LastBody);

                // 期限を変えて同期 → 同じ予定を更新（増えない）
                db.UpdateAnkenDetails(a.Id, "ブラケット", new DateTime(2026, 10, 15), db.GetItems(a.Id).ToDictionary(k => k.Key, k => k.Value));
                var id2 = DeadlineCalendar.Sync(cal, db, db.GetAnken(a.Id)!);
                Assert.Equal(id, id2);
                Assert.Single(cal.Items);
                Assert.EndsWith("|20261015", cal.Items[id]);

                DeadlineCalendar.Remove(cal, db, db.GetAnken(a.Id)!);
                Assert.Empty(cal.Items);
                Assert.Null(db.GetAnken(a.Id)!.CalendarEntryId);
                DeadlineCalendar.Remove(cal, db, db.GetAnken(a.Id)!); // 予定が無くても何も起きない
            }
        }

        [Fact]
        public void Global_search_finds_anken_notes_mails_answers_and_files_with_all_tokens()
        {
            using (var t = new TempDir())
            using (var db = new AnkenDb(Path.Combine(t.Path, "a.db")))
            {
                var ws = Path.Combine(t.Path, "ws");
                var c = db.AddClient("ダミーC");
                var s = db.AddSupplier("調達先甲", "甲", "", "");
                var a = db.InsertAnken(new AnkenInput { ClientId = c.Id, RequestDate = new DateTime(2026, 10, 1), PartNumber = "DUMMY-001", ReplyDueDate = new DateTime(2026, 10, 9) }, Path.Combine("ダミーC", "20261001　DUMMY-001"));
                var b = db.InsertAnken(new AnkenInput { ClientId = c.Id, RequestDate = new DateTime(2026, 9, 1), PartNumber = "OTHER-777", ReplyDueDate = new DateTime(2026, 9, 9) }, Path.Combine("ダミーC", "20260901　OTHER-777"));
                db.AddSupplierToAnken(a.Id, s.Id);
                var ans = db.ListAnkenSuppliers(a.Id).Single();
                ans.Note = "表面処理は黒染めで対応可能";
                db.SaveSupplierAnswer(ans, new Quote[0]);
                db.AddNote(a.Id, null, "客先が黒染めを希望している");
                db.AddMailLog(new MailLogEntry { AnkenId = a.Id, Kind = MailKind.Client, ToAddress = "c@example.com", Subject = "黒染めの仕様確認", Status = "取り込み済み", CreatedAt = DateTime.Now });
                var dir = Path.Combine(ws, b.FolderPath, FolderNames.Subfolders[4]);
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "甲　黒染め見積.pdf"), "x");

                var hits = GlobalSearch.Run(db, ws, "黒染め");
                Assert.Equal(new[] { "メモ", "メール", "回答" }, hits.Where(h => h.Anken.Id == a.Id).Select(h => h.Kind).ToArray());
                Assert.Equal("ファイル", hits.Single(h => h.Anken.Id == b.Id).Kind);
                Assert.NotNull(hits.Single(h => h.Anken.Id == b.Id).Path);

                Assert.Single(GlobalSearch.Run(db, ws, "dummy-001 ダミー"), h => h.Kind == "案件");
                Assert.Empty(GlobalSearch.Run(db, ws, "黒染め 存在しない語"));
                Assert.Empty(GlobalSearch.Run(db, ws, "   "));
                Assert.Single(GlobalSearch.Run(db, ws, "黒染め", maxHits: 1));
            }
        }

        [Fact]
        public void Real_v6_database_migrates_to_v7()
        {
            using (var t = new TempDir())
            {
                var path = Path.Combine(t.Path, "v6.db");
                using (var db = new AnkenDb(path))
                {
                    var c = db.AddClient("ダミーC");
                    db.InsertAnken(new AnkenInput { ClientId = c.Id, RequestDate = new DateTime(2026, 1, 5), PartNumber = "P1", ReplyDueDate = new DateTime(2026, 1, 9) }, "x");
                }

                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                using (var conn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + path))
                {
                    conn.Open();
                    foreach (var sql in new[] { "ALTER TABLE anken DROP COLUMN calendar_entry_id", "PRAGMA user_version = 6" })
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
                    Assert.Null(a.CalendarEntryId);
                    db.SetCalendarEntryId(a.Id, "ABC");
                    Assert.Equal("ABC", db.GetAnken(a.Id)!.CalendarEntryId);
                }
            }
        }
    }
}
