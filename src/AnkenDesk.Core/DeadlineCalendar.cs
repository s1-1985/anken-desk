using System;
using System.Collections.Generic;
using System.Linq;

namespace AnkenDesk.Core
{
    /// <summary>Outlookの予定表（実体は OutlookAccess）。画面やテストから差し替えられるようにしてある。</summary>
    public interface ICalendarGateway : IDisposable
    {
        /// <summary>
        /// 終日の予定を作る。<paramref name="existingEntryId"/> があって、その予定が見つかれば、作り直さずに更新する。
        /// 戻り値は、予定のEntryID。
        /// </summary>
        string AddOrUpdateAllDay(string? existingEntryId, string subject, DateTime date, string body, int reminderMinutesBeforeStart);

        /// <summary>予定を削除する（見つからなければ何もしない）。</summary>
        void Delete(string entryId);
    }

    /// <summary>回答期限を、Outlookの予定表に入れる。件名・本文の作り方を決める。</summary>
    public static class DeadlineCalendar
    {
        /// <summary>前日の朝9時に通知（終日の予定は、当日の0時が基準なので、15時間前）。</summary>
        public const int ReminderMinutes = 15 * 60;

        public static string Subject(AnkenRecord anken)
        {
            return "【回答期限】" + Home.Title(anken) + (string.IsNullOrWhiteSpace(anken.PartName) ? "" : "　" + anken.PartName);
        }

        public static string Body(AnkenRecord anken, IEnumerable<AnkenSupplier> suppliers)
        {
            var list = suppliers.ToList();
            var pending = list.Where(s => !s.IsAnswered).Select(s => s.ShortName).ToList();
            return "得意先・種別: " + anken.ClientName + "\r\n"
                + "回答状況: " + list.Count(s => s.IsAnswered) + "/" + list.Count + "社"
                + (pending.Count > 0 ? "（未回答: " + string.Join("、", pending) + "）" : "") + "\r\n"
                + "案件フォルダ: " + anken.FolderPath + "\r\n\r\n"
                + "※案件デスクが作った予定です（回答期限を変えたときは、案件画面から更新できます）。";
        }

        /// <summary>予定を作る・更新して、EntryIDをDBに記録する。</summary>
        public static string Sync(ICalendarGateway calendar, AnkenDb db, AnkenRecord anken)
        {
            var entry = calendar.AddOrUpdateAllDay(
                anken.CalendarEntryId, Subject(anken), anken.ReplyDueDate.Date, Body(anken, db.ListAnkenSuppliers(anken.Id)), ReminderMinutes);
            db.SetCalendarEntryId(anken.Id, entry);
            return entry;
        }

        /// <summary>予定を削除して、記録も消す。予定が無い案件では何もしない。</summary>
        public static void Remove(ICalendarGateway calendar, AnkenDb db, AnkenRecord anken)
        {
            if (anken.CalendarEntryId != null)
            {
                calendar.Delete(anken.CalendarEntryId);
                db.SetCalendarEntryId(anken.Id, null);
            }
        }
    }
}
