using System;
using System.Collections.Generic;
using System.Linq;

namespace AnkenDesk.Core
{
    /// <summary>案件画面の比較マトリクスと、期限・回答状況の計算。画面に依存しない。</summary>
    public static class Comparison
    {
        /// <summary>数量パターンごとに、単価が最も安い回答。同額なら全部を返す。単価が空の回答は対象外。</summary>
        public static IReadOnlyList<Quote> CheapestQuotes(IEnumerable<Quote> quotes)
        {
            var result = new List<Quote>();
            foreach (var group in quotes.Where(q => q.UnitPrice.HasValue).GroupBy(q => q.PatternId))
            {
                var min = group.Min(q => q.UnitPrice!.Value);
                result.AddRange(group.Where(q => q.UnitPrice!.Value == min));
            }

            return result;
        }

        /// <summary>回答済みの社数と、依頼した社数。回答済みは「回答受領日」が入っていること。</summary>
        public static void Progress(IReadOnlyCollection<AnkenSupplier> suppliers, out int answered, out int total)
        {
            total = suppliers.Count;
            answered = suppliers.Count(s => s.IsAnswered);
        }

        /// <summary>回答期限の状態を文字で返す（色だけに頼らない）。</summary>
        public static string DueStatus(DateTime dueDate, DateTime today)
        {
            var days = (dueDate.Date - today.Date).Days;
            if (days < 0)
            {
                return "期限超過 " + (-days) + "日";
            }

            if (days == 0)
            {
                return "本日期限";
            }

            return "あと" + days + "日";
        }

        /// <summary>単価の表示。例: 4850 → 「¥4,850」、4850.5 → 「¥4,850.5」。</summary>
        public static string FormatPrice(decimal price)
        {
            return "¥" + price.ToString("#,##0.####", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
