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
