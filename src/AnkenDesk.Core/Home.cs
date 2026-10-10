using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AnkenDesk.Core
{
    public enum HomeFilter
    {
        /// <summary>要対応: 調達先が1社以上あり、未回答の調達先がある案件。</summary>
        NeedsAction,
        All,

        /// <summary>回答済み: 調達先が1社以上あり、全社が回答済みの案件。</summary>
        Answered,

        /// <summary>今週期限: 要対応のうち、回答期限が今日から7日以内（期限超過も含む）。</summary>
        DueThisWeek,

        /// <summary>終了・保留: 受注・失注・保留にした案件。</summary>
        Closed,
    }

    /// <summary>ホームの一覧の1行（1案件）。</summary>
    public sealed class HomeRow
    {
        public AnkenRecord Anken { get; set; } = new AnkenRecord();
        public int Answered { get; set; }
        public int Total { get; set; }

        /// <summary>回答期限までの日数。期限を過ぎていれば負の数。</summary>
        public int DaysToDue { get; set; }

        /// <summary>未回答の調達先の略称。</summary>
        public IReadOnlyList<string> PendingNames { get; set; } = new List<string>();

        /// <summary>この案件に加えてある調達先の名前と略称（検索用）。</summary>
        public IReadOnlyList<string> SupplierNames { get; set; } = new List<string>();

        /// <summary>見積依頼を送った（依頼送付日がある）のに、まだ回答が無い調達先。催促の相手。</summary>
        public IReadOnlyList<long> RemindSupplierIds { get; set; } = new List<long>();

        /// <summary>次の操作が「催促メールを作成」になる。</summary>
        public bool CanRemind
        {
            get { return RemindSupplierIds.Count > 0; }
        }

        public bool NeedsAction
        {
            get { return !AnkenStatus.IsClosed(Anken.Status) && Total > 0 && Answered < Total; }
        }

        public bool IsAnswered
        {
            get { return Total > 0 && Answered == Total; }
        }

        /// <summary>状態を文字で返す（色だけに頼らない）。</summary>
        public string StatusText
        {
            get
            {
                if (AnkenStatus.IsClosed(Anken.Status))
                {
                    return Anken.Status;
                }

                if (Total == 0)
                {
                    return "依頼先なし";
                }

                if (IsAnswered)
                {
                    return "回答済み";
                }

                return Comparison.DueStatus(Anken.ReplyDueDate, Anken.ReplyDueDate.AddDays(-DaysToDue));
            }
        }
    }

    /// <summary>件数タイル。いずれも要対応の案件の中での件数。</summary>
    public sealed class HomeSummary
    {
        public int Overdue { get; set; }
        public int Today { get; set; }

        /// <summary>期限まであと1〜3日。</summary>
        public int Within3Days { get; set; }

        /// <summary>要対応の案件の数（未回答の調達先がある案件）。</summary>
        public int Waiting { get; set; }
    }

    public static class Home
    {
        /// <summary>
        /// 一覧の行を作る。並びは、要対応が先（回答期限の近い順）、そのあとは依頼日の新しい順。
        /// </summary>
        public static IReadOnlyList<HomeRow> Build(
            IEnumerable<AnkenRecord> ankens,
            IReadOnlyDictionary<long, IReadOnlyList<AnkenSupplier>> suppliers,
            DateTime today)
        {
            var rows = new List<HomeRow>();
            foreach (var a in ankens)
            {
                IReadOnlyList<AnkenSupplier>? list;
                if (!suppliers.TryGetValue(a.Id, out list))
                {
                    list = new List<AnkenSupplier>();
                }

                rows.Add(new HomeRow
                {
                    Anken = a,
                    Total = list.Count,
                    Answered = list.Count(s => s.IsAnswered),
                    DaysToDue = (a.ReplyDueDate.Date - today.Date).Days,
                    PendingNames = list.Where(s => !s.IsAnswered).Select(s => s.ShortName).ToList(),
                    RemindSupplierIds = list.Where(s => !s.IsAnswered && s.SentAt != null).Select(s => s.SupplierId).ToList(),
                    SupplierNames = list.SelectMany(s => new[] { s.SupplierName, s.ShortName }).Distinct().ToList(),
                });
            }

            return rows
                .OrderBy(r => r.NeedsAction ? 0 : 1)
                .ThenBy(r => r.NeedsAction ? r.Anken.ReplyDueDate : DateTime.MaxValue)
                .ThenByDescending(r => r.Anken.RequestDate)
                .ThenByDescending(r => r.Anken.Id)
                .ToList();
        }

        public static HomeSummary Summarize(IEnumerable<HomeRow> rows)
        {
            var s = new HomeSummary();
            foreach (var r in rows.Where(x => x.NeedsAction))
            {
                s.Waiting++;
                if (r.DaysToDue < 0)
                {
                    s.Overdue++;
                }
                else if (r.DaysToDue == 0)
                {
                    s.Today++;
                }
                else if (r.DaysToDue <= 3)
                {
                    s.Within3Days++;
                }
            }

            return s;
        }

        /// <summary>タブの絞り込みと、検索（品番・日付・調達先など）。検索は、空白で区切った語がすべて含まれる行だけ残す。</summary>
        public static IReadOnlyList<HomeRow> Filter(IEnumerable<HomeRow> rows, HomeFilter filter, string? query)
        {
            IEnumerable<HomeRow> result = rows;
            if (filter == HomeFilter.NeedsAction)
            {
                result = result.Where(r => r.NeedsAction);
            }
            else if (filter == HomeFilter.Answered)
            {
                result = result.Where(r => r.IsAnswered && !AnkenStatus.IsClosed(r.Anken.Status));
            }
            else if (filter == HomeFilter.DueThisWeek)
            {
                result = result.Where(r => r.NeedsAction && r.DaysToDue <= 7);
            }
            else if (filter == HomeFilter.Closed)
            {
                result = result.Where(r => AnkenStatus.IsClosed(r.Anken.Status));
            }
            else
            {
                // すべて
            }

            return result.Where(r => Matches(r, query)).ToList();
        }

        /// <summary>検索語（空白は半角でも全角でもよい）がすべて、品番・品名・備考・得意先・調達先・依頼日のどれかに含まれるか。</summary>
        public static bool Matches(HomeRow row, string? query)
        {
            var tokens = (query ?? "").Split(new[] { ' ', '　' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
            {
                return true;
            }

            var a = row.Anken;
            var fields = new List<string>
            {
                a.PartNumber, a.PartName, a.Note, a.ClientName,
                a.RequestDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
                a.RequestDate.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture),
                a.RequestDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            };
            fields.AddRange(row.SupplierNames);

            return tokens.All(t => fields.Any(f => f.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        /// <summary>
        /// 催促が要る案件: 見積依頼を送ったのに未回答の調達先があり、回答期限が明日以前（期限超過も含む）。
        /// 期限の近い順。
        /// </summary>
        public static IReadOnlyList<HomeRow> RemindCandidates(IEnumerable<HomeRow> rows)
        {
            return rows.Where(r => r.NeedsAction && r.CanRemind && r.DaysToDue <= 1)
                .OrderBy(r => r.Anken.ReplyDueDate).ThenBy(r => r.Anken.Id).ToList();
        }

        /// <summary>状態の頭につける記号。色だけに頼らず、文字でも区別できるようにする（CLAUDE.md）。</summary>
        public static string StatusMark(HomeRow r)
        {
            if (AnkenStatus.IsClosed(r.Anken.Status))
            {
                return "－ ";
            }

            if (r.IsAnswered)
            {
                return "○ ";
            }

            if (!r.NeedsAction)
            {
                return "・ ";
            }

            if (r.DaysToDue < 0)
            {
                return "▲ ";
            }

            return r.DaysToDue <= 1 ? "△ " : "□ ";
        }

        /// <summary>一覧に出す案件名: 「YYYYMMDD　品番」（備考があれば続けて）。</summary>
        public static string Title(AnkenRecord a)
        {
            return FolderNames.BuildAnkenFolderName(a.RequestDate, a.PartNumber, a.Note);
        }
    }
}
