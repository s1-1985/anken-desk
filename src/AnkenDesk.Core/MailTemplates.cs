using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AnkenDesk.Core
{
    /// <summary>メールの件名・本文の雛形と、差し込み。差し込みは調達先ごとに行う。</summary>
    public static class MailTemplates
    {
        public const string SettingPrefix = "mail_template:";

        public const string RequestSubject = "見積検討のお願い（{品番}）";

        public const string RequestBody =
            "{調達先名} 御中\n\n"
            + "お世話になっております。\n\n"
            + "下記の案件につきまして、添付の見積依頼書の内容でお見積りをお願いいたします。\n\n"
            + "品番: {品番}\n"
            + "品名: {品名}\n"
            + "回答希望日: {回答希望期日}\n\n"
            + "お忙しいところ恐縮ですが、よろしくお願いいたします。";

        public const string ReminderSubject = "【ご確認】見積検討のお願い（{品番}）";

        public const string ReminderBody =
            "{調達先名} 御中\n\n"
            + "お世話になっております。\n\n"
            + "先日お送りしました下記の案件の見積依頼につきまして、ご回答の状況はいかがでしょうか。\n\n"
            + "品番: {品番}\n"
            + "品名: {品名}\n"
            + "回答希望日: {回答希望期日}\n\n"
            + "お忙しいところ恐縮ですが、ご確認のほどよろしくお願いいたします。\n"
            + "行き違いでご回答済みの場合は、失礼をお許しください。";

        public static string DefaultSubject(MailKind kind)
        {
            return kind == MailKind.Request ? RequestSubject : ReminderSubject;
        }

        public static string DefaultBody(MailKind kind)
        {
            return kind == MailKind.Request ? RequestBody : ReminderBody;
        }

        /// <summary>差し込みできる語（画面の説明に出す）。</summary>
        public static readonly string[] Placeholders = { "{調達先名}", "{品番}", "{品名}", "{回答希望期日}" };

        /// <summary>
        /// 差し込む。値が空の語を含む行は、行ごと消す（例: 品名が空なら「品名: 」の行）。
        /// 件名のように1行の文字列で、空の語があるときは、その語だけを空にする。
        /// </summary>
        public static string Apply(string template, Supplier supplier, AnkenRecord anken, DateTime dueDate)
        {
            var values = new Dictionary<string, string>
            {
                { "{調達先名}", supplier.Name },
                { "{品番}", anken.PartNumber },
                { "{品名}", anken.PartName },
                { "{回答希望期日}", FormatDate(dueDate) },
            };

            var text = (template ?? "").Replace("\r\n", "\n");
            var lines = text.Split('\n');
            if (lines.Length == 1)
            {
                return ReplaceAll(lines[0], values);
            }

            var result = new List<string>();
            foreach (var line in lines)
            {
                var emptyHit = values.Any(kv => string.IsNullOrWhiteSpace(kv.Value) && line.Contains(kv.Key));
                if (emptyHit)
                {
                    continue;
                }

                result.Add(ReplaceAll(line, values));
            }

            return string.Join("\n", result);
        }

        /// <summary>「2026年10月5日(月)」の形。</summary>
        public static string FormatDate(DateTime d)
        {
            return d.ToString("yyyy年M月d日", CultureInfo.InvariantCulture) + "(" + "日月火水木金土"[(int)d.DayOfWeek] + ")";
        }

        private static string ReplaceAll(string text, Dictionary<string, string> values)
        {
            foreach (var kv in values)
            {
                text = text.Replace(kv.Key, kv.Value);
            }

            return text;
        }
    }
}
