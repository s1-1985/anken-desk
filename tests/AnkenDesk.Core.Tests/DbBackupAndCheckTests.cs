using System;
using System.IO;
using System.Linq;
using Xunit;

namespace AnkenDesk.Core.Tests
{
    public class DbBackupTests
    {
        [Fact]
        public void Creates_one_per_day_and_keeps_latest()
        {
            using (var t = new TempDir())
            {
                var db = Path.Combine(t.Path, "a.db");
                File.WriteAllText(db, "x");
                var dir = Path.Combine(t.Path, "backup");

                Assert.NotNull(DbBackup.CreateDaily(db, dir, new DateTime(2026, 10, 1, 9, 0, 0), 2));
                Assert.Null(DbBackup.CreateDaily(db, dir, new DateTime(2026, 10, 1, 15, 0, 0), 2)); // 同じ日
                Assert.NotNull(DbBackup.CreateDaily(db, dir, new DateTime(2026, 10, 2, 9, 0, 0), 2));
                Assert.NotNull(DbBackup.CreateDaily(db, dir, new DateTime(2026, 10, 3, 9, 0, 0), 2));

                var names = Directory.GetFiles(dir).Select(Path.GetFileName).OrderBy(x => x).ToList();
                Assert.Equal(2, names.Count);
                Assert.StartsWith("anken-20261002", names[0]);
                Assert.StartsWith("anken-20261003", names[1]);
            }
        }

        [Fact]
        public void No_db_means_no_backup_and_no_error()
        {
            using (var t = new TempDir())
            {
                Assert.Null(DbBackup.CreateDaily(Path.Combine(t.Path, "none.db"), Path.Combine(t.Path, "b"), DateTime.Now));
                Assert.False(Directory.Exists(Path.Combine(t.Path, "b")));
            }
        }
    }

    public class FolderCheckTests
    {
        [Fact]
        public void Finds_missing_folder_and_subfolders_and_repairs_only_subfolders()
        {
            using (var t = new TempDir())
            using (var db = new AnkenDb(Path.Combine(t.Path, "a.db")))
            {
                var root = Path.Combine(t.Path, "ws");
                Directory.CreateDirectory(root);
                var c = db.AddClient("ダミー得意先");
                var reg = new AnkenRegistrar(db, root);
                Func<string, AnkenRecord> make = part => reg.Register(new AnkenInput { ClientId = c.Id, RequestDate = new DateTime(2026, 1, 5), PartNumber = part, ReplyDueDate = new DateTime(2026, 1, 10) });
                var ok = make("OK-1");
                var gone = make("GONE-1");
                var partial = make("PART-1");

                Directory.Delete(Path.Combine(root, gone.FolderPath), true);
                Directory.Delete(Path.Combine(root, partial.FolderPath, FolderNames.Subfolders[2]));

                var problems = FolderCheck.Run(root, db.ListAnkens());
                Assert.Equal(2, problems.Count);
                Assert.DoesNotContain(problems, p => p.Anken.Id == ok.Id);
                Assert.True(problems.Single(p => p.Anken.Id == gone.Id).FolderMissing);
                var pp = problems.Single(p => p.Anken.Id == partial.Id);
                Assert.Equal(new[] { FolderNames.Subfolders[2] }, pp.MissingSubfolders);

                Assert.Equal(0, FolderCheck.CreateMissingSubfolders(root, problems.Single(p => p.Anken.Id == gone.Id)));
                Assert.False(Directory.Exists(Path.Combine(root, gone.FolderPath)));
                Assert.Equal(1, FolderCheck.CreateMissingSubfolders(root, pp));
                Assert.Single(FolderCheck.Run(root, db.ListAnkens()));
            }
        }
    }
}
