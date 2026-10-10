using System;
using System.Runtime.InteropServices;
using AnkenDesk.Core;

namespace AnkenDesk.OutlookAccess
{
    /// <summary>
    /// Outlookの予定表に、回答期限の終日の予定を入れる（COM、レイトバインド）。
    /// 起動中のOutlookがあれば接続し、無ければ自分で起動する（自分で起動したときだけ、終了時にQuitする）。
    /// 1つのインスタンスは、1つのスレッド（STA）の中で使い切る。COM参照は必ず解放する。
    /// </summary>
    public sealed class OutlookCalendar : ICalendarGateway
    {
        private const int OlAppointmentItem = 1;
        private const int OlFolderCalendar = 9;
        private const int OlFree = 0;
        private const string Category = "案件デスク";

        private dynamic? _app;
        private dynamic? _ns;
        private bool _startedByUs;

        public OutlookCalendar()
        {
            try
            {
                _app = Marshal.GetActiveObject("Outlook.Application");
            }
            catch (COMException)
            {
                var type = Type.GetTypeFromProgID("Outlook.Application");
                if (type == null)
                {
                    throw new InvalidOperationException("Outlook（デスクトップ版）が見つかりません。");
                }

                _app = Activator.CreateInstance(type);
                _startedByUs = true;
            }

            _ns = _app!.GetNamespace("MAPI");
        }

        public string AddOrUpdateAllDay(string? existingEntryId, string subject, DateTime date, string body, int reminderMinutesBeforeStart)
        {
            dynamic? item = null;
            try
            {
                if (!string.IsNullOrEmpty(existingEntryId))
                {
                    try
                    {
                        item = _ns!.GetItemFromID(existingEntryId);
                    }
                    catch (COMException)
                    {
                        item = null; // 削除されている。作り直す。
                    }
                }

                if (item == null)
                {
                    item = _app!.CreateItem(OlAppointmentItem);
                }

                item.Subject = subject;
                item.AllDayEvent = true;
                item.Start = date.Date;
                item.End = date.Date.AddDays(1);
                item.Body = body;
                item.BusyStatus = OlFree; // 予定表を「予定あり」で埋めない
                item.Categories = Category;
                item.ReminderSet = true;
                item.ReminderMinutesBeforeStart = reminderMinutesBeforeStart;
                item.Save();
                return (string)item.EntryID;
            }
            finally
            {
                Release(item);
            }
        }

        public void Delete(string entryId)
        {
            dynamic? item = null;
            try
            {
                try
                {
                    item = _ns!.GetItemFromID(entryId);
                }
                catch (COMException)
                {
                    return; // もう無い
                }

                item.Delete();
            }
            finally
            {
                Release(item);
            }
        }

        public void Dispose()
        {
            var app = _app;
            var ns = _ns;
            _app = null;
            _ns = null;

            if (app != null && _startedByUs)
            {
                try
                {
                    app!.Quit();
                }
                catch (COMException)
                {
                }
            }

            Release(ns);
            Release(app);
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        private static void Release(object? com)
        {
            if (com != null && Marshal.IsComObject(com))
            {
                try
                {
                    Marshal.ReleaseComObject(com);
                }
                catch (ArgumentException)
                {
                }
            }
        }
    }

    /// <summary>Outlookとの接続の状態を調べる（診断用。読むだけ）。</summary>
    public static class OutlookStatusCheck
    {
        public sealed class Result
        {
            public bool Running { get; set; }
            public bool StartedByCheck { get; set; }
            public bool Offline { get; set; }
            public string Version { get; set; } = "";
            public string Profile { get; set; } = "";
            public int InboxCount { get; set; }
            public int InboxUnread { get; set; }
            public string Error { get; set; } = "";
        }

        public static Result Run()
        {
            var result = new Result();
            dynamic? app = null;
            dynamic? ns = null;
            dynamic? inbox = null;
            try
            {
                try
                {
                    app = Marshal.GetActiveObject("Outlook.Application");
                    result.Running = true;
                }
                catch (COMException)
                {
                    var type = Type.GetTypeFromProgID("Outlook.Application");
                    if (type == null)
                    {
                        result.Error = "Outlook（デスクトップ版）が見つかりません。";
                        return result;
                    }

                    app = Activator.CreateInstance(type);
                    result.StartedByCheck = true;
                }

                ns = app!.GetNamespace("MAPI");
                result.Version = (string)app.Version;
                try
                {
                    result.Offline = (bool)ns.Offline;
                }
                catch (COMException)
                {
                }

                try
                {
                    result.Profile = (string)ns.CurrentProfileName;
                }
                catch (COMException)
                {
                }

                try
                {
                    inbox = ns.GetDefaultFolder(6);
                    result.InboxCount = (int)inbox.Items.Count;
                    result.InboxUnread = (int)inbox.UnReadItemCount;
                }
                catch (COMException)
                {
                }
            }
            catch (COMException ex)
            {
                result.Error = ex.Message;
            }
            finally
            {
                if (result.StartedByCheck && app != null)
                {
                    try
                    {
                        app.Quit();
                    }
                    catch (COMException)
                    {
                    }
                }

                foreach (object? o in new object?[] { inbox, ns, app })
                {
                    if (o != null && Marshal.IsComObject(o))
                    {
                        try
                        {
                            Marshal.ReleaseComObject(o);
                        }
                        catch (ArgumentException)
                        {
                        }
                    }
                }
            }

            return result;
        }
    }
}
