using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AnkenDesk.Core
{
    /// <summary>見積の有効期限の注意1件。</summary>
    public sealed class ValidityAlert
    {
        public AnkenSupplier Supplier { get; set; } = new AnkenSupplier();

        /// <summary>有効期限までの日数。切れていれば負の数。</summary>
        public int DaysLeft { get; set; }

        public bool Expired
        {
            get { return DaysLeft < 0; }
        }

        public string Text { get; set; } = "";
    }

    /// <summary>
    /// 調達先の見積（回答）の有効期限。切れた見積の単価は、そのままは客先へ出せない・発注できないので、
    /// 切れる前に気づけるようにする。回答済みで、有効期限が入っているものだけが対象。
    /// </summary>
    public static class QuoteValidity
    {
        /// <summary>何日前から「間近」として知らせるか。</summary>
        public const int WarnDays = 7;

        /// <summary>期限切れ・間近（<see cref="WarnDays"/> 日以内）の見積。期限の近い順（切れているものが先）。</summary>
        public static IReadOnlyList<ValidityAlert> Alerts(IEnumerable<AnkenSupplier> suppliers, DateTime today)
        {
            var result = new List<ValidityAlert>();
            foreach (var s in suppliers)
            {
                if (!s.IsAnswered || !s.ValidUntil.HasValue)
                {
                    continue;
                }

                var days = (s.ValidUntil.Value.Date - today.Date).Days;
                if (days > WarnDays)
                {
                    continue;
                }

                var date = s.ValidUntil.Value.ToString("M/d", CultureInfo.InvariantCulture);
                result.Add(new ValidityAlert
                {
                    Supplier = s,
                    DaysLeft = days,
                    Text = days < 0 ? s.ShortName + ": 有効期限切れ（" + (-days) + "日前）" : days == 0 ? s.ShortName + ": 本日まで" : s.ShortName + ": " + date + "まで（あと" + days + "日）",
                });
            }

            return result.OrderBy(a => a.DaysLeft).ToList();
        }

        /// <summary>案件画面の表の「有効期限」の行の文字。</summary>
        public static string Text(AnkenSupplier s, DateTime today)
        {
            if (!s.ValidUntil.HasValue)
            {
                return "";
            }

            var days = (s.ValidUntil.Value.Date - today.Date).Days;
            var date = s.ValidUntil.Value.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
            if (!s.IsAnswered)
            {
                return date;
            }

            return days < 0 ? date + "\r\n【期限切れ " + (-days) + "日前】" : days <= WarnDays ? date + "\r\n（あと" + days + "日）" : date;
        }
    }
}
