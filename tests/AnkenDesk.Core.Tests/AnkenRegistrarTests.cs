using System;
using System.IO;
using System.Linq;
using AnkenDesk.Core;
using Xunit;

namespace AnkenDesk.Core.Tests
{
    public class AnkenRegistrarTests
    {
        private static AnkenInput Input(long clientId, string part = "TEST-001", string note = "")
        {
            var input = new AnkenInput
            {
                ClientId = clientId,
                RequestDate = new DateTime(2026, 10, 5),
                PartNumber = part,
                Note = note,
                ReplyDueDate = new DateTime(2026, 10, 12),
            };
            input.Quantities.Add(new QuantityPattern { Kind = "試作", Quantity = 3, Unit = "個" });
            return input;
        }

        [Fact]
        public void 登録すると案件フォルダと15個のサブフォルダができてDBに入る()
        {
            using (var dir = new TempDir())
            using (var db = new AnkenDb(Path.Combine(dir.Path, "a.db")))
            {
                var root = Path.Combine(dir.Path, "ws");
                Directory.CreateDirectory(root);
                var client = db.AddClient("得意先A\u3000試作");
                var reg = new AnkenRegistrar(db, root);

                var rec = reg.Register(Input(client.Id, "TEST-001", "Lot5"));

                var full = Path.Combine(root, "得意先A\u3000試作", "20261005\u3000TEST-001\u3000Lot5");
                Assert.True(Directory.Exists(full));
                var subs = Directory.GetDirectories(full).Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal).ToArray();
                Assert.Equal(FolderNames.Subfolders.OrderBy(x => x, StringComparer.Ordinal).ToArray(), subs);
                Assert.Equal(Path.Combine("得意先A\u3000試作", "20261005\u3000TEST-001\u3000Lot5"), rec.FolderPath);
                Assert.Single(db.ListAnkens());
            }
        }

        [Fact]
        public void 同名のフォルダがあれば何も作らず例外()
        {
            using (var dir = new TempDir())
            using (var db = new AnkenDb(Path.Combine(dir.Path, "a.db")))
            {
                var root = Path.Combine(dir.Path, "ws");
                Directory.CreateDirectory(root);
                var client = db.AddClient("A");
                var reg = new AnkenRegistrar(db, root);
                reg.Register(Input(client.Id));

                Assert.Throws<DuplicateFolderException>(() => reg.Register(Input(client.Id)));
                Assert.Single(db.ListAnkens());
            }
        }

        [Fact]
        public void 備考を変えれば同じ品番でも登録できる()
        {
            using (var dir = new TempDir())
            using (var db = new AnkenDb(Path.Combine(dir.Path, "a.db")))
            {
                var root = Path.Combine(dir.Path, "ws");
                Directory.CreateDirectory(root);
                var client = db.AddClient("A");
                var reg = new AnkenRegistrar(db, root);

                reg.Register(Input(client.Id));
                reg.Register(Input(client.Id, note: "可動"));

                Assert.Equal(2, db.ListAnkens().Count);
            }
        }

        [Fact]
        public void WorkSpaceのフォルダが無ければ作らずに例外()
        {
            using (var dir = new TempDir())
            using (var db = new AnkenDb(Path.Combine(dir.Path, "a.db")))
            {
                var client = db.AddClient("A");
                var reg = new AnkenRegistrar(db, Path.Combine(dir.Path, "ない"));

                Assert.Throws<DirectoryNotFoundException>(() => reg.Register(Input(client.Id)));
                Assert.False(Directory.Exists(Path.Combine(dir.Path, "ない")));
            }
        }

        [Fact]
        public void 得意先が無ければ例外でフォルダも作らない()
        {
            using (var dir = new TempDir())
            using (var db = new AnkenDb(Path.Combine(dir.Path, "a.db")))
            {
                var root = Path.Combine(dir.Path, "ws");
                Directory.CreateDirectory(root);
                var reg = new AnkenRegistrar(db, root);

                Assert.Throws<InvalidOperationException>(() => reg.Register(Input(999)));
                Assert.Empty(Directory.GetDirectories(root));
            }
        }

        [Fact]
        public void 品番が空なら例外でフォルダも作らない()
        {
            using (var dir = new TempDir())
            using (var db = new AnkenDb(Path.Combine(dir.Path, "a.db")))
            {
                var root = Path.Combine(dir.Path, "ws");
                Directory.CreateDirectory(root);
                var client = db.AddClient("A");
                var reg = new AnkenRegistrar(db, root);

                Assert.Throws<ArgumentException>(() => reg.Register(Input(client.Id, " ")));
                Assert.Empty(Directory.GetDirectories(root));
            }
        }
    }
}
