using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace AnkenDesk.Core
{
    // 見積の出し直し（版）。HANDOFF.md §3.2: 最新版が正。登録時に旧版を残す／残さないを選ぶ（初期値は残す）。
    public sealed partial class AnkenDb
    {
        /// <summary>
        /// 出し直しの回答に差し替える。keepHistory が true なら、差し替える前の回答を旧版（履歴）として残す。
        /// false なら旧版のデータは残らない。archivedFiles は、旧版フォルダへ移した見積書のファイル名（改行区切り、履歴に記録するだけ）。
        /// 旧版として残す回答が空（回答日も単価も無い）ときは、履歴を作らない。
        /// </summary>
        public void SaveNewVersion(AnkenSupplier newAnswer, IEnumerable<Quote> newQuotes, bool keepHistory, string archivedFiles)
        {
            using (var conn = Open())
            using (var tx = conn.BeginTransaction())
            {
                if (keepHistory)
                {
                    Archive(conn, tx, newAnswer.AnkenId, newAnswer.SupplierId, archivedFiles ?? "");
                }

                WriteAnswer(conn, tx, newAnswer, newQuotes);
                tx.Commit();
            }
        }

        /// <summary>旧版の一覧。新しい版が先。</summary>
        public IReadOnlyList<AnswerVersion> ListAnswerHistory(long ankenId, long supplierId)
        {
            var list = new List<AnswerVersion>();
            using (var conn = Open())
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT id, version, archived_at, sent_at, received_at, extra_cost, relaxation, note, files "
                        + "FROM answer_history WHERE anken_id = $a AND supplier_id = $s ORDER BY version DESC";
                    cmd.Parameters.AddWithValue("$a", ankenId);
                    cmd.Parameters.AddWithValue("$s", supplierId);
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            list.Add(new AnswerVersion
                            {
                                Id = r.GetInt64(0),
                                AnkenId = ankenId,
                                SupplierId = supplierId,
                                Version = r.GetInt32(1),
                                ArchivedAt = DateTime.ParseExact(r.GetString(2), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                                SentAt = r.IsDBNull(3) ? (DateTime?)null : ParseDate(r.GetString(3)),
                                ReceivedAt = r.IsDBNull(4) ? (DateTime?)null : ParseDate(r.GetString(4)),
                                ExtraCost = r.GetString(5),
                                Relaxation = r.GetString(6),
                                Note = r.GetString(7),
                                Files = r.GetString(8),
                            });
                        }
                    }
                }

                foreach (var v in list)
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT pattern_id, unit_price, lead_time_days FROM quote_history WHERE history_id = $h";
                        cmd.Parameters.AddWithValue("$h", v.Id);
                        using (var r = cmd.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                v.Quotes.Add(new Quote
                                {
                                    SupplierId = supplierId,
                                    PatternId = r.GetInt64(0),
                                    UnitPrice = r.IsDBNull(1) ? (decimal?)null : decimal.Parse(r.GetString(1), CultureInfo.InvariantCulture),
                                    LeadTimeDays = r.IsDBNull(2) ? (int?)null : r.GetInt32(2),
                                });
                            }
                        }
                    }
                }
            }

            return list;
        }

        private static void Archive(SqliteConnection conn, SqliteTransaction tx, long ankenId, long supplierId, string files)
        {
            string? sent, received, extra, relax, note;
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT sent_at, received_at, extra_cost, relaxation, note FROM anken_supplier WHERE anken_id = $a AND supplier_id = $s";
                cmd.Parameters.AddWithValue("$a", ankenId);
                cmd.Parameters.AddWithValue("$s", supplierId);
                using (var r = cmd.ExecuteReader())
                {
                    if (!r.Read())
                    {
                        throw new InvalidOperationException("この案件に、その調達先が加えられていません。");
                    }

                    sent = r.IsDBNull(0) ? null : r.GetString(0);
                    received = r.IsDBNull(1) ? null : r.GetString(1);
                    extra = r.GetString(2);
                    relax = r.GetString(3);
                    note = r.GetString(4);
                }
            }

            long quoteCount;
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT COUNT(*) FROM quote WHERE anken_id = $a AND supplier_id = $s";
                cmd.Parameters.AddWithValue("$a", ankenId);
                cmd.Parameters.AddWithValue("$s", supplierId);
                quoteCount = Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
            }

            // 残す中身が何も無ければ、空の版を作らない。
            if (received == null && quoteCount == 0 && extra.Length == 0 && relax.Length == 0 && note.Length == 0 && files.Length == 0)
            {
                return;
            }

            long historyId;
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT INTO answer_history (anken_id, supplier_id, version, archived_at, sent_at, received_at, extra_cost, relaxation, note, files) "
                    + "VALUES ($a, $s, (SELECT COALESCE(MAX(version), 0) + 1 FROM answer_history WHERE anken_id = $a AND supplier_id = $s), "
                    + "$at, $sent, $recv, $extra, $relax, $note, $files); SELECT last_insert_rowid()";
                cmd.Parameters.AddWithValue("$a", ankenId);
                cmd.Parameters.AddWithValue("$s", supplierId);
                cmd.Parameters.AddWithValue("$at", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                cmd.Parameters.AddWithValue("$sent", (object?)sent ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$recv", (object?)received ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$extra", extra);
                cmd.Parameters.AddWithValue("$relax", relax);
                cmd.Parameters.AddWithValue("$note", note);
                cmd.Parameters.AddWithValue("$files", files);
                historyId = Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT INTO quote_history (history_id, pattern_id, unit_price, lead_time_days) "
                    + "SELECT $h, pattern_id, unit_price, lead_time_days FROM quote WHERE anken_id = $a AND supplier_id = $s";
                cmd.Parameters.AddWithValue("$h", historyId);
                cmd.Parameters.AddWithValue("$a", ankenId);
                cmd.Parameters.AddWithValue("$s", supplierId);
                cmd.ExecuteNonQuery();
            }
        }
    }
}
