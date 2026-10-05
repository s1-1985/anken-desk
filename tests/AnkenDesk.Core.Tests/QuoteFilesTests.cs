using System;
using System.IO;
using System.Linq;
using AnkenDesk.Core;
using Xunit;

namespace AnkenDesk.Core.Tests
{
    public class QuoteFilesTests
    {
        private static string MakeAnken(TempDir dir)
        {
            var anken = Path.Combine(dir.Path, "anken");
            foreach (var sub in FolderNames.Subfolders)
            {
                Directory.CreateDirectory(Path.Combine(anken, sub));
            }

            return anken;
        }

        private static string MakeSource(TempDir dir, string name, string content = "pdf")
        {
            var src = Path.Combine(dir.Path, "src");
            Directory.CreateDirectory(src);
            var p = Path.Combine(src, name);
            File.WriteAllText(p, content);
            return p;
        }

        [Fact]
        public void ファイル名は略称と元の名前を全角スペースでつなぐ()
        {
            Assert.Equal("略称　元.pdf", QuoteFiles.BuildFileName("略称", "元.pdf"));
        }

        [Fact]
        public void ファイル名の使えない文字は置き換える()
        {
            Assert.Equal("A_B　見積:書.pdf".Replace(':', '_'), QuoteFiles.BuildFileName("A/B", "見積:書.pdf"));
        }

        [Fact]
        public void 略称かファイル名が空なら例外()
        {
            Assert.Throws<ArgumentException>(() => QuoteFiles.BuildFileName(" ", "a.pdf"));
            Assert.Throws<ArgumentException>(() => QuoteFiles.BuildFileName("A", ""));
        }

        [Fact]
        public void 保存すると5番のフォルダにコピーされ元は残る()
        {
            using (var dir = new TempDir())
            {
                var anken = MakeAnken(dir);
                var src = MakeSource(dir, "元.pdf", "内容");

                var saved = QuoteFiles.Save(anken, "略称", src);

                Assert.Equal(Path.Combine(anken, FolderNames.Subfolders[4], "略称　元.pdf"), saved);
                Assert.Equal("内容", File.ReadAllText(saved));
                Assert.True(File.Exists(src));
            }
        }

        [Fact]
        public void 同名があれば連番を付けて上書きしない()
        {
            using (var dir = new TempDir())
            {
                var anken = MakeAnken(dir);
                var src = MakeSource(dir, "元.pdf", "2回目");
                var first = QuoteFiles.Save(anken, "略称", MakeSource(dir, "元.pdf", "1回目"));

                var second = QuoteFiles.Save(anken, "略称", src);
                var third = QuoteFiles.Save(anken, "略称", src);

                Assert.EndsWith("略称　元 (2).pdf", second);
                Assert.EndsWith("略称　元 (3).pdf", third);
                Assert.Equal("1回目", File.ReadAllText(first));
            }
        }

        [Fact]
        public void 案件フォルダか元のファイルが無ければ例外()
        {
            using (var dir = new TempDir())
            {
                var anken = MakeAnken(dir);
                var src = MakeSource(dir, "元.pdf");

                Assert.Throws<DirectoryNotFoundException>(() => QuoteFiles.Save(Path.Combine(dir.Path, "ない"), "略称", src));
                Assert.Throws<FileNotFoundException>(() => QuoteFiles.Save(anken, "略称", Path.Combine(dir.Path, "ない.pdf")));
            }
        }

        [Fact]
        public void 一覧は略称の完全一致の接頭辞だけ拾う()
        {
            using (var dir = new TempDir())
            {
                var anken = MakeAnken(dir);
                QuoteFiles.Save(anken, "A", MakeSource(dir, "x.pdf"));
                QuoteFiles.Save(anken, "AB", MakeSource(dir, "y.pdf"));

                var a = QuoteFiles.ListCurrent(anken, "A");

                Assert.Equal("A　x.pdf", Assert.Single(a));
            }
        }

        [Fact]
        public void 旧版へ移すと元の場所から無くなり旧版フォルダに入る()
        {
            using (var dir = new TempDir())
            {
                var anken = MakeAnken(dir);
                var saved = QuoteFiles.Save(anken, "A", MakeSource(dir, "x.pdf", "旧"));

                var moves = QuoteFiles.MoveToOld(anken, QuoteFiles.ListCurrent(anken, "A"));

                var m = Assert.Single(moves);
                Assert.Equal(saved, m.Key);
                Assert.False(File.Exists(saved));
                Assert.Equal("旧", File.ReadAllText(m.Value));
                Assert.Equal(Path.Combine(anken, FolderNames.Subfolders[4], FolderNames.OldVersionFolder), Path.GetDirectoryName(m.Value));
                Assert.Empty(QuoteFiles.ListCurrent(anken, "A"));
                Assert.Single(QuoteFiles.ListOld(anken, "A"));
            }
        }

        [Fact]
        public void 旧版に同名があれば連番で残し両方とも消さない()
        {
            using (var dir = new TempDir())
            {
                var anken = MakeAnken(dir);
                var src = MakeSource(dir, "x.pdf");
                QuoteFiles.Save(anken, "A", src);
                QuoteFiles.MoveToOld(anken, QuoteFiles.ListCurrent(anken, "A"));
                QuoteFiles.Save(anken, "A", src);

                QuoteFiles.MoveToOld(anken, QuoteFiles.ListCurrent(anken, "A"));

                Assert.Equal(2, QuoteFiles.ListOld(anken, "A").Count);
            }
        }

        [Fact]
        public void 移したものは元に戻せる()
        {
            using (var dir = new TempDir())
            {
                var anken = MakeAnken(dir);
                var saved = QuoteFiles.Save(anken, "A", MakeSource(dir, "x.pdf"));
                var moves = QuoteFiles.MoveToOld(anken, QuoteFiles.ListCurrent(anken, "A"));

                QuoteFiles.Undo(moves);

                Assert.True(File.Exists(saved));
                Assert.Empty(QuoteFiles.ListOld(anken, "A"));
            }
        }

        [Fact]
        public void 移すファイルが無ければ何もしない()
        {
            using (var dir = new TempDir())
            {
                var anken = MakeAnken(dir);

                var moves = QuoteFiles.MoveToOld(anken, new[] { "ない.pdf" });

                Assert.Empty(moves);
                Assert.False(Directory.Exists(QuoteFiles.OldDir(anken)));
            }
        }
    }
}
