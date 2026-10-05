using System;
using AnkenDesk.Core;
using Xunit;

namespace AnkenDesk.Core.Tests
{
    public class FolderNamesTests
    {
        [Fact]
        public void 案件フォルダ名は日付と品番を全角スペースでつなぐ()
        {
            var name = FolderNames.BuildAnkenFolderName(new DateTime(2026, 10, 5), "TEST-001", null);

            Assert.Equal("20261005\u3000TEST-001", name);
        }

        [Fact]
        public void 備考があれば全角スペースで末尾に付く()
        {
            var name = FolderNames.BuildAnkenFolderName(new DateTime(2026, 10, 5), "TEST-001", "Lot5");

            Assert.Equal("20261005\u3000TEST-001\u3000Lot5", name);
        }

        [Fact]
        public void 備考が空白だけなら付けない()
        {
            var name = FolderNames.BuildAnkenFolderName(new DateTime(2026, 10, 5), "TEST-001", "  ");

            Assert.Equal("20261005\u3000TEST-001", name);
        }

        [Fact]
        public void 使えない文字は置き換える()
        {
            var name = FolderNames.BuildAnkenFolderName(new DateTime(2026, 1, 2), "A/B:C", "x*y");

            Assert.Equal("20260102\u3000A_B_C\u3000x_y", name);
        }

        [Fact]
        public void 品番が空なら例外()
        {
            Assert.Throws<ArgumentException>(() => FolderNames.BuildAnkenFolderName(DateTime.Today, "  ", null));
        }

        [Fact]
        public void サブフォルダは15個で番号が1から15まで順に付く()
        {
            Assert.Equal(15, FolderNames.Subfolders.Length);
            for (var i = 0; i < 15; i++)
            {
                Assert.StartsWith((i + 1) + ".", FolderNames.Subfolders[i]);
            }
        }
    }
}
