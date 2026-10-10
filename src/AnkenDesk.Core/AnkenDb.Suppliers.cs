using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace AnkenDesk.Core
{
    // 調達先マスターと、案件ごとの調達先・回答。
    public sealed partial class AnkenDb
    {
        // ---- 調達先マスター ----

        public IReadOnlyList<Supplier> ListSuppliers()
        {
            var list = new List<Supplier>();
            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT id, name, short_name, email, address, specialty FROM supplier ORDER BY name";
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        list.Add(new Supplier
                        {
                            Id = r.GetInt64(0),
                            Name = r.GetString(1),
                            ShortName = r.GetString(2),
                            Email = r.GetString(3),
                            Address = r.GetString(4),
                            Specialty = r.GetString(5),
                        });
                    }
                }
            }

            return list;
        }

        /// <summary>略称が空なら、正式名を略称にする。</summary>
        public Supplier AddSupplier(string name, string shortName, string email, string address)
        {
            var s = CheckSupplier(name, shortName, email, address);
            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO supplier (name, short_name, email, address) VALUES ($n, $s, $e, $a); SELECT last_insert_rowid()";
                cmd.Parameters.AddWithValue("$n", s.Name);
                cmd.Parameters.AddWithValue("$s", s.ShortName);
                cmd.Parameters.AddWithValue("$e", s.Email);
                cmd.Parameters.AddWithValue("$a", s.Address);
                try
                {
                    s.Id = Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
                }
                catch (SqliteException ex) when (IsUniqueViolation(ex))
                {
                    throw new InvalidOperationException("同じ名前の調達先が既にあります。");
                }
            }

            return s;
        }

        public void UpdateSupplier(Supplier supplier)
        {
            var s = CheckSupplier(supplier.Name, supplier.ShortName, supplier.Email, supplier.Address);
            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "UPDATE supplier SET name = $n, short_name = $s, email = $e, address = $a WHERE id = $id";
                cmd.Parameters.AddWithValue("$n", s.Name);
                cmd.Parameters.AddWithValue("$s", s.ShortName);
                cmd.Parameters.AddWithValue("$e", s.Email);
                cmd.Parameters.AddWithValue("$a", s.Address);
                cmd.Parameters.AddWithValue("$id", supplier.Id);
                try
                {
                    cmd.ExecuteNonQuery();
                }
                catch (SqliteException ex) when (IsUniqueViolation(ex))
                {
                    throw new InvalidOperationException("同じ名前の調達先が既にあります。");
                }
            }
        }

        /// <summary>案件で使われていない調達先だけ削除できる。</summary>
        public void DeleteSupplier(long id)
        {
            using (var conn = Open())
            {
                using (var count = conn.CreateCommand())
                {
                    count.CommandText = "SELECT COUNT(*) FROM anken_supplier WHERE supplier_id = $id";
                    count.Parameters.AddWithValue("$id", id);
                    if (Convert.ToInt64(count.ExecuteScalar(), CultureInfo.InvariantCulture) > 0)
                    {
                        throw new InvalidOperationException("案件で使われているため、削除できません。");
                    }
                }

                using (var del = conn.CreateCommand())
                {
                    del.CommandText = "DELETE FROM supplier WHERE id = $id";
                    del.Parameters.AddWithValue("$id", id);
                    del.ExecuteNonQuery();
                }
            }
        }

        // ---- 案件ごとの調達先と回答 ----

        public AnkenRecord? GetAnken(long id)
        {
            foreach (var a in ListAnkens())
            {
                if (a.Id == id)
                {
                    return a;
                }
            }

            return null;
        }

        public IReadOnlyList<AnkenSupplier> ListAnkenSuppliers(long ankenId)
        {
            var list = new List<AnkenSupplier>();
            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT a.supplier_id, s.name, s.short_name, a.sent_at, a.received_at, a.extra_cost, a.relaxation, a.note "
                    + "FROM anken_supplier a JOIN supplier s ON s.id = a.supplier_id WHERE a.anken_id = $a ORDER BY s.name";
                cmd.Parameters.AddWithValue("$a", ankenId);
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        list.Add(new AnkenSupplier
                        {
                            AnkenId = ankenId,
                            SupplierId = r.GetInt64(0),
                            SupplierName = r.GetString(1),
                            ShortName = r.GetString(2),
                            SentAt = r.IsDBNull(3) ? (DateTime?)null : ParseDate(r.GetString(3)),
                            ReceivedAt = r.IsDBNull(4) ? (DateTime?)null : ParseDate(r.GetString(4)),
                            ExtraCost = r.GetString(5),
                            Relaxation = r.GetString(6),
                            Note = r.GetString(7),
                        });
                    }
                }
            }

            return list;
        }

        /// <summary>全案件の調達先と回答状況を、案件のIDごとにまとめて返す（ホームの一覧用）。</summary>
        public IReadOnlyDictionary<long, IReadOnlyList<AnkenSupplier>> ListAllAnkenSuppliers()
        {
            var dict = new Dictionary<long, List<AnkenSupplier>>();
            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT a.anken_id, a.supplier_id, s.name, s.short_name, a.sent_at, a.received_at, a.extra_cost, a.relaxation, a.note "
                    + "FROM anken_supplier a JOIN supplier s ON s.id = a.supplier_id ORDER BY a.anken_id, s.name";
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        var ankenId = r.GetInt64(0);
                        List<AnkenSupplier>? list;
                        if (!dict.TryGetValue(ankenId, out list))
                        {
                            list = new List<AnkenSupplier>();
                            dict[ankenId] = list;
                        }

                        list.Add(new AnkenSupplier
                        {
                            AnkenId = ankenId,
                            SupplierId = r.GetInt64(1),
                            SupplierName = r.GetString(2),
                            ShortName = r.GetString(3),
                            SentAt = r.IsDBNull(4) ? (DateTime?)null : ParseDate(r.GetString(4)),
                            ReceivedAt = r.IsDBNull(5) ? (DateTime?)null : ParseDate(r.GetString(5)),
                            ExtraCost = r.GetString(6),
                            Relaxation = r.GetString(7),
                            Note = r.GetString(8),
                        });
                    }
                }
            }

            var result = new Dictionary<long, IReadOnlyList<AnkenSupplier>>();
            foreach (var kv in dict)
            {
                result[kv.Key] = kv.Value;
            }

            return result;
        }

        /// <summary>案件に調達先を加える。既に加えてあれば何もしない。</summary>
        public void AddSupplierToAnken(long ankenId, long supplierId)
        {
            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "INSERT OR IGNORE INTO anken_supplier (anken_id, supplier_id) VALUES ($a, $s)";
                cmd.Parameters.AddWithValue("$a", ankenId);
                cmd.Parameters.AddWithValue("$s", supplierId);
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// 案件から調達先を外す。その調達先の単価・日付などの入力も消えるが、その時点の内容を
        /// 「外した調達先」の控えに残す（<see cref="RestoreRemovedSupplier"/> で戻せる）。回答の履歴（版）は戻らない。
        /// </summary>
        public void RemoveSupplierFromAnken(long ankenId, long supplierId)
        {
            using (var conn = Open())
            using (var tx = conn.BeginTransaction())
            {
                var quotes = new List<string>();
                using (var q = conn.CreateCommand())
                {
                    q.Transaction = tx;
                    q.CommandText = "SELECT pattern_id, unit_price, lead_time_days FROM quote WHERE anken_id = $a AND supplier_id = $s";
                    q.Parameters.AddWithValue("$a", ankenId);
                    q.Parameters.AddWithValue("$s", supplierId);
                    using (var r = q.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            quotes.Add(r.GetInt64(0) + "|" + (r.IsDBNull(1) ? "" : r.GetString(1)) + "|" + (r.IsDBNull(2) ? "" : r.GetInt32(2).ToString(CultureInfo.InvariantCulture)));
                        }
                    }
                }

                var breakdowns = new List<string>();
                using (var b = conn.CreateCommand())
                {
                    b.Transaction = tx;
                    b.CommandText = "SELECT pattern_id, item, amount FROM quote_breakdown WHERE anken_id = $a AND supplier_id = $s ORDER BY pattern_id, seq";
                    b.Parameters.AddWithValue("$a", ankenId);
                    b.Parameters.AddWithValue("$s", supplierId);
                    using (var r = b.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            breakdowns.Add(r.GetInt64(0) + "|" + SnapshotText(r.GetString(1)) + "|" + r.GetString(2));
                        }
                    }
                }

                using (var snap = conn.CreateCommand())
                {
                    snap.Transaction = tx;
                    snap.CommandText = "INSERT INTO removed_supplier (anken_id, supplier_id, sent_at, received_at, extra_cost, relaxation, note, quotes, breakdowns, removed_at) "
                        + "SELECT anken_id, supplier_id, sent_at, received_at, extra_cost, relaxation, note, $quotes, $breakdowns, $now "
                        + "FROM anken_supplier WHERE anken_id = $a AND supplier_id = $s";
                    snap.Parameters.AddWithValue("$quotes", string.Join(";", quotes));
                    snap.Parameters.AddWithValue("$breakdowns", string.Join(";", breakdowns));
                    snap.Parameters.AddWithValue("$now", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                    snap.Parameters.AddWithValue("$a", ankenId);
                    snap.Parameters.AddWithValue("$s", supplierId);
                    snap.ExecuteNonQuery();
                }

                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "DELETE FROM anken_supplier WHERE anken_id = $a AND supplier_id = $s";
                    cmd.Parameters.AddWithValue("$a", ankenId);
                    cmd.Parameters.AddWithValue("$s", supplierId);
                    cmd.ExecuteNonQuery();
                }

                tx.Commit();
            }
        }

        // 控えの文字列の区切り（; |）が項目名に入っていても壊れないよう、全角に置き換える。
        private static string SnapshotText(string text)
        {
            return (text ?? "").Replace(';', '；').Replace('|', '｜');
        }

        public IReadOnlyList<Quote> ListQuotes(long ankenId)
        {
            var list = new List<Quote>();
            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT supplier_id, pattern_id, unit_price, lead_time_days FROM quote WHERE anken_id = $a";
                cmd.Parameters.AddWithValue("$a", ankenId);
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        list.Add(new Quote
                        {
                            SupplierId = r.GetInt64(0),
                            PatternId = r.GetInt64(1),
                            UnitPrice = r.IsDBNull(2) ? (decimal?)null : decimal.Parse(r.GetString(2), CultureInfo.InvariantCulture),
                            LeadTimeDays = r.IsDBNull(3) ? (int?)null : r.GetInt32(3),
                        });
                    }
                }
            }

            return list;
        }

        /// <summary>
        /// 1社分の回答を保存する。調達先ごとの項目（日付・別費用・緩和条件・備考）と、
        /// 数量パターンごとの単価・リードタイムを、同じトランザクションで置き換える。
        /// 単価もリードタイムも空の行は保存しない。
        /// </summary>
        public void SaveSupplierAnswer(AnkenSupplier answer, IEnumerable<Quote> quotes)
        {
            using (var conn = Open())
            using (var tx = conn.BeginTransaction())
            {
                WriteAnswer(conn, tx, answer, quotes);
                tx.Commit();
            }
        }

        // 現在の回答（調達先ごとの項目と数量パターンごとの単価）を、渡された内容に置き換える。
        private static void WriteAnswer(SqliteConnection conn, SqliteTransaction tx, AnkenSupplier answer, IEnumerable<Quote> quotes)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE anken_supplier SET sent_at = $sent, received_at = $recv, extra_cost = $extra, relaxation = $relax, note = $note "
                    + "WHERE anken_id = $a AND supplier_id = $s";
                cmd.Parameters.AddWithValue("$sent", DateOrNull(answer.SentAt));
                cmd.Parameters.AddWithValue("$recv", DateOrNull(answer.ReceivedAt));
                cmd.Parameters.AddWithValue("$extra", answer.ExtraCost.Trim());
                cmd.Parameters.AddWithValue("$relax", answer.Relaxation.Trim());
                cmd.Parameters.AddWithValue("$note", answer.Note.Trim());
                cmd.Parameters.AddWithValue("$a", answer.AnkenId);
                cmd.Parameters.AddWithValue("$s", answer.SupplierId);
                if (cmd.ExecuteNonQuery() == 0)
                {
                    throw new InvalidOperationException("この案件に、その調達先が加えられていません。");
                }
            }

            using (var del = conn.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = "DELETE FROM quote WHERE anken_id = $a AND supplier_id = $s";
                del.Parameters.AddWithValue("$a", answer.AnkenId);
                del.Parameters.AddWithValue("$s", answer.SupplierId);
                del.ExecuteNonQuery();
            }

            foreach (var q in quotes)
            {
                if (!q.UnitPrice.HasValue && !q.LeadTimeDays.HasValue)
                {
                    continue;
                }

                using (var ins = conn.CreateCommand())
                {
                    ins.Transaction = tx;
                    ins.CommandText = "INSERT INTO quote (anken_id, supplier_id, pattern_id, unit_price, lead_time_days) VALUES ($a, $s, $p, $price, $lt)";
                    ins.Parameters.AddWithValue("$a", answer.AnkenId);
                    ins.Parameters.AddWithValue("$s", answer.SupplierId);
                    ins.Parameters.AddWithValue("$p", q.PatternId);
                    ins.Parameters.AddWithValue("$price", q.UnitPrice.HasValue ? (object)q.UnitPrice.Value.ToString(CultureInfo.InvariantCulture) : DBNull.Value);
                    ins.Parameters.AddWithValue("$lt", q.LeadTimeDays.HasValue ? (object)q.LeadTimeDays.Value : DBNull.Value);
                    ins.ExecuteNonQuery();
                }
            }
        }

        private static object DateOrNull(DateTime? d)
        {
            return d.HasValue ? (object)d.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : DBNull.Value;
        }

        private static Supplier CheckSupplier(string name, string shortName, string email, string address)
        {
            var n = (name ?? "").Trim();
            if (n.Length == 0)
            {
                throw new ArgumentException("調達先の名前が空です。");
            }

            var s = (shortName ?? "").Trim();
            if (s.Length == 0)
            {
                s = n;
            }

            if (!Sanitizer.IsValidName(s))
            {
                throw new ArgumentException("略称は、ファイル名に使うため、使えない文字（\\ / : * ? \" < > |）は使えません。");
            }

            return new Supplier { Name = n, ShortName = s, Email = (email ?? "").Trim(), Address = (address ?? "").Trim() };
        }
    }
}
