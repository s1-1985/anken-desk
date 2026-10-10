using System.IO;
using System.Linq;
using Xunit;

namespace AnkenDesk.Core.Tests
{
    public class RequestFilesTests
    {
        [Theory]
        [InlineData("依頼書.xlsx", "1.客先見積依頼内容")]
        [InlineData("依頼書.XLS", "1.客先見積依頼内容")]
        [InlineData("図面.pdf", "2.図面")]
        [InlineData("図面.PDF", "2.図面")]
        [InlineData("形状.zip", "1.客先見積依頼内容")]
        [InlineData("拡張子なし", "1.客先見積依頼内容")]
        public void Sorts_by_extension(string name, string folder)
        {
            Assert.Equal(folder, RequestFiles.TargetSubfolder(name));
        }

        [Fact]
        public void Copies_without_moving_numbers_duplicates_and_reports_failures()
        {
            using (var t = new TempDir())
            {
                var src = Path.Combine(t.Path, "src");
                Directory.CreateDirectory(src);
                File.WriteAllText(Path.Combine(src, "依頼.xlsx"), "a");
                File.WriteAllText(Path.Combine(src, "図面.pdf"), "b");
                var anken = Path.Combine(t.Path, "anken");
                Directory.CreateDirectory(anken);

                var files = new[] { Path.Combine(src, "依頼.xlsx"), Path.Combine(src, "図面.pdf"), Path.Combine(src, "無い.pdf") };
                var first = RequestFiles.CopyInto(anken, files);
                var second = RequestFiles.CopyInto(anken, files.Take(1));

                Assert.True(File.Exists(Path.Combine(anken, "1.客先見積依頼内容", "依頼.xlsx")));
                Assert.True(File.Exists(Path.Combine(anken, "2.図面", "図面.pdf")));
                Assert.True(File.Exists(Path.Combine(src, "依頼.xlsx"))); // 元は残る
                Assert.NotNull(first[0].Destination);
                Assert.Null(first[2].Destination);
                Assert.NotEqual("", first[2].Error);
                Assert.EndsWith("依頼 (2).xlsx", second[0].Destination);
            }
        }
    }
}
