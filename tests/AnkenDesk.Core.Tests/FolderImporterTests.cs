using System;
using System.IO;
using System.Linq;
using Xunit;

namespace AnkenDesk.Core.Tests
{
    public class FolderImporterTests
    {
        private const string S = "　";

        [Fact]
        public void Parse_part_only()
        {
            string p;
            var r = FolderImporter.Parse("20260105" + S + "DUMMY-001", out p)!;
            Assert.Equal(new DateTime(2026, 1, 5), r.RequestDate);
            Assert.Equal("DUMMY-001", r.PartNumber);
            Assert.Equal("", r.Note);
        }

        [Fact]
        public void Parse_with_note_keeps_inner_spaces_in_note()
        {
            string p;
            var r = FolderImporter.Parse("20260105" + S + "DUMMY-001" + S + "増面" + S + "Lot2", out p)!;
            Assert.Equal("DUMMY-001", r.PartNumber);
            Assert.Equal("増面" + S + "Lot2", r.Note);
        }

        [Theory]
        [InlineData("DUMMY-001")]
        [InlineData("2026010　DUMMY")]
        [InlineData("20261301" + S + "DUMMY")]
        [InlineData("20260105 DUMMY-001")]
        [InlineData("20260105" + S)]
        [InlineData("20260105" + S + S + "備考だけ")]
        public void Parse_rejects_bad_names(string name)
        {
            string p;
            Assert.Null(FolderImporter.Parse(name, out p));
            Assert.NotEqual("", p);
        }

        [Fact]
        public void Scan_is_read_only_and_flags_problems()
        {
            using (var t = new TempDir())
            {
                var ok = Path.Combine(t.Path, "得意先A", "20260105" + S + "DUMMY-001");
                Directory.CreateDirectory(Path.Combine(ok, FolderNames.Subfolders[0]));
                Directory.CreateDirectory(Path.Combine(t.Path, "得意先A", "メモ"));
                Directory.CreateDirectory(Path.Combine(t.Path, "得意先A", "_旧"));
                Directory.CreateDirectory(Path.Combine(t.Path, ".hidden", "20260105" + S + "X"));
                var before = Directory.GetFileSystemEntries(t.Path, "*", SearchOption.AllDirectories).Length;

                var list = FolderImporter.Scan(t.Path, new string[0]);

                Assert.Equal(2, list.Count);
                var good = list.Single(c => c.Parsed != null);
                Assert.Equal(14, good.MissingSubfolders);
                Assert.True(good.CanImport);
                Assert.False(list.Single(c => c.Parsed == null).CanImport);
                Assert.Equal(before, Directory.GetFileSystemEntries(t.Path, "*", SearchOption.AllDirectories).Length);
            }
        }

        [Fact]
        public void Apply_registers_only_new_ones_and_creates_nothing_on_disk()
        {
            using (var t = new TempDir())
            using (var db = new AnkenDb(Path.Combine(t.Path, "a.db")))
            {
                var root = Path.Combine(t.Path, "ws");
                Directory.CreateDirectory(Path.Combine(root, "得意先A", "20260105" + S + "DUMMY-001"));
                Directory.CreateDirectory(Path.Combine(root, "得意先A", "20260106" + S + "DUMMY-002" + S + "備考"));
                Directory.CreateDirectory(Path.Combine(root, "得意先B", "20260107" + S + "DUMMY-003"));
                Directory.CreateDirectory(Path.Combine(root, "得意先B", "不明"));

                var first = FolderImporter.Scan(root, new string[0]);
                var n = FolderImporter.Apply(db, first, out var failures);
                Assert.Equal(3, n);
                Assert.Empty(failures);
                Assert.Equal(2, db.ListClients().Count);
                Assert.Equal(3, db.ListAnkens().Count);
                Assert.Equal("備考", db.ListAnkens().Single(a => a.PartNumber == "DUMMY-002").Note);

                // 2回目は、登録済みなので何も入らない
                var second = FolderImporter.Scan(root, db.ListAnkens().Select(a => a.FolderPath));
                Assert.Equal(0, FolderImporter.Apply(db, second, out failures));
                Assert.Equal(3, db.ListAnkens().Count);

                // フォルダは増えていない（サブフォルダも作らない）
                Assert.Empty(Directory.GetDirectories(Path.Combine(root, "得意先A", "20260105" + S + "DUMMY-001")));
            }
        }
    }
}
