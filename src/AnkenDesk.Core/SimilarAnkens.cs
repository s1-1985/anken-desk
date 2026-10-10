using System;
using System.Collections.Generic;
using System.Linq;

namespace AnkenDesk.Core
{
    /// <summary>過去案件との品番の近さ。</summary>
    public enum SimilarKind
    {
        /// <summary>先頭が6文字以上、共通。</summary>
        Prefix = 1,

        /// <summary>片方の品番が、もう片方を含む（4文字以上）。</summary>
        Contains = 2,

        /// <summary>同じ品番（空白・ハイフンなどの違いは無視）。</summary>
        Same = 3,
    }

    /// <summary>過去案件で出た1つの単価。</summary>
    public sealed class SimilarPrice
    {
        public string SupplierName { get; set; } = "";
        public string SupplierShortName { get; set; } = "";
        public string PatternText { get; set; } = "";
        public decimal Price { get; set; }
        public int? LeadTimeDays { get; set; }

        /// <summary>今回の案件に、同じ調達先・同じ数量の単価があるとき、その単価。</summary>
        public decimal? CurrentPrice { get; set; }

        /// <summary>今回の単価が過去より何%高い（正）・安い（負）か。比べられないときは null。</summary>
        public decimal? DiffPercent { get; set; }
    }

    public sealed class SimilarAnken
    {
        public AnkenRecord Anken { get; set; } = new AnkenRecord();
        public SimilarKind Kind { get; set; }
        public List<SimilarPrice> Prices { get; } = new List<SimilarPrice>();
    }

    /// <summary>
    /// 過去の類似案件を探す。AIも外部通信も使わず、DBの中の品番を比べるだけ。
    /// 品番の比べ方は、空白・ハイフン・アンダースコア・ピリオド・スラッシュを除き、大文字にしたうえで、
    /// 同じ → 片方がもう片方を含む（4文字以上）→ 先頭6文字以上が共通、の順に近いとみなす【仮置き】。
    /// </summary>
    public static class SimilarAnkens
    {
        private static readonly char[] Ignored = { ' ', '　', '-', '－', '‐', '−', '_', '.', '/', '／' };

        public static string Normalize(string part)
        {
            var chars = (part ?? "").Where(c => Array.IndexOf(Ignored, c) < 0).ToArray();
            return new string(chars).ToUpperInvariant();
        }

        /// <summary>2つの品番の近さ。近くなければ null。</summary>
        public static SimilarKind? Compare(string a, string b)
        {
            var x = Normalize(a);
            var y = Normalize(b);
            if (x.Length == 0 || y.Length == 0)
            {
                return null;
            }

            if (x == y)
            {
                return SimilarKind.Same;
            }

            if (x.Length >= 4 && y.Length >= 4 && (x.Contains(y) || y.Contains(x)))
            {
                return SimilarKind.Contains;
            }

            var n = 0;
            while (n < x.Length && n < y.Length && x[n] == y[n])
            {
                n++;
            }

            return n >= 6 ? (SimilarKind?)SimilarKind.Prefix : null;
        }

        /// <summary>過去の類似案件（近い順、同じ近さなら依頼日の新しい順）。最大 <paramref name="max"/> 件。</summary>
        public static IReadOnlyList<SimilarAnken> Find(AnkenDb db, AnkenRecord current, int max = 30)
        {
            var result = new List<SimilarAnken>();
            foreach (var a in db.ListAnkens())
            {
                if (a.Id == current.Id)
                {
                    continue;
                }

                var kind = Compare(current.PartNumber, a.PartNumber);
                if (kind.HasValue)
                {
                    result.Add(new SimilarAnken { Anken = a, Kind = kind.Value });
                }
            }

            var ordered = result.OrderByDescending(r => r.Kind).ThenByDescending(r => r.Anken.RequestDate).ThenByDescending(r => r.Anken.Id).Take(max).ToList();
            if (ordered.Count == 0)
            {
                return ordered;
            }

            // 今回の案件の単価（調達先ID + 数量の表記 → 単価）。比べるのに使う。
            var currentPatterns = db.ListQuantities(current.Id).ToDictionary(p => p.Id, p => ExcelExports.PatternText(p));
            var currentPrices = new Dictionary<string, decimal>();
            foreach (var q in db.ListQuotes(current.Id).Where(q => q.UnitPrice.HasValue))
            {
                string text;
                if (currentPatterns.TryGetValue(q.PatternId, out text))
                {
                    currentPrices[q.SupplierId + "|" + text] = q.UnitPrice!.Value;
                }
            }

            foreach (var s in ordered)
            {
                var patterns = db.ListQuantities(s.Anken.Id).ToDictionary(p => p.Id, p => ExcelExports.PatternText(p));
                var suppliers = db.ListAnkenSuppliers(s.Anken.Id).ToDictionary(x => x.SupplierId);
                foreach (var q in db.ListQuotes(s.Anken.Id).Where(q => q.UnitPrice.HasValue))
                {
                    string text;
                    AnkenSupplier? sup;
                    if (!patterns.TryGetValue(q.PatternId, out text) || !suppliers.TryGetValue(q.SupplierId, out sup))
                    {
                        continue;
                    }

                    var price = new SimilarPrice
                    {
                        SupplierName = sup.SupplierName,
                        SupplierShortName = sup.ShortName,
                        PatternText = text,
                        Price = q.UnitPrice!.Value,
                        LeadTimeDays = q.LeadTimeDays,
                    };
                    decimal now;
                    if (currentPrices.TryGetValue(q.SupplierId + "|" + text, out now))
                    {
                        price.CurrentPrice = now;
                        if (price.Price != 0)
                        {
                            price.DiffPercent = Math.Round((now - price.Price) / price.Price * 100m, 1, MidpointRounding.AwayFromZero);
                        }
                    }

                    s.Prices.Add(price);
                }
            }

            return ordered;
        }
    }
}
