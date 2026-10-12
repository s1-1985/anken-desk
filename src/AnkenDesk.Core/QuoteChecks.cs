using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AnkenDesk.Core
{
    public enum WarningLevel
    {
        /// <summary>気にしておくとよい。</summary>
        Notice = 1,

        /// <summary>入力ミスか、条件の違いかもしれない。調達先に確認する。</summary>
        Check = 2,
    }

    /// <summary>比較表の確認事項1件。</summary>
    public sealed class QuoteWarning
    {
        public WarningLevel Level { get; set; }
        public string Text { get; set; } = "";
        public long? SupplierId { get; set; }
        public long? PatternId { get; set; }
    }

    /// <summary>
    /// 比較表の自動チェック（ビッドレベリングの考え方）。AIは使わず、単価・リードタイム・日付の並びだけを見る。
    /// 見つかるのは「おかしいかもしれない」点で、間違いと決めつけない。しきい値は仮置き。
    /// </summary>
    public static class QuoteChecks
    {
        /// <summary>3社以上: 中央値からこの割合以上離れたら、外れ値。</summary>
        public const decimal OutlierRatio = 0.4m;

        /// <summary>2社: 高い方が安い方の何倍以上なら、確認。</summary>
        public const decimal PairRatio = 2.0m;

        /// <summary>リードタイムが、中央値の何倍以上なら、確認（3社以上）。</summary>
        public const decimal LeadTimeRatio = 2.0m;

        public static IReadOnlyList<QuoteWarning> Run(
            IReadOnlyList<QuantityPattern> patterns, IReadOnlyList<AnkenSupplier> suppliers, IReadOnlyList<Quote> quotes, DateTime today)
        {
            var w = new List<QuoteWarning>();
            var answered = suppliers.Where(s => s.IsAnswered).ToList();
            Func<QuantityPattern, string> label = ExcelExports.PatternText;

            // 1. 数量が多いのに、単価が高い（同じ種別・単位の中で）。試作より量産が高い。
            foreach (var s in suppliers)
            {
                var mine = quotes.Where(q => q.SupplierId == s.SupplierId && q.UnitPrice.HasValue).ToList();
                var priced = patterns.Where(p => p.Kind != "年間見込数").Select(p => new { P = p, Q = mine.FirstOrDefault(q => q.PatternId == p.Id) })
                    .Where(x => x.Q != null).ToList();

                foreach (var g in priced.GroupBy(x => x.P.Kind + "|" + x.P.Unit))
                {
                    var ordered = g.OrderBy(x => x.P.Quantity).ToList();
                    for (var i = 1; i < ordered.Count; i++)
                    {
                        var small = ordered[i - 1];
                        var big = ordered[i];
                        if (big.P.Quantity > small.P.Quantity && big.Q!.UnitPrice > small.Q!.UnitPrice)
                        {
                            w.Add(new QuoteWarning
                            {
                                Level = WarningLevel.Check, SupplierId = s.SupplierId, PatternId = big.P.Id,
                                Text = s.ShortName + ": 数量が多い「" + label(big.P) + "」の単価（" + Comparison.FormatPrice(big.Q.UnitPrice!.Value)
                                    + "）が、少ない「" + label(small.P) + "」（" + Comparison.FormatPrice(small.Q.UnitPrice!.Value) + "）より高くなっています。",
                            });
                        }
                    }
                }

                var proto = priced.Where(x => x.P.Kind == "試作").ToList();
                var mass = priced.Where(x => x.P.Kind == "量産").ToList();
                if (proto.Count > 0 && mass.Count > 0)
                {
                    var protoMin = proto.OrderBy(x => x.Q!.UnitPrice).First();
                    foreach (var m in mass.Where(m => m.Q!.UnitPrice > protoMin.Q!.UnitPrice))
                    {
                        w.Add(new QuoteWarning
                        {
                            Level = WarningLevel.Check, SupplierId = s.SupplierId, PatternId = m.P.Id,
                            Text = s.ShortName + ": 量産「" + label(m.P) + "」の単価（" + Comparison.FormatPrice(m.Q!.UnitPrice!.Value)
                                + "）が、試作「" + label(protoMin.P) + "」（" + Comparison.FormatPrice(protoMin.Q!.UnitPrice!.Value) + "）より高くなっています。",
                        });
                    }
                }
            }

            // 2. 他社と大きく違う単価・リードタイム（数量パターンごと）
            foreach (var p in patterns)
            {
                var prices = quotes.Where(q => q.PatternId == p.Id && q.UnitPrice.HasValue).ToList();
                if (prices.Count >= 3)
                {
                    var med = Median(prices.Select(q => q.UnitPrice!.Value).ToList());
                    if (med > 0)
                    {
                        foreach (var q in prices)
                        {
                            var diff = (q.UnitPrice!.Value - med) / med;
                            if (Math.Abs(diff) >= OutlierRatio)
                            {
                                w.Add(new QuoteWarning
                                {
                                    Level = WarningLevel.Check, SupplierId = q.SupplierId, PatternId = p.Id,
                                    Text = Name(suppliers, q.SupplierId) + ": 「" + label(p) + "」の単価（" + Comparison.FormatPrice(q.UnitPrice.Value) + "）が、"
                                        + prices.Count + "社の中央値（" + Comparison.FormatPrice(med) + "）より " + Percent(diff) + (diff > 0 ? " 高い" : " 安い") + "です。",
                                });
                            }
                        }
                    }
                }
                else if (prices.Count == 2)
                {
                    var hi = prices.OrderByDescending(q => q.UnitPrice).First();
                    var lo = prices.OrderBy(q => q.UnitPrice).First();
                    if (lo.UnitPrice > 0 && hi.UnitPrice / lo.UnitPrice >= PairRatio)
                    {
                        w.Add(new QuoteWarning
                        {
                            Level = WarningLevel.Check, PatternId = p.Id,
                            Text = "「" + label(p) + "」: " + Name(suppliers, hi.SupplierId) + "（" + Comparison.FormatPrice(hi.UnitPrice!.Value) + "）は、"
                                + Name(suppliers, lo.SupplierId) + "（" + Comparison.FormatPrice(lo.UnitPrice!.Value) + "）の " + (hi.UnitPrice / lo.UnitPrice).Value.ToString("0.0", CultureInfo.InvariantCulture) + " 倍です。",
                        });
                    }
                }

                var lts = quotes.Where(q => q.PatternId == p.Id && q.LeadTimeDays.HasValue).ToList();
                if (lts.Count >= 3)
                {
                    var med = Median(lts.Select(q => (decimal)q.LeadTimeDays!.Value).ToList());
                    if (med > 0)
                    {
                        foreach (var q in lts.Where(q => q.LeadTimeDays!.Value >= med * LeadTimeRatio))
                        {
                            w.Add(new QuoteWarning
                            {
                                Level = WarningLevel.Notice, SupplierId = q.SupplierId, PatternId = p.Id,
                                Text = Name(suppliers, q.SupplierId) + ": 「" + label(p) + "」のリードタイム（" + q.LeadTimeDays + "日）が、中央値（" + med.ToString("0.#", CultureInfo.InvariantCulture) + "日）の2倍以上です。",
                            });
                        }
                    }
                }
            }

            // 3. 回答済みなのに、単価が入っていない
            foreach (var s in answered)
            {
                var mine = quotes.Where(q => q.SupplierId == s.SupplierId && q.UnitPrice.HasValue).ToList();
                if (mine.Count == 0)
                {
                    w.Add(new QuoteWarning { Level = WarningLevel.Check, SupplierId = s.SupplierId, Text = s.ShortName + ": 回答済みですが、単価が1つも入っていません。" });
                    continue;
                }

                foreach (var p in patterns)
                {
                    if (mine.All(q => q.PatternId != p.Id) && quotes.Any(q => q.PatternId == p.Id && q.UnitPrice.HasValue && q.SupplierId != s.SupplierId))
                    {
                        w.Add(new QuoteWarning
                        {
                            Level = WarningLevel.Notice, SupplierId = s.SupplierId, PatternId = p.Id,
                            Text = s.ShortName + ": 「" + label(p) + "」の単価が未入力です（他社は入っています）。",
                        });
                    }
                }
            }

            // 4. 別費用・有効期限
            foreach (var s in suppliers)
            {
                if (!string.IsNullOrWhiteSpace(s.ExtraCost) && !s.ExtraCost.Any(char.IsDigit))
                {
                    w.Add(new QuoteWarning { Level = WarningLevel.Notice, SupplierId = s.SupplierId, Text = s.ShortName + ": 別費用に、金額がありません（「" + s.ExtraCost.Trim() + "」）。" });
                }
            }

            foreach (var s in answered)
            {
                if (!s.ValidUntil.HasValue)
                {
                    w.Add(new QuoteWarning { Level = WarningLevel.Notice, SupplierId = s.SupplierId, Text = s.ShortName + ": 見積の有効期限が未入力です。" });
                }
                else if (s.ValidUntil.Value.Date < today.Date)
                {
                    w.Add(new QuoteWarning { Level = WarningLevel.Check, SupplierId = s.SupplierId, Text = s.ShortName + ": 見積の有効期限が切れています（" + s.ValidUntil.Value.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) + "）。単価は、再確認が要ります。" });
                }
            }

            return w.OrderByDescending(x => x.Level).ToList();
        }

        private static string Name(IReadOnlyList<AnkenSupplier> suppliers, long id)
        {
            var s = suppliers.FirstOrDefault(x => x.SupplierId == id);
            return s == null ? "(不明)" : s.ShortName;
        }

        private static string Percent(decimal ratio)
        {
            return Math.Abs(Math.Round(ratio * 100m, 0, MidpointRounding.AwayFromZero)).ToString("0", CultureInfo.InvariantCulture) + "%";
        }

        private static decimal Median(List<decimal> values)
        {
            var sorted = values.OrderBy(v => v).ToList();
            var n = sorted.Count;
            return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2m;
        }
    }
}
