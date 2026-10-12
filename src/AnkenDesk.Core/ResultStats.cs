using System;
using System.Collections.Generic;
using System.Linq;

namespace AnkenDesk.Core
{
    /// <summary>勝敗・粗利の集計の1行（得意先別、調達先別、月別などの1つ分）。</summary>
    public sealed class ResultStatRow
    {
        public string Label { get; set; } = "";
        public int Won { get; set; }
        public int Lost { get; set; }
        public int OnHold { get; set; }

        /// <summary>見積中・客先提出済み（まだ結果が出ていない）。</summary>
        public int Open { get; set; }

        /// <summary>受注率 = 受注 ÷（受注＋失注）。結果が出たものが無ければnull。保留・未決は含めない。</summary>
        public decimal? WinRate
        {
            get { return Won + Lost == 0 ? (decimal?)null : Math.Round((decimal)Won / (Won + Lost) * 100m, 1, MidpointRounding.AwayFromZero); }
        }

        /// <summary>受注した案件の、平均の粗利率（%）。提出単価と仕入単価が入っている数量だけで、案件ごとに平均してから、案件の平均を取る。</summary>
        public decimal? AverageMargin { get; set; }

        public int Decided
        {
            get { return Won + Lost; }
        }
    }

    public sealed class ResultReport
    {
        public ResultStatRow Overall { get; set; } = new ResultStatRow { Label = "全体" };
        public IReadOnlyList<ResultStatRow> ByClient { get; set; } = new List<ResultStatRow>();

        /// <summary>採用された調達先ごとの、受注した案件の数と平均の粗利率。</summary>
        public IReadOnlyList<ResultStatRow> BySupplier { get; set; } = new List<ResultStatRow>();
        public IReadOnlyList<ResultStatRow> ByMonth { get; set; } = new List<ResultStatRow>();

        /// <summary>失注・保留の理由ごとの件数（理由が空のものは「（未入力）」）。</summary>
        public IReadOnlyList<KeyValuePair<string, int>> LossReasons { get; set; } = new List<KeyValuePair<string, int>>();
    }

    /// <summary>
    /// 勝敗・粗利の集計。期間は、結果の日（無ければ依頼日）で絞る。
    /// 「受注」の粗利は、数量ごとに選んだ調達先の単価（仕入）と提出単価から（数量や金額の重みは付けず、率だけ）。
    /// </summary>
    public static class ResultStats
    {
        public static ResultReport Compute(AnkenDb db, DateTime? from, DateTime? to)
        {
            var ankens = db.ListAnkens().Where(a =>
            {
                var d = (a.ResultDate ?? a.RequestDate).Date;
                return (!from.HasValue || d >= from.Value.Date) && (!to.HasValue || d <= to.Value.Date);
            }).ToList();

            var margins = new Dictionary<long, decimal>();
            foreach (var a in ankens.Where(a => a.Status == AnkenStatus.Won))
            {
                var m = AnkenMargin(db, a);
                if (m.HasValue)
                {
                    margins[a.Id] = m.Value;
                }
            }

            var suppliers = db.ListSuppliers().ToDictionary(s => s.Id, s => s.Name);
            var report = new ResultReport();
            report.Overall = Row("全体", ankens, margins);
            report.ByClient = ankens.GroupBy(a => a.ClientName).Select(g => Row(g.Key, g.ToList(), margins)).OrderByDescending(r => r.Won + r.Lost + r.OnHold + r.Open).ThenBy(r => r.Label, StringComparer.Ordinal).ToList();
            report.ByMonth = ankens.GroupBy(a => (a.ResultDate ?? a.RequestDate).ToString("yyyy/MM")).Select(g => Row(g.Key, g.ToList(), margins)).OrderByDescending(r => r.Label, StringComparer.Ordinal).ToList();
            report.BySupplier = ankens.Where(a => a.Status == AnkenStatus.Won && a.AdoptedSupplierId.HasValue)
                .GroupBy(a => a.AdoptedSupplierId!.Value)
                .Select(g => Row(suppliers.ContainsKey(g.Key) ? suppliers[g.Key] : "(削除された調達先)", g.ToList(), margins))
                .OrderByDescending(r => r.Won).ThenBy(r => r.Label, StringComparer.Ordinal).ToList();
            report.LossReasons = ankens.Where(a => a.Status == AnkenStatus.Lost || a.Status == AnkenStatus.OnHold)
                .GroupBy(a => string.IsNullOrWhiteSpace(a.ResultReason) ? "（未入力）" : a.ResultReason)
                .Select(g => new KeyValuePair<string, int>(g.Key, g.Count()))
                .OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).ToList();
            return report;
        }

        private static ResultStatRow Row(string label, IReadOnlyList<AnkenRecord> ankens, IReadOnlyDictionary<long, decimal> margins)
        {
            var won = ankens.Where(a => a.Status == AnkenStatus.Won).ToList();
            var withMargin = won.Where(a => margins.ContainsKey(a.Id)).Select(a => margins[a.Id]).ToList();
            return new ResultStatRow
            {
                Label = label,
                Won = won.Count,
                Lost = ankens.Count(a => a.Status == AnkenStatus.Lost),
                OnHold = ankens.Count(a => a.Status == AnkenStatus.OnHold),
                Open = ankens.Count(a => a.Status == AnkenStatus.InProgress || a.Status == AnkenStatus.Submitted),
                AverageMargin = withMargin.Count == 0 ? (decimal?)null : Math.Round(withMargin.Average(), 1, MidpointRounding.AwayFromZero),
            };
        }

        /// <summary>1案件の平均の粗利率（%）。提出単価と、その仕入単価（選んだ調達先の単価）がそろった数量だけで平均する。無ければnull。</summary>
        public static decimal? AnkenMargin(AnkenDb db, AnkenRecord anken)
        {
            var quotes = db.ListQuotes(anken.Id);
            var rates = new List<decimal>();
            foreach (var c in db.ListClientPrices(anken.Id))
            {
                if (!c.SellingPrice.HasValue || !c.AdoptedSupplierId.HasValue)
                {
                    continue;
                }

                var q = quotes.FirstOrDefault(x => x.SupplierId == c.AdoptedSupplierId.Value && x.PatternId == c.PatternId && x.UnitPrice.HasValue);
                if (q == null)
                {
                    continue;
                }

                var rate = Margin.GrossMarginPercent(c.SellingPrice.Value, q.UnitPrice!.Value);
                if (rate.HasValue)
                {
                    rates.Add(rate.Value);
                }
            }

            return rates.Count == 0 ? (decimal?)null : Math.Round(rates.Average(), 1, MidpointRounding.AwayFromZero);
        }
    }
}
