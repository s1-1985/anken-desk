using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AnkenDesk.Core;
using Xunit;

namespace AnkenDesk.Core.Tests
{
    public class MailTests
    {
        private static readonly DateTime Now = new DateTime(2026, 10, 5, 14, 12, 0);

        private sealed class Env : IDisposable
        {
            public TempDir Dir = new TempDir();
            public AnkenDb Db;
            public AnkenRecord Anken;
            public string AnkenFull;
            public Supplier A;
            public Supplier B;

            public Env()
            {
                Db = new AnkenDb(Path.Combine(Dir.Path, "a.db"));
                var client = Db.AddClient("得意先A");
                var input = new AnkenInput { ClientId = client.Id, RequestDate = new DateTime(2026, 10, 5), PartNumber = "TEST-001", PartName = "ダミー部品", ReplyDueDate = new DateTime(2026, 10, 12) };
                Anken = Db.InsertAnken(input, Path.Combine("得意先A", "20261005　TEST-001"));
                AnkenFull = Path.Combine(Dir.Path, "ws", Anken.FolderPath);
                foreach (var sub in FolderNames.Subfolders)
                {
                    Directory.CreateDirectory(Path.Combine(AnkenFull, sub));
                }

                A = Db.AddSupplier("調達先甲", "甲", "kou@example.com", "");
                B = Db.AddSupplier("調達先乙", "乙", "otsu@example.com", "");
                Db.AddSupplierToAnken(Anken.Id, A.Id);
                Db.AddSupplierToAnken(Anken.Id, B.Id);
            }

            public KeyValuePair<Supplier, MailRequest> Item(Supplier s, params string[] attachments)
            {
                return new KeyValuePair<Supplier, MailRequest>(s,
                    MailSender.BuildRequest(Anken, new DateTime(2026, 10, 12), s, MailTemplates.RequestSubject, MailTemplates.RequestBody, attachments, true));
            }

            public void Dispose()
            {
                Db.Dispose();
                Dir.Dispose();
            }
        }

        // ---- 雛形 ----

        [Fact]
        public void 件名と本文に調達先名と品番と回答希望日が入る()
        {
            using (var e = new Env())
            {
                var req = MailSender.BuildRequest(e.Anken, new DateTime(2026, 10, 12), e.A, MailTemplates.RequestSubject, MailTemplates.RequestBody, new string[0], false);

                Assert.Equal("見積検討のお願い（TEST-001）", req.Subject);
                Assert.StartsWith("調達先甲 御中", req.Body);
                Assert.Contains("品番: TEST-001", req.Body);
                Assert.Contains("品名: ダミー部品", req.Body);
                Assert.Contains("回答希望日: 2026年10月12日(月)", req.Body);
                Assert.Equal("kou@example.com", req.ToAddress);
            }
        }

        [Fact]
        public void 品名が空なら品名の行を消す()
        {
            var anken = new AnkenRecord { PartNumber = "P1", PartName = "" };
            var s = new Supplier { Name = "甲" };

            var body = MailTemplates.Apply(MailTemplates.RequestBody, s, anken, new DateTime(2026, 10, 12));

            Assert.DoesNotContain("品名", body);
            Assert.Contains("品番: P1", body);
        }

        [Fact]
        public void 催促の雛形は依頼と別の文面()
        {
            Assert.NotEqual(MailTemplates.RequestSubject, MailTemplates.ReminderSubject);
            Assert.Contains("ご回答の状況", MailTemplates.ReminderBody);
            Assert.Equal(MailTemplates.ReminderSubject, MailTemplates.DefaultSubject(MailKind.Reminder));
        }

        [Fact]
        public void 雛形は設定で変えられ変えなければ既定の文章()
        {
            using (var e = new Env())
            {
                string subject, body;
                e.Db.GetMailTemplate(MailKind.Request, out subject, out body);
                Assert.Equal(MailTemplates.RequestSubject, subject);

                e.Db.SetMailTemplate(MailKind.Request, "別の件名（{品番}）", "別の本文");
                e.Db.GetMailTemplate(MailKind.Request, out subject, out body);
                Assert.Equal("別の件名（{品番}）", subject);
                Assert.Equal("別の本文", body);

                e.Db.GetMailTemplate(MailKind.Reminder, out subject, out body);
                Assert.Equal(MailTemplates.ReminderSubject, subject);
            }
        }

        // ---- 宛先 ----

        [Theory]
        [InlineData("a@example.com", true)]
        [InlineData("a@example.com;b@example.co.jp", true)]
        [InlineData(" a@example.com ; b@example.com ", true)]
        [InlineData("", false)]
        [InlineData("   ", false)]
        [InlineData("a@", false)]
        [InlineData("@example.com", false)]
        [InlineData("a@@example.com", false)]
        [InlineData("a b@example.com", false)]
        [InlineData("a@example", false)]
        [InlineData("a@example.com;bad", false)]
        public void 宛先の形を確かめる(string text, bool expected)
        {
            Assert.Equal(expected, MailSender.IsValidAddressList(text));
        }

        // ---- 送信の流れ ----

        [Fact]
        public void 調達先ごとに別のメールで送り_msgを保存し記録して依頼送付日を入れる()
        {
            using (var e = new Env())
            using (var gw = new FakeMailGateway())
            {
                var outcomes = MailSender.SendAll(gw, e.Db, e.Anken, e.AnkenFull, MailKind.Request,
                    new[] { e.Item(e.A, "添付1.xlsx"), e.Item(e.B, "添付1.xlsx") }, Now);

                Assert.Equal(2, outcomes.Count);
                Assert.All(outcomes, o => Assert.Equal(MailOutcome.Sent, o.Result.Outcome));
                Assert.Equal(new[] { "kou@example.com", "otsu@example.com" }, gw.Sent.Select(r => r.ToAddress).ToArray());
                Assert.All(gw.Sent, r => Assert.Single(r.Attachments));

                var msgs = Directory.GetFiles(Path.Combine(e.AnkenFull, FolderNames.Subfolders[3]), "*.msg").Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal).ToArray();
                Assert.Equal(new[] { "20261005　乙　見積検討のお願い（TEST-001）.msg", "20261005　甲　見積検討のお願い（TEST-001）.msg" }, msgs);

                var log = e.Db.ListMailLog(e.Anken.Id);
                Assert.Equal(2, log.Count);
                Assert.All(log, l => { Assert.Equal("送信済み", l.Status); Assert.Equal(MailKind.Request, l.Kind); Assert.NotNull(l.MsgPath); });

                var sups = e.Db.ListAnkenSuppliers(e.Anken.Id);
                Assert.All(sups, s => Assert.Equal(new DateTime(2026, 10, 5), s.SentAt));
            }
        }

        [Fact]
        public void 送信トレイに残ったら止めて残りは送らず依頼送付日も入れない()
        {
            using (var e = new Env())
            using (var gw = new FakeMailGateway())
            {
                gw.Outcomes[0] = MailOutcome.Queued;

                var outcomes = MailSender.SendAll(gw, e.Db, e.Anken, e.AnkenFull, MailKind.Request, new[] { e.Item(e.A), e.Item(e.B) }, Now);

                Assert.Single(outcomes);
                Assert.Equal(MailOutcome.Queued, outcomes[0].Result.Outcome);
                Assert.Single(gw.Sent);
                var log = Assert.Single(e.Db.ListMailLog(e.Anken.Id));
                Assert.Equal("送信トレイに残っている", log.Status);
                Assert.Null(log.MsgPath);
                Assert.All(e.Db.ListAnkenSuppliers(e.Anken.Id), s => Assert.Null(s.SentAt));
            }
        }

        [Fact]
        public void 送信が例外でも止めて失敗として記録する()
        {
            using (var e = new Env())
            using (var gw = new FakeMailGateway())
            {
                gw.Throws[0] = new InvalidOperationException("Outlookが見つかりません");

                var outcomes = MailSender.SendAll(gw, e.Db, e.Anken, e.AnkenFull, MailKind.Request, new[] { e.Item(e.A), e.Item(e.B) }, Now);

                var o = Assert.Single(outcomes);
                Assert.Equal(MailOutcome.Failed, o.Result.Outcome);
                Assert.Contains("Outlookが見つかりません", o.Result.Message);
                Assert.Equal("失敗", Assert.Single(e.Db.ListMailLog(e.Anken.Id)).Status);
            }
        }

        [Fact]
        public void 催促は依頼送付日を変えない()
        {
            using (var e = new Env())
            using (var gw = new FakeMailGateway())
            {
                e.Db.MarkRequestSent(e.Anken.Id, e.A.Id, new DateTime(2026, 9, 29));

                MailSender.SendAll(gw, e.Db, e.Anken, e.AnkenFull, MailKind.Reminder, new[] { e.Item(e.A) }, Now);

                var a = e.Db.ListAnkenSuppliers(e.Anken.Id).Single(s => s.SupplierId == e.A.Id);
                Assert.Equal(new DateTime(2026, 9, 29), a.SentAt);
                Assert.Equal(MailKind.Reminder, Assert.Single(e.Db.ListMailLog(e.Anken.Id)).Kind);
            }
        }

        [Fact]
        public void 同じ名前のmsgがあれば連番にして上書きしない()
        {
            using (var e = new Env())
            using (var gw = new FakeMailGateway())
            {
                MailSender.SendAll(gw, e.Db, e.Anken, e.AnkenFull, MailKind.Request, new[] { e.Item(e.A) }, Now);
                MailSender.SendAll(gw, e.Db, e.Anken, e.AnkenFull, MailKind.Request, new[] { e.Item(e.A) }, Now);

                var msgs = Directory.GetFiles(Path.Combine(e.AnkenFull, FolderNames.Subfolders[3]), "*.msg");
                Assert.Equal(2, msgs.Length);
            }
        }

        [Fact]
        public void 記録は新しい順で件名は件名の使えない文字を置き換えたmsg名になる()
        {
            using (var e = new Env())
            using (var gw = new FakeMailGateway())
            {
                var item = new KeyValuePair<Supplier, MailRequest>(e.A,
                    MailSender.BuildRequest(e.Anken, new DateTime(2026, 10, 12), e.A, "件名:A/B", "本文", new string[0], false));

                MailSender.SendAll(gw, e.Db, e.Anken, e.AnkenFull, MailKind.Request, new[] { item }, Now);

                var msg = Directory.GetFiles(Path.Combine(e.AnkenFull, FolderNames.Subfolders[3]), "*.msg").Single();
                Assert.EndsWith("件名_A_B.msg", msg);
            }
        }
    }
}
