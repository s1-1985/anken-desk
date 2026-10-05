using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AnkenDesk.Core;
using Xunit;

namespace AnkenDesk.Core.Tests
{
    public class InboundTests
    {
        private static readonly DateTime Now = new DateTime(2026, 10, 6, 9, 0, 0);

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
                var input = new AnkenInput { ClientId = client.Id, RequestDate = new DateTime(2026, 10, 5), PartNumber = "TEST-001", ReplyDueDate = new DateTime(2026, 10, 12) };
                Anken = Db.InsertAnken(input, Path.Combine("得意先A", "20261005　TEST-001"));
                AnkenFull = Path.Combine(Dir.Path, "ws", Anken.FolderPath);
                foreach (var sub in FolderNames.Subfolders)
                {
                    Directory.CreateDirectory(Path.Combine(AnkenFull, sub));
                }

                A = Db.AddSupplier("調達先甲", "甲", "kou@example.com;kou2@example.com", "");
                B = Db.AddSupplier("調達先乙", "乙", "otsu@example.com", "");
                Db.AddSupplierToAnken(Anken.Id, A.Id);
                Db.AddSupplierToAnken(Anken.Id, B.Id);
            }

            public static InboundMail Mail(string subject = "御見積書【TEST-001】", string sender = "kou@example.com")
            {
                var m = new InboundMail { EntryId = "E1", Subject = subject, SenderName = "甲の担当", SenderAddress = sender, ReceivedAt = new DateTime(2026, 10, 6, 8, 30, 0) };
                m.Attachments.Add(new InboundAttachment { Index = 1, FileName = "見積書.pdf", Size = 1000 });
                m.Attachments.Add(new InboundAttachment { Index = 2, FileName = "図面.pdf", Size = 2000 });
                return m;
            }

            public void Dispose()
            {
                Db.Dispose();
                Dir.Dispose();
            }
        }

        // ---- 取り込み ----

        [Fact]
        public void 添付を略称つきの名前で5番のフォルダへ保存しmsgをメールのフォルダへ保存する()
        {
            using (var e = new Env())
            using (var inbox = new FakeMailInbox())
            {
                var mail = Env.Mail();

                var outcome = InboundImporter.Import(inbox, e.Db, e.Anken, e.AnkenFull, e.A, mail, new[] { 1, 2 }, true, null, Now);

                var quoteDir = Path.Combine(e.AnkenFull, FolderNames.Subfolders[4]);
                Assert.Equal(new[] { Path.Combine(quoteDir, "甲　見積書.pdf"), Path.Combine(quoteDir, "甲　図面.pdf") }, outcome.SavedFiles.ToArray());
                Assert.True(File.Exists(Path.Combine(quoteDir, "甲　見積書.pdf")));
                Assert.Equal(Path.Combine(quoteDir, "メール", "20261006　甲　御見積書【TEST-001】.msg"), outcome.MsgPath);
                Assert.Empty(outcome.Errors);

                // 5番の直下にはPDFだけ（.msgは別のフォルダ）。画面の見積書の一覧に.msgが混ざらない。
                Assert.Equal(2, QuoteFiles.ListCurrent(e.AnkenFull, "甲").Count);
            }
        }

        [Fact]
        public void 選んだ添付だけ保存する()
        {
            using (var e = new Env())
            using (var inbox = new FakeMailInbox())
            {
                var outcome = InboundImporter.Import(inbox, e.Db, e.Anken, e.AnkenFull, e.A, Env.Mail(), new[] { 2 }, false, null, Now);

                Assert.EndsWith("甲　図面.pdf", Assert.Single(outcome.SavedFiles));
                Assert.Null(outcome.MsgPath);
            }
        }

        [Fact]
        public void 同名があれば連番で上書きしない()
        {
            using (var e = new Env())
            using (var inbox = new FakeMailInbox())
            {
                InboundImporter.Import(inbox, e.Db, e.Anken, e.AnkenFull, e.A, Env.Mail(), new[] { 1 }, false, null, Now);
                var second = InboundImporter.Import(inbox, e.Db, e.Anken, e.AnkenFull, e.A, Env.Mail(), new[] { 1 }, false, null, Now);

                Assert.EndsWith("甲　見積書 (2).pdf", Assert.Single(second.SavedFiles));
                Assert.Equal(2, QuoteFiles.ListCurrent(e.AnkenFull, "甲").Count);
            }
        }

        [Fact]
        public void 記録に残し回答受領日を入れる()
        {
            using (var e = new Env())
            using (var inbox = new FakeMailInbox())
            {
                var outcome = InboundImporter.Import(inbox, e.Db, e.Anken, e.AnkenFull, e.A, Env.Mail(), new[] { 1 }, true, new DateTime(2026, 10, 6), Now);

                Assert.True(outcome.ReceivedDateMarked);
                var a = e.Db.ListAnkenSuppliers(e.Anken.Id).Single(s => s.SupplierId == e.A.Id);
                Assert.Equal(new DateTime(2026, 10, 6), a.ReceivedAt);
                Assert.True(a.IsAnswered);
                Assert.False(e.Db.ListAnkenSuppliers(e.Anken.Id).Single(s => s.SupplierId == e.B.Id).IsAnswered);

                var log = Assert.Single(e.Db.ListMailLog(e.Anken.Id));
                Assert.Equal(MailKind.Answer, log.Kind);
                Assert.Equal("取り込み済み", log.Status);
                Assert.Equal("kou@example.com", log.ToAddress);
                Assert.Equal(new DateTime(2026, 10, 6, 8, 30, 0), log.SentAt);
                Assert.NotNull(log.MsgPath);
            }
        }

        [Fact]
        public void 回答受領日を入れない指定なら入れない()
        {
            using (var e = new Env())
            using (var inbox = new FakeMailInbox())
            {
                var outcome = InboundImporter.Import(inbox, e.Db, e.Anken, e.AnkenFull, e.A, Env.Mail(), new[] { 1 }, false, null, Now);

                Assert.False(outcome.ReceivedDateMarked);
                Assert.False(e.Db.ListAnkenSuppliers(e.Anken.Id).Single(s => s.SupplierId == e.A.Id).IsAnswered);
            }
        }

        [Fact]
        public void ひとつの保存に失敗しても残りを保存し失敗は一覧に残る()
        {
            using (var e = new Env())
            using (var inbox = new FakeMailInbox())
            {
                inbox.FailAttachments.Add(1);

                var outcome = InboundImporter.Import(inbox, e.Db, e.Anken, e.AnkenFull, e.A, Env.Mail(), new[] { 1, 2 }, false, new DateTime(2026, 10, 6), Now);

                Assert.EndsWith("甲　図面.pdf", Assert.Single(outcome.SavedFiles));
                var err = Assert.Single(outcome.Errors);
                Assert.Contains("見積書.pdf", err);
                Assert.Equal("一部だけ取り込み済み", Assert.Single(e.Db.ListMailLog(e.Anken.Id)).Status);
            }
        }

        [Fact]
        public void 何も保存できなければ記録も回答受領日も入れない()
        {
            using (var e = new Env())
            using (var inbox = new FakeMailInbox())
            {
                inbox.FailAttachments.Add(1);
                inbox.FailMsg = true;

                var outcome = InboundImporter.Import(inbox, e.Db, e.Anken, e.AnkenFull, e.A, Env.Mail(), new[] { 1 }, true, new DateTime(2026, 10, 6), Now);

                Assert.False(outcome.AnythingSaved);
                Assert.Equal(2, outcome.Errors.Count);
                Assert.False(outcome.ReceivedDateMarked);
                Assert.Empty(e.Db.ListMailLog(e.Anken.Id));
                Assert.False(e.Db.ListAnkenSuppliers(e.Anken.Id).Single(s => s.SupplierId == e.A.Id).IsAnswered);
            }
        }

        [Fact]
        public void 存在しない添付の番号はエラーにする()
        {
            using (var e = new Env())
            using (var inbox = new FakeMailInbox())
            {
                var outcome = InboundImporter.Import(inbox, e.Db, e.Anken, e.AnkenFull, e.A, Env.Mail(), new[] { 9 }, false, null, Now);

                Assert.Contains("番号9", Assert.Single(outcome.Errors));
            }
        }

        [Fact]
        public void 案件フォルダが無ければ例外()
        {
            using (var e = new Env())
            using (var inbox = new FakeMailInbox())
            {
                Assert.Throws<DirectoryNotFoundException>(() =>
                    InboundImporter.Import(inbox, e.Db, e.Anken, Path.Combine(e.Dir.Path, "ない"), e.A, Env.Mail(), new[] { 1 }, false, null, Now));
            }
        }

        // ---- 候補の推定 ----

        private static IReadOnlyDictionary<long, IReadOnlyList<AnkenSupplier>> Map(Env e)
        {
            return e.Db.ListAllAnkenSuppliers();
        }

        [Fact]
        public void 件名の品番と差出人のアドレスから案件と調達先を推定する()
        {
            using (var e = new Env())
            {
                var c = Assert.Single(InboundMatcher.Suggest(Env.Mail(), e.Db.ListAnkens(), Map(e), e.Db.ListSuppliers()));

                Assert.Equal(e.Anken.Id, c.Anken.Id);
                Assert.Equal(e.A.Id, c.Supplier!.SupplierId);
                Assert.Equal(121, c.Score);
            }
        }

        [Fact]
        public void 品番が件名に無くても差出人が調達先なら候補にする()
        {
            using (var e = new Env())
            {
                var c = Assert.Single(InboundMatcher.Suggest(Env.Mail("お世話になっております"), e.Db.ListAnkens(), Map(e), e.Db.ListSuppliers()));

                Assert.Equal(21, c.Score);
                Assert.Equal(e.A.Id, c.Supplier!.SupplierId);
            }
        }

        [Fact]
        public void 宛先が複数のアドレスのどれかでも一致し_大文字小文字は区別しない()
        {
            using (var e = new Env())
            {
                var c = Assert.Single(InboundMatcher.Suggest(Env.Mail("件名", "KOU2@Example.com"), e.Db.ListAnkens(), Map(e), e.Db.ListSuppliers()));

                Assert.Equal(e.A.Id, c.Supplier!.SupplierId);
            }
        }

        [Fact]
        public void 品番だけ合って差出人が知らない人なら調達先は空で案件だけ候補にする()
        {
            using (var e = new Env())
            {
                var c = Assert.Single(InboundMatcher.Suggest(Env.Mail("【TEST-001】", "unknown@example.com"), e.Db.ListAnkens(), Map(e), e.Db.ListSuppliers()));

                Assert.Equal(100, c.Score);
                Assert.Null(c.Supplier);
            }
        }

        [Fact]
        public void 何も合わなければ候補なし()
        {
            using (var e = new Env())
            {
                Assert.Empty(InboundMatcher.Suggest(Env.Mail("別の件", "unknown@example.com"), e.Db.ListAnkens(), Map(e), e.Db.ListSuppliers()));
            }
        }

        [Fact]
        public void 品番が短すぎる案件は件名の部分一致で候補にしない()
        {
            using (var e = new Env())
            {
                var client = e.Db.AddClient("得意先B");
                e.Db.InsertAnken(new AnkenInput { ClientId = client.Id, RequestDate = new DateTime(2026, 10, 5), PartNumber = "A1", ReplyDueDate = new DateTime(2026, 10, 12) }, "y");

                var list = InboundMatcher.Suggest(Env.Mail("A1 の件", "unknown@example.com"), e.Db.ListAnkens(), Map(e), e.Db.ListSuppliers());

                Assert.Empty(list);
            }
        }

        [Fact]
        public void 候補は点数の高い順で同点は回答期限の近い順()
        {
            using (var e = new Env())
            {
                var client = e.Db.AddClient("得意先B");
                var near = e.Db.InsertAnken(new AnkenInput { ClientId = client.Id, RequestDate = new DateTime(2026, 10, 5), PartNumber = "TEST-001", ReplyDueDate = new DateTime(2026, 10, 8) }, "y");

                var list = InboundMatcher.Suggest(Env.Mail(sender: "unknown@example.com"), e.Db.ListAnkens(), Map(e), e.Db.ListSuppliers());

                Assert.Equal(new[] { near.Id, e.Anken.Id }, list.Select(c => c.Anken.Id).ToArray());
            }
        }
    }
}
