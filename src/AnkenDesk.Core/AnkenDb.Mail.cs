using System;
using System.Collections.Generic;
using System.Globalization;

namespace AnkenDesk.Core
{
    // メールの記録と、メールの雛形。
    public sealed partial class AnkenDb
    {
        public void AddMailLog(MailLogEntry e)
        {
            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO mail_log (anken_id, supplier_id, kind, to_address, subject, status, entry_id, sent_at, msg_path, created_at) "
                    + "VALUES ($a, $s, $k, $to, $sub, $st, $eid, $sent, $msg, $at)";
                cmd.Parameters.AddWithValue("$a", e.AnkenId);
                cmd.Parameters.AddWithValue("$s", e.SupplierId.HasValue ? (object)e.SupplierId.Value : DBNull.Value);
                cmd.Parameters.AddWithValue("$k", MailSender.KindText(e.Kind));
                cmd.Parameters.AddWithValue("$to", e.ToAddress);
                cmd.Parameters.AddWithValue("$sub", e.Subject);
                cmd.Parameters.AddWithValue("$st", e.Status);
                cmd.Parameters.AddWithValue("$eid", (object?)e.EntryId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$sent", e.SentAt.HasValue ? (object)e.SentAt.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : DBNull.Value);
                cmd.Parameters.AddWithValue("$msg", (object?)e.MsgPath ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$at", e.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>案件のメールの記録。新しいものが先。</summary>
        public IReadOnlyList<MailLogEntry> ListMailLog(long ankenId)
        {
            var list = new List<MailLogEntry>();
            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT id, supplier_id, kind, to_address, subject, status, entry_id, sent_at, msg_path, created_at "
                    + "FROM mail_log WHERE anken_id = $a ORDER BY created_at DESC, id DESC";
                cmd.Parameters.AddWithValue("$a", ankenId);
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        list.Add(new MailLogEntry
                        {
                            Id = r.GetInt64(0),
                            AnkenId = ankenId,
                            SupplierId = r.IsDBNull(1) ? (long?)null : r.GetInt64(1),
                            Kind = r.GetString(2) == "催促" ? MailKind.Reminder : MailKind.Request,
                            ToAddress = r.GetString(3),
                            Subject = r.GetString(4),
                            Status = r.GetString(5),
                            EntryId = r.IsDBNull(6) ? null : r.GetString(6),
                            SentAt = r.IsDBNull(7) ? (DateTime?)null : DateTime.ParseExact(r.GetString(7), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                            MsgPath = r.IsDBNull(8) ? null : r.GetString(8),
                            CreatedAt = DateTime.ParseExact(r.GetString(9), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                        });
                    }
                }
            }

            return list;
        }

        /// <summary>見積依頼を送ったので、その調達先の「依頼送付日」を入れる。</summary>
        public void MarkRequestSent(long ankenId, long supplierId, DateTime date)
        {
            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "UPDATE anken_supplier SET sent_at = $d WHERE anken_id = $a AND supplier_id = $s";
                cmd.Parameters.AddWithValue("$d", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                cmd.Parameters.AddWithValue("$a", ankenId);
                cmd.Parameters.AddWithValue("$s", supplierId);
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>メールの件名・本文の雛形（設定で変えていなければ、既定の文章）。</summary>
        public void GetMailTemplate(MailKind kind, out string subject, out string body)
        {
            var key = MailTemplates.SettingPrefix + (kind == MailKind.Request ? "request" : "reminder");
            subject = GetSetting(key + ":subject") ?? MailTemplates.DefaultSubject(kind);
            body = GetSetting(key + ":body") ?? MailTemplates.DefaultBody(kind);
        }

        public void SetMailTemplate(MailKind kind, string subject, string body)
        {
            var key = MailTemplates.SettingPrefix + (kind == MailKind.Request ? "request" : "reminder");
            SetSetting(key + ":subject", subject ?? "");
            SetSetting(key + ":body", body ?? "");
        }
    }
}
