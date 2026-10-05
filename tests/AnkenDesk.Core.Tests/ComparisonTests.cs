using System;
using System.Linq;
using AnkenDesk.Core;
using Xunit;

namespace AnkenDesk.Core.Tests
{
    public class ComparisonTests
    {
        private static Quote Q(long supplier, long pattern, decimal? price) =>
            new Quote { SupplierId = supplier, PatternId = pattern, UnitPrice = price, LeadTimeDays = 10 };

        [Fact]
        public void 数量ごとに最安の回答を選ぶ()
        {
            var cheapest = Comparison.CheapestQuotes(new[]
            {
                Q(1, 100, 4850m), Q(2, 100, 5200m),
                Q(1, 200, 3200m), Q(2, 200, 2980m),
            });

            Assert.Equal(2, cheapest.Count);
            Assert.Contains(cheapest, q => q.PatternId == 100 && q.SupplierId == 1);
            Assert.Contains(cheapest, q => q.PatternId == 200 && q.SupplierId == 2);
        }

        [Fact]
        public void 同額なら両方を最安にする()
        {
            var cheapest = Comparison.CheapestQuotes(new[] { Q(1, 100, 500m), Q(2, 100, 500m), Q(3, 100, 600m) });

            Assert.Equal(new long[] { 1, 2 }, cheapest.Select(q => q.SupplierId).OrderBy(x => x).ToArray());
        }

        [Fact]
        public void 単価が空の回答は最安の対象外()
        {
            var cheapest = Comparison.CheapestQuotes(new[] { Q(1, 100, null), Q(2, 100, 800m) });

            Assert.Equal(2, Assert.Single(cheapest).SupplierId);
        }

        [Fact]
        public void 回答が1つだけでも最安になる()
        {
            Assert.Single(Comparison.CheapestQuotes(new[] { Q(1, 100, 800m) }));
            Assert.Empty(Comparison.CheapestQuotes(new Quote[0]));
        }

        [Fact]
        public void 回答状況は回答受領日の入った社数を数える()
        {
            var list = new[]
            {
                new AnkenSupplier { ReceivedAt = new DateTime(2026, 10, 7) },
                new AnkenSupplier { ReceivedAt = null },
                new AnkenSupplier { ReceivedAt = new DateTime(2026, 10, 8) },
            };

            Comparison.Progress(list, out var answered, out var total);

            Assert.Equal(2, answered);
            Assert.Equal(3, total);
        }

        [Theory]
        [InlineData("2026-10-02", "2026-10-05", "期限超過 3日")]
        [InlineData("2026-10-05", "2026-10-05", "本日期限")]
        [InlineData("2026-10-07", "2026-10-05", "あと2日")]
        public void 回答期限の状態を文字にする(string due, string today, string expected)
        {
            Assert.Equal(expected, Comparison.DueStatus(DateTime.Parse(due), DateTime.Parse(today)));
        }

        [Theory]
        [InlineData(4850, "¥4,850")]
        [InlineData(4850.5, "¥4,850.5")]
        [InlineData(0.25, "¥0.25")]
        public void 単価の表示(double price, string expected)
        {
            Assert.Equal(expected, Comparison.FormatPrice((decimal)price));
        }
    }
}
