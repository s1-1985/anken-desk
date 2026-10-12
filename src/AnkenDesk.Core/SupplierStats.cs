using System;
using System.Collections.Generic;
using System.Linq;

namespace AnkenDesk.Core
{
    /// <summary>調達先1社の実績（全案件を通した集計）。</summary>
    public sealed class SupplierStat
    {
        public long SupplierId { get; set; }
        public string Name { get; set; } = "";
        public string ShortName { get; set; } = "";

        /// <summary>依頼先として加えられた案件の数。</summary>
        public int Ankens { get; set; }

        /// <summary>見積依頼を送った（依頼送付日がある）案件の数。</summary>
        public int Requested { get; set; }

        /// <summary>回答した（回答受領日がある）案件の数。</summary>
        public int Answered { get; set; }

        /// <summary>辞退された案件の数。</summary>
        public int Declined { get; set; }

        /// <summary>辞退率（辞退 ÷ 依頼）。依頼が無ければnull。</summary>
        public double? DeclineRate
        {
            get { return Requested == 0 ? (double?)null : (double)Declined / Requested; }
        }

        /// <summary>1行の実績の文字（調達先を選ぶ画面に出す）。例: 「回答80%・平均3.2日・最安2回・辞退1」。実績が無ければ空。</summary>
        public string Summary()
        {
            if (Ankens == 0)
            {
                return "";
            }

            var parts = new List<string>();
            if (AnswerRate.HasValue)
            {
                parts.Add("回答" + (AnswerRate.Value * 100).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "%");
            }

            if (AverageReplyDays.HasValue)
            {
                parts.Add("平均" + AverageReplyDays.Value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "日");
            }

            if (CheapestAnkens > 0)
            {
                parts.Add("最安" + CheapestAnkens + "回");
            }

            if (Declined > 0)
            {
                parts.Add("辞退" + Declined);
            }

            return parts.Count == 0 ? "依頼" + Ankens + "件" : string.Join("・", parts);
        }

        /// <summary>依頼送付日から回答受領日までの平均日数。両方の日付がある案件だけで数える。無ければnull。</summary>
        public double? AverageReplyDays { get; set; }

        /// <summary>数量パターンのどれかで最安だった案件の数（単価を出した他社と比べて、同額を含む）。</summary>
        public int CheapestAnkens { get; set; }

        /// <summary>回答率（回答 ÷ 依頼）。依頼が無ければnull。</summary>
        public double? AnswerRate
        {
            get { return Requested == 0 ? (double?)null : (double)Answered / Requested; }
        }
    }

    public static class SupplierStats
    {
        /// <summary>
        /// 全案件の調達先の記録から、調達先ごとの実績を作る。
        /// 「最安」は、2社以上が単価を出した数量パターンでだけ数える（1社だけなら比べようがないため）【仮置き】。
        /// </summary>
        public static IReadOnlyList<SupplierStat> Compute(
            IReadOnlyDictionary<long, IReadOnlyList<AnkenSupplier>> byAnken,
            Func<long, IReadOnlyList<Quote>> quotesOf)
        {
            var map = new Dictionary<long, SupplierStat>();
            var replyDays = new Dictionary<long, List<double>>();

            foreach (var kv in byAnken)
            {
                foreach (var s in kv.Value)
                {
                    SupplierStat st;
                    if (!map.TryGetValue(s.SupplierId, out st))
                    {
                        st = new SupplierStat { SupplierId = s.SupplierId, Name = s.SupplierName, ShortName = s.ShortName };
                        map[s.SupplierId] = st;
                        replyDays[s.SupplierId] = new List<double>();
                    }

                    st.Ankens++;
                    if (s.SentAt.HasValue)
                    {
                        st.Requested++;
                    }

                    if (s.IsAnswered)
                    {
                        st.Answered++;
                    }

                    if (s.IsDeclined)
                    {
                        st.Declined++;
                    }

                    if (s.SentAt.HasValue && s.ReceivedAt.HasValue)
                    {
                        replyDays[s.SupplierId].Add((s.ReceivedAt.Value.Date - s.SentAt.Value.Date).TotalDays);
                    }
                }

                var quotes = quotesOf(kv.Key);
                var contested = quotes.Where(q => q.UnitPrice.HasValue).GroupBy(q => q.PatternId)
                    .Where(g => g.Select(q => q.SupplierId).Distinct().Count() >= 2)
                    .SelectMany(g => g).ToList();
                foreach (var cheapest in Comparison.CheapestQuotes(contested).Select(q => q.SupplierId).Distinct())
                {
                    SupplierStat st;
                    if (map.TryGetValue(cheapest, out st))
                    {
                        st.CheapestAnkens++;
                    }
                }
            }

            foreach (var st in map.Values)
            {
                var days = replyDays[st.SupplierId];
                st.AverageReplyDays = days.Count == 0 ? (double?)null : days.Average();
            }

            return map.Values.OrderByDescending(s => s.Ankens).ThenBy(s => s.Name, StringComparer.Ordinal).ToList();
        }
    }
}
