using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace AnkenDesk.Core.Tests
{
    public class ConversationTests
    {
        private sealed class Env : IDisposable
        {
            public TempDir Dir = new TempDir();
            public AnkenDb Db;
            public AnkenRecord A1, A2;
            public Supplier S1, S2;

            public Env()
            {
                Db = new AnkenDb(Path.Combine(Dir.Path, "a.db"));
                var c = Db.AddClient("ダミーC");
                S1 = Db.AddSupplier("調達先甲", "甲", "kou@example.com", "");
                S2 = Db.AddSupplier("調達先乙", "乙", "otsu@example.com", "");
                A1 = Db.InsertAnken(new AnkenInput { ClientId = c.Id, RequestDate = new DateTime(2026, 10, 1), PartNumber = "DUMMY-001", ReplyDueDate = new DateTime(2026, 10, 9) }, "p1");
                A2 = Db.InsertAnken(new AnkenInput { ClientId = c.Id, RequestDate = new DateTime(2026, 10, 2), PartNumber = "DUMMY-002", ReplyDueDate = new DateTime(2026, 10, 9) }, "p2");
                foreach (var a in new[] { A1, A2 })
                {
                    Db.AddSupplierToAnken(a.Id, S1.Id);
                    Db.AddSupplierToAnken(a.Id, S2.Id);
                }
            }

            public void Log(AnkenRecord a, Supplier s, MailKind kind, string? conv, string status = "送信済み", string? entry = "E")
            {
                Db.AddMailLog(new MailLogEntry { AnkenId = a.Id, SupplierId = s.Id, Kind = kind, ToAddress = s.Email, Subject = "依頼", Status = status, EntryId = entry, ConversationId = conv, CreatedAt = DateTime.Now });
            }

            public void Dispose()
            {
                Db.Dispose();
                Dir.Dispose();
            }
        }

        private static InboundMail Mail(string id, string subject, string sender, string conv, params string[] files)
        {
            var m = new InboundMail { EntryId = id, Subject = subject, SenderAddress = sender, ConversationId = conv, ReceivedAt = new DateTime(2026, 10, 6, 9, 0, 0) };
            var i = 1;
            foreach (var f in files)
            {
                m.Attachments.Add(new InboundAttachment { Index = i++, FileName = f });
            }

            return m;
        }

        [Fact]
        public void Conversation_id_is_recorded_when_sending_and_survives_reading_back()
        {
            using (var e = new Env())
            using (var gw = new FakeMailGateway())
            {
                var dir = Path.Combine(e.Dir.Path, "ws", e.A1.FolderPath);
                Directory.CreateDirectory(dir);
                var req = MailSender.BuildRequest(e.A1, new DateTime(2026, 10, 12), e.S1, MailTemplates.RequestSubject, MailTemplates.RequestBody, new string[0], false);
                MailSender.SendAll(gw, e.Db, e.A1, dir, MailKind.Request, new[] { new KeyValuePair<Supplier, MailRequest>(e.S1, req) }, DateTime.Now);
                var log = e.Db.ListMailLog(e.A1.Id).Single();
                Assert.StartsWith("CONV", log.ConversationId);
                Assert.Equal(e.A1.Id, e.Db.ListConversationLinks()[log.ConversationId!].AnkenId);
            }
        }

        [Fact]
        public void Links_use_sent_requests_and_reminders_only_and_skip_ambiguous_conversations()
        {
            using (var e = new Env())
            {
                e.Log(e.A1, e.S1, MailKind.Request, "C1");
                e.Log(e.A1, e.S1, MailKind.Reminder, "C1b");
                e.Log(e.A2, e.S2, MailKind.Request, "C2");
                e.Log(e.A1, e.S2, MailKind.Request, "SAME");
                e.Log(e.A2, e.S2, MailKind.Request, "SAME");     // 同じ会話が2案件に → 曖昧
                e.Log(e.A1, e.S1, MailKind.Answer, "ANS");        // 回答の記録は、会話の鍵にしない
                e.Log(e.A1, e.S1, MailKind.Request, null);

                var links = e.Db.ListConversationLinks();
                Assert.Equal(e.S1.Id, links["C1"].SupplierId);
                Assert.Equal(e.A1.Id, links["C1b"].AnkenId);
                Assert.Equal(e.A2.Id, links["C2"].AnkenId);
                Assert.False(links.ContainsKey("SAME"));
                Assert.False(links.ContainsKey("ANS"));
            }
        }

        [Fact]
        public void Reply_in_the_same_conversation_is_matched_without_part_number_or_attachments_even_if_closed()
        {
            using (var e = new Env())
            {
                e.Log(e.A1, e.S1, MailKind.Request, "C1");
                e.Db.SetAnkenStatus(e.A1.Id, AnkenStatus.Won);
                var mails = new[]
                {
                    Mail("M1", "Re: 見積検討のお願い", "someone-else@example.com", "C1"),                  // 件名に品番なし・添付なし・別の人 → 会話で結ぶ
                    Mail("M2", "見積書送付", "kou@example.com", "C1", "見積.pdf", "logo.png"),             // 会話＋見積書
                    Mail("M3", "無関係な話", "kou@example.com", "OTHER", "x.pdf"),                         // 会話が違い、品番なし → 出さない
                    Mail("M4", "DUMMY-002 ご確認", "kou@example.com", "NEW", "y.pdf"),                    // 会話なし。品番と差出人 → 確実
                    Mail("M5", "DUMMY-002 ご確認", "x@example.com", "NEW2", "y.pdf"),                     // 品番だけ → 要確認
                };
                var plan = InboundTriage.Plan(mails, e.Db.ListAnkens(), e.Db.ListAllAnkenSuppliers(), e.Db.ListSuppliers(), new HashSet<string>(), e.Db.ListConversationLinks());

                Assert.Equal(new[] { "M1", "M2", "M4", "M5" }.OrderBy(x => x), plan.Select(p => p.Mail.EntryId).OrderBy(x => x));
                var m1 = plan.Single(p => p.Mail.EntryId == "M1");
                Assert.Equal(TriageLevel.Thread, m1.Level);
                Assert.Equal(e.A1.Id, m1.Candidate.Anken.Id);
                Assert.Equal(e.S1.Id, m1.Candidate.Supplier!.SupplierId);
                Assert.False(m1.HasQuoteAttachment);       // 添付なし → 回答受領日の初期値は入れない
                Assert.Empty(m1.AttachmentIndexes);
                var m2 = plan.Single(p => p.Mail.EntryId == "M2");
                Assert.True(m2.HasQuoteAttachment);
                Assert.Equal(new[] { 1 }, m2.AttachmentIndexes.ToArray());
                Assert.Equal(TriageLevel.PartAndSender, plan.Single(p => p.Mail.EntryId == "M4").Level);
                Assert.Equal(TriageLevel.PartOnly, plan.Single(p => p.Mail.EntryId == "M5").Level);
                Assert.False(plan.Single(p => p.Mail.EntryId == "M5").Confident);
                // 並びは、確かな順（会話 → 品番と差出人 → 品番だけ）
                Assert.Equal(TriageLevel.PartOnly, plan.Last().Level);
                Assert.Equal(TriageLevel.Thread, plan[0].Level);
            }
        }

        [Fact]
        public void Conversation_match_needs_the_supplier_to_still_be_on_the_anken_and_skips_imported_mails()
        {
            using (var e = new Env())
            {
                e.Log(e.A1, e.S1, MailKind.Request, "C1");
                e.Db.RemoveSupplierFromAnken(e.A1.Id, e.S1.Id);
                var mails = new[] { Mail("M1", "Re: 依頼", "kou@example.com", "C1"), Mail("M2", "Re: 依頼", "kou@example.com", "C1") };
                var plan = InboundTriage.Plan(mails, e.Db.ListAnkens(), e.Db.ListAllAnkenSuppliers(), e.Db.ListSuppliers(), new HashSet<string>(), e.Db.ListConversationLinks());
                Assert.Empty(plan); // 調達先を外してある → 結べない

                Assert.True(e.Db.RestoreRemovedSupplier(e.Db.ListRemovedSuppliers(e.A1.Id).Single().Id));
                var again = InboundTriage.Plan(mails, e.Db.ListAnkens(), e.Db.ListAllAnkenSuppliers(), e.Db.ListSuppliers(), new HashSet<string> { "M2" }, e.Db.ListConversationLinks());
                Assert.Equal(new[] { "M1" }, again.Select(p => p.Mail.EntryId).ToArray());
            }
        }

        [Fact]
        public void A_mail_moved_to_another_outlook_folder_is_still_recognised_as_imported()
        {
            using (var e = new Env())
            {
                var received = new DateTime(2026, 10, 6, 9, 30, 15);
                e.Db.AddMailLog(new MailLogEntry
                {
                    AnkenId = e.A1.Id, SupplierId = e.S1.Id, Kind = MailKind.Answer, ToAddress = "kou@example.com", Subject = "御見積書 DUMMY-001",
                    Status = "取り込み済み", EntryId = "OLD-ENTRY", SentAt = received, CreatedAt = DateTime.Now,
                });
                var imported = e.Db.ListImportedEntryIds();
                Assert.Contains("OLD-ENTRY", imported);

                // 移動してEntryIDが変わっても、件名と受信日時（分まで）で同じメールと分かる
                var moved = new InboundMail { EntryId = "NEW-ENTRY", Subject = "御見積書 DUMMY-001", SenderAddress = "kou@example.com", ReceivedAt = received.AddSeconds(20) };
                moved.Attachments.Add(new InboundAttachment { Index = 1, FileName = "見積.pdf" });
                var other = new InboundMail { EntryId = "OTHER", Subject = "御見積書 DUMMY-001", SenderAddress = "kou@example.com", ReceivedAt = received.AddMinutes(5) };
                other.Attachments.Add(new InboundAttachment { Index = 1, FileName = "見積2.pdf" });
                var plan = InboundTriage.Plan(new[] { moved, other }, e.Db.ListAnkens(), e.Db.ListAllAnkenSuppliers(), e.Db.ListSuppliers(), imported);
                Assert.Equal(new[] { "OTHER" }, plan.Select(p => p.Mail.EntryId).ToArray());
            }
        }

        [Fact]
        public void Backfill_fills_only_missing_ids_and_counts_unavailable_ones()
        {
            using (var e = new Env())
            using (var inbox = new FakeMailInbox())
            {
                e.Log(e.A1, e.S1, MailKind.Request, null, entry: "E1");
                e.Log(e.A1, e.S2, MailKind.Request, null, entry: "E2");          // Outlookに無い
                e.Log(e.A2, e.S1, MailKind.Request, "ALREADY", entry: "E3");     // もう持っている
                e.Log(e.A2, e.S2, MailKind.Request, null, status: "送信トレイに残っている", entry: null); // 送信できていない
                inbox.Conversations["E1"] = "FILLED";

                Assert.Equal(2, e.Db.ListMailsMissingConversation().Count);
                var r = ConversationBackfill.Run(inbox, e.Db);
                Assert.Equal(1, r.Updated);
                Assert.Equal(1, r.Missing);
                Assert.Equal(e.A1.Id, e.Db.ListConversationLinks()["FILLED"].AnkenId);
                Assert.Equal(1, ConversationBackfill.Run(inbox, e.Db).Missing); // 2回目は、取れないものだけが残る
            }
        }

        [Fact]
        public void Real_v7_database_migrates_to_v8_keeping_mail_log()
        {
            using (var t = new TempDir())
            {
                var path = Path.Combine(t.Path, "v7.db");
                long ankenId;
                using (var db = new AnkenDb(path))
                {
                    var c = db.AddClient("ダミーC");
                    var a = db.InsertAnken(new AnkenInput { ClientId = c.Id, RequestDate = new DateTime(2026, 1, 5), PartNumber = "P1", ReplyDueDate = new DateTime(2026, 1, 9) }, "x");
                    ankenId = a.Id;
                    db.AddMailLog(new MailLogEntry { AnkenId = a.Id, Kind = MailKind.Request, ToAddress = "a@example.com", Subject = "依頼", Status = "送信済み", EntryId = "E1", CreatedAt = DateTime.Now });
                }

                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                using (var conn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + path))
                {
                    conn.Open();
                    foreach (var sql in new[] { "DROP INDEX idx_mail_log_conversation", "ALTER TABLE mail_log DROP COLUMN conversation_id", "PRAGMA user_version = 7" })
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
                    var log = db.ListMailLog(ankenId).Single();
                    Assert.Equal("E1", log.EntryId);
                    Assert.Null(log.ConversationId);
                    Assert.Single(db.ListMailsMissingConversation());
                }
            }
        }
    }
}
