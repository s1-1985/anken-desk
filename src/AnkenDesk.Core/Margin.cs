using System;

namespace AnkenDesk.Core
{
    /// <summary>
    /// 客先への提出単価と粗利の計算。仕入単価（調達先の単価）から、掛け率・粗利率で提出単価を出す、
    /// または提出単価から粗利を出す。金額は小数第2位まで（四捨五入）。
    /// 用語: 掛け率（上乗せ）= 仕入に何%を上乗せするか。粗利率 = 粗利 ÷ 提出単価。
    /// </summary>
    public static class Margin
    {
        public static decimal Round(decimal value)
        {
            return Math.Round(value, 2, MidpointRounding.AwayFromZero);
        }

        /// <summary>仕入に <paramref name="markupPercent"/> %を上乗せした提出単価。例: 100円に20% → 120円。</summary>
        public static decimal SellingFromMarkup(decimal cost, decimal markupPercent)
        {
            return Round(cost * (1 + markupPercent / 100m));
        }

        /// <summary>粗利率 <paramref name="marginPercent"/> %になる提出単価。例: 80円で粗利率20% → 100円。粗利率が100%以上なら null。</summary>
        public static decimal? SellingFromGrossMargin(decimal cost, decimal marginPercent)
        {
            if (marginPercent >= 100m)
            {
                return null;
            }

            return Round(cost / (1 - marginPercent / 100m));
        }

        /// <summary>粗利（提出単価 − 仕入単価）。</summary>
        public static decimal GrossProfit(decimal selling, decimal cost)
        {
            return Round(selling - cost);
        }

        /// <summary>粗利率（%、小数第1位）。提出単価が0なら null。</summary>
        public static decimal? GrossMarginPercent(decimal selling, decimal cost)
        {
            if (selling == 0)
            {
                return null;
            }

            return Math.Round((selling - cost) / selling * 100m, 1, MidpointRounding.AwayFromZero);
        }

        /// <summary>上乗せ率（%、小数第1位）。仕入が0なら null。</summary>
        public static decimal? MarkupPercent(decimal selling, decimal cost)
        {
            if (cost == 0)
            {
                return null;
            }

            return Math.Round((selling - cost) / cost * 100m, 1, MidpointRounding.AwayFromZero);
        }
    }
}
