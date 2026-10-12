using System;
using System.Collections.Generic;
using System.Linq;

namespace AnkenDesk.Core
{
    /// <summary>前回の単価1つ。</summary>
    public sealed class PreviousPrice
    {
        public decimal Price { get; set; }

        /// <summary>前回の案件の依頼日。</summary>
        public DateTime Date { get; set; }
        public long AnkenId { get; set; }

        /// <summary>今回が前回より何%高い（正）・安い（負）か。</summary>
        public decimal? DiffPercent(decimal current)
        {
            return Price == 0 ? (decimal?)null : Math.Round((current - Price) / Price * 100m, 1, MidpointRounding.AwayFromZero);
        }

        public static string Format(PreviousPrice p, decimal? current)
        {
            var text = "前回 " + Comparison.FormatPrice(p.Price);
            var diff = current.HasValue ? p.DiffPercent(current.Value) : null;
            return diff.HasValue ? text + "（" + (diff.Value > 0 ? "+" : "") + diff.Value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "%）" : text;
        }
    }

    /// <summary>
    /// 同じ品番（空白・ハイフンなどの違いは無視）の過去の案件から、前回の仕入単価・前回の提出単価を探す。
    /// 同じ調達先・同じ数量の表記のものだけ（比べられるものだけ）。前回は、依頼日が今回以前で、いちばん新しい案件。
    /// </summary>
    public static class PriceHistory
    {
        public static string QuoteKey(long supplierId, string patternText)
        {
            return supplierId + "|" + patternText;
        }

        /// <summary>前回の仕入単価。キーは <see cref="QuoteKey"/>。</summary>
        public static IReadOnlyDictionary<string, PreviousPrice> PreviousQuotes(AnkenDb db, AnkenRecord current)
        {
            var result = new Dictionary<string, PreviousPrice>();
            foreach (var past in SamePartBefore(db, current))
            {
                var patterns = db.ListQuantities(past.Id).ToDictionary(p => p.Id, p => ExcelExports.PatternText(p));
                foreach (var q in db.ListQuotes(past.Id).Where(q => q.UnitPrice.HasValue))
                {
                    string text;
                    if (!patterns.TryGetValue(q.PatternId, out text))
                    {
                        continue;
                    }

                    var key = QuoteKey(q.SupplierId, text);
                    if (!result.ContainsKey(key)) // 新しい案件から見ているので、最初に見つかったものが前回
                    {
                        result[key] = new PreviousPrice { Price = q.UnitPrice!.Value, Date = past.RequestDate, AnkenId = past.Id };
                    }
                }
            }

            return result;
        }

        /// <summary>前回の提出単価。キーは数量の表記。</summary>
        public static IReadOnlyDictionary<string, PreviousPrice> PreviousSelling(AnkenDb db, AnkenRecord current)
        {
            var result = new Dictionary<string, PreviousPrice>();
            foreach (var past in SamePartBefore(db, current))
            {
                var patterns = db.ListQuantities(past.Id).ToDictionary(p => p.Id, p => ExcelExports.PatternText(p));
                foreach (var c in db.ListClientPrices(past.Id).Where(c => c.SellingPrice.HasValue))
                {
                    string text;
                    if (patterns.TryGetValue(c.PatternId, out text) && !result.ContainsKey(text))
                    {
                        result[text] = new PreviousPrice { Price = c.SellingPrice!.Value, Date = past.RequestDate, AnkenId = past.Id };
                    }
                }
            }

            return result;
        }

        // 同じ品番の、今回より前（同日を含む）の案件。新しい順。
        private static IEnumerable<AnkenRecord> SamePartBefore(AnkenDb db, AnkenRecord current)
        {
            return db.ListAnkens()
                .Where(a => a.Id != current.Id && a.RequestDate.Date <= current.RequestDate.Date && SimilarAnkens.Compare(current.PartNumber, a.PartNumber) == SimilarKind.Same)
                .OrderByDescending(a => a.RequestDate).ThenByDescending(a => a.Id)
                .ToList();
        }
    }
}
