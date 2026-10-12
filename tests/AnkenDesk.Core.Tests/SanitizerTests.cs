using System.IO;
using System.Linq;
using AnkenDesk.Core;
using Xunit;

namespace AnkenDesk.Core.Tests
{
    public class SanitizerTests
    {
        [Theory]
        [InlineData("a\\b", "a_b")]
        [InlineData("a/b", "a_b")]
        [InlineData("a:b*c?d\"e<f>g|h", "a_b_c_d_e_f_g_h")]
        [InlineData("  abc  ", "abc")]
        [InlineData("abc.", "abc")]
        [InlineData("abc\u3000", "abc")]
        public void 使えない文字と前後の空白を整える(string input, string expected)
        {
            Assert.Equal(expected, Sanitizer.FileName(input));
        }

        [Theory]
        [InlineData("CON", "_CON")]
        [InlineData("nul", "_nul")]
        [InlineData("COM1.txt", "_COM1.txt")]
        [InlineData("CONSOLE", "CONSOLE")]
        public void 予約名は避ける(string input, string expected)
        {
            Assert.Equal(expected, Sanitizer.FileName(input));
        }

        [Fact]
        public void 全角の文字はそのまま残す()
        {
            Assert.Equal("見積／依頼", Sanitizer.FileName("見積／依頼"));
        }

        [Theory]
        [InlineData("得意先\u3000試作", true)]
        [InlineData("", false)]
        [InlineData("  ", false)]
        [InlineData("a/b", false)]
        [InlineData("CON", false)]
        public void IsValidName(string input, bool expected)
        {
            Assert.Equal(expected, Sanitizer.IsValidName(input));
        }
    }
}

namespace AnkenDesk.Core.Tests
{
    public class FitFileNameTests
    {
        [Fact]
        public void Short_names_are_untouched()
        {
            Assert.Equal("甲　見積書.pdf", Sanitizer.FitFileName(@"C:\ws\得意先\案件\5.調達先見積もり", "甲　見積書.pdf"));
        }

        [Fact]
        public void Long_names_are_cut_so_the_whole_path_fits_and_the_extension_stays()
        {
            var dir = @"C:\Users\someone\Desktop\Work space\得意先名　種別\20261005　DUMMY-001　備考\5.調達先見積もり\メール";
            var longName = "20261005　甲　" + new string('あ', 300) + ".msg";
            var fitted = Sanitizer.FitFileName(dir, longName);
            Assert.EndsWith(".msg", fitted);
            Assert.True(dir.Length + 1 + fitted.Length + 6 <= Sanitizer.MaxPathLength, "path too long: " + (dir.Length + 1 + fitted.Length));
            Assert.StartsWith("20261005　甲　", fitted);
        }

        [Fact]
        public void Does_not_split_surrogate_pairs_and_keeps_a_minimum()
        {
            var dir = new string('x', 240); // フォルダだけで、ほぼ上限
            var fitted = Sanitizer.FitFileName(dir, new string('a', 100) + ".pdf");
            Assert.EndsWith(".pdf", fitted);
            Assert.True(fitted.Length >= 8);

            var emoji = Sanitizer.FitFileName(@"C:\" + new string('y', 220), string.Concat(Enumerable.Repeat("😀", 60)) + ".txt");
            Assert.False(char.IsHighSurrogate(emoji[emoji.Length - 5])); // 拡張子の直前が、ペアの前半で終わらない
        }

        [Fact]
        public void UniquePath_uses_the_fitted_name_and_numbering_still_fits()
        {
            using (var t = new TempDir())
            {
                var longName = new string('a', 400) + ".pdf"; // Linuxのファイル名は255バイトまでなので、ASCIIで
                var first = QuoteFiles.UniquePath(t.Path, longName);
                File.WriteAllText(first, "x");
                var second = QuoteFiles.UniquePath(t.Path, longName);
                Assert.NotEqual(first, second);
                Assert.True(second.Length - t.Path.Length < Sanitizer.MaxPathLength - t.Path.Length + 1);
                Assert.EndsWith(").pdf", second);
                Assert.True(second.Length <= Sanitizer.MaxPathLength);
            }
        }
    }
}
