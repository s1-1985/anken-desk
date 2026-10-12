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
                cmd.CommandText = "INSERT INTO mail_log (anken_id, supplier_id, kind, to_address, subject, status, entry_id, sent_at, msg_path, created_at, conversation_id) "
                    + "VALUES ($a, $s, $k, $to, $sub, $st, $eid, $sent, $msg, $at, $conv)";
                cmd.Parameters.AddWithValue("$conv", (object?)e.ConversationId ?? DBNull.Value);
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
                cmd.CommandText = "SELECT id, supplier_id, kind, to_address, subject, status, entry_id, sent_at, msg_path, created_at, conversation_id "
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
                            Kind = KindFromText(r.GetString(2)),
                            ToAddress = r.GetString(3),
                            Subject = r.GetString(4),
                            Status = r.GetString(5),
                            EntryId = r.IsDBNull(6) ? null : r.GetString(6),
                            SentAt = r.IsDBNull(7) ? (DateTime?)null : DateTime.ParseExact(r.GetString(7), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                            MsgPath = r.IsDBNull(8) ? null : r.GetString(8),
                            CreatedAt = DateTime.ParseExact(r.GetString(9), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                            ConversationId = r.IsDBNull(10) ? null : r.GetString(10),
                        });
                    }
                }
            }

            return list;
        }

        /// <summary>全案件のメールの記録（横断検索用）。</summary>
        public IReadOnlyList<MailLogEntry> ListAllMailLog()
        {
            var list = new List<MailLogEntry>();
            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT id, anken_id, supplier_id, kind, to_address, subject, status, entry_id, sent_at, msg_path, created_at, conversation_id FROM mail_log ORDER BY created_at DESC, id DESC";
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        list.Add(new MailLogEntry
                        {
                            Id = r.GetInt64(0),
                            AnkenId = r.GetInt64(1),
                            SupplierId = r.IsDBNull(2) ? (long?)null : r.GetInt64(2),
                            Kind = KindFromText(r.GetString(3)),
                            ToAddress = r.GetString(4),
                            Subject = r.GetString(5),
                            Status = r.GetString(6),
                            EntryId = r.IsDBNull(7) ? null : r.GetString(7),
                            SentAt = r.IsDBNull(8) ? (DateTime?)null : DateTime.ParseExact(r.GetString(8), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                            MsgPath = r.IsDBNull(9) ? null : r.GetString(9),
                            CreatedAt = DateTime.ParseExact(r.GetString(10), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                            ConversationId = r.IsDBNull(11) ? null : r.GetString(11),
                        });
                    }
                }
            }

            return list;
        }

        /// <summary>送った依頼・催促のメールのうち、会話IDが無いもの（EntryIDはある）。会話IDを補うために使う。</summary>
        public IReadOnlyList<KeyValuePair<long, string>> ListMailsMissingConversation()
        {
            var list = new List<KeyValuePair<long, string>>();
            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT id, entry_id FROM mail_log WHERE kind IN ('依頼', '催促') AND status = '送信済み' AND entry_id IS NOT NULL AND (conversation_id IS NULL OR conversation_id = '') ORDER BY id";
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        list.Add(new KeyValuePair<long, string>(r.GetInt64(0), r.GetString(1)));
                    }
                }
            }

            return list;
        }

        public void SetMailConversationId(long mailLogId, string conversationId)
        {
            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "UPDATE mail_log SET conversation_id = $c WHERE id = $id";
                cmd.Parameters.AddWithValue("$c", conversationId);
                cmd.Parameters.AddWithValue("$id", mailLogId);
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// 会話ID → （案件, 調達先）。送った依頼・催促のメールの記録から作る。
        /// 同じ会話IDが2つ以上の案件に付いているときは、結びつけられない（曖昧）ので入れない。
        /// </summary>
        public IReadOnlyDictionary<string, ConversationLink> ListConversationLinks()
        {
            var found = new Dictionary<string, ConversationLink>();
            var ambiguous = new HashSet<string>();
            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT conversation_id, anken_id, supplier_id FROM mail_log WHERE kind IN ('依頼', '催促') AND conversation_id IS NOT NULL AND conversation_id <> '' AND supplier_id IS NOT NULL";
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        var conv = r.GetString(0);
                        var link = new ConversationLink { AnkenId = r.GetInt64(1), SupplierId = r.GetInt64(2) };
                        ConversationLink? old;
                        if (found.TryGetValue(conv, out old) && (old.AnkenId != link.AnkenId || old.SupplierId != link.SupplierId))
                        {
                            ambiguous.Add(conv);
                        }

                        found[conv] = link;
                    }
                }
            }

            foreach (var a in ambiguous)
            {
                found.Remove(a);
            }

            return found;
        }

        private static MailKind KindFromText(string text)
        {
            switch (text)
            {
                case "催促":
                    return MailKind.Reminder;
                case "回答":
                    return MailKind.Answer;
                case "客先":
                    return MailKind.Client;
                default:
                    return MailKind.Request;
            }
        }

        /// <summary>メールを見分ける、件名と受信日時（分まで）の組。EntryIDは、メールをOutlookの別のフォルダへ動かすと変わるので、その備えに使う。</summary>
        public static string ImportedKey(string subject, DateTime receivedAt)
        {
            return "KEY|" + (subject ?? "").Trim() + "|" + receivedAt.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 取り込み済みのメール（回答・客先）の目印。EntryIDと、<see cref="ImportedKey"/>（件名＋受信日時）の両方を入れる。
        /// 受信メールの自動仕分けで、同じメールを二度出さないために使う。
        /// </summary>
        public ISet<string> ListImportedEntryIds()
        {
            var set = new HashSet<string>();
            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT entry_id, subject, sent_at FROM mail_log WHERE kind IN ('回答', '客先')";
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        if (!r.IsDBNull(0))
                        {
                            set.Add(r.GetString(0));
                        }

                        if (!r.IsDBNull(2))
                        {
                            set.Add(ImportedKey(r.GetString(1), DateTime.ParseExact(r.GetString(2), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)));
                        }
                    }
                }
            }

            return set;
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

        /// <summary>調達先から回答（見積書）を受け取ったので、「回答受領日」を入れる。</summary>
        public void MarkAnswerReceived(long ankenId, long supplierId, DateTime date)
        {
            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "UPDATE anken_supplier SET received_at = $d WHERE anken_id = $a AND supplier_id = $s";
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
