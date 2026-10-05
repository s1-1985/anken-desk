using System.IO;
using System.Linq;
using AnkenDesk.Core;
using Xunit;

namespace AnkenDesk.Core.Tests
{
    public class FolderInspectorTests
    {
        [Fact]
        public void 各サブフォルダの直下のファイル数を数える()
        {
            using (var dir = new TempDir())
            {
                foreach (var sub in FolderNames.Subfolders)
                {
                    Directory.CreateDirectory(Path.Combine(dir.Path, sub));
                }

                File.WriteAllText(Path.Combine(dir.Path, FolderNames.Subfolders[4], "a.txt"), "x");
                File.WriteAllText(Path.Combine(dir.Path, FolderNames.Subfolders[4], "b.txt"), "x");
                Directory.CreateDirectory(Path.Combine(dir.Path, FolderNames.Subfolders[4], "中のフォルダ"));

                var list = FolderInspector.Scan(dir.Path);

                Assert.Equal(15, list.Count);
                Assert.Equal(2, list[4].FileCount);
                Assert.Equal(0, list[0].FileCount);
                Assert.All(list, e => Assert.True(e.Exists));
            }
        }

        [Fact]
        public void 旧版フォルダがあれば5番の直後に並べる()
        {
            using (var dir = new TempDir())
            {
                foreach (var sub in FolderNames.Subfolders)
                {
                    Directory.CreateDirectory(Path.Combine(dir.Path, sub));
                }

                var old = Path.Combine(dir.Path, FolderNames.Subfolders[4], FolderNames.OldVersionFolder);
                Directory.CreateDirectory(old);
                File.WriteAllText(Path.Combine(old, "old.txt"), "x");

                var list = FolderInspector.Scan(dir.Path);

                Assert.Equal(16, list.Count);
                Assert.True(list[5].IsOldVersion);
                Assert.Equal(FolderNames.OldVersionFolder, list[5].Name);
                Assert.Equal(1, list[5].FileCount);
                Assert.Equal(0, list[4].FileCount);
            }
        }

        [Fact]
        public void フォルダが無ければ存在なしと0件を返す()
        {
            using (var dir = new TempDir())
            {
                var list = FolderInspector.Scan(Path.Combine(dir.Path, "ない"));

                Assert.Equal(15, list.Count);
                Assert.All(list, e => Assert.False(e.Exists));
                Assert.All(list, e => Assert.Equal(0, e.FileCount));
            }
        }
    }
}
