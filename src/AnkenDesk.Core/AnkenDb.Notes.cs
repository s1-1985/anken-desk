using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AnkenDesk.Core
{
    public sealed partial class AnkenDb
    {
        // ---- メモ ----

        public AnkenNote AddNote(long ankenId, long? supplierId, string text, string source = "")
        {
            var t = (text ?? "").Trim();
            if (t.Length == 0)
            {
                throw new ArgumentException("メモが空です。");
            }

            var now = DateTime.Now;
            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO anken_note (anken_id, supplier_id, created_at, text, source) VALUES ($a, $s, $c, $t, $src); SELECT last_insert_rowid()";
                cmd.Parameters.AddWithValue("$a", ankenId);
                cmd.Parameters.AddWithValue("$s", (object?)supplierId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$c", now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                cmd.Parameters.AddWithValue("$t", t);
                cmd.Parameters.AddWithValue("$src", source ?? "");
                var id = Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
                return new AnkenNote { Id = id, AnkenId = ankenId, SupplierId = supplierId, CreatedAt = now, Text = t, Source = source ?? "" };
            }
        }

        /// <summary>案件のメモ（新しい順）。</summary>
        public IReadOnlyList<AnkenNote> ListNotes(long ankenId)
        {
            var list = new List<AnkenNote>();
            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT id, supplier_id, created_at, text, source FROM anken_note WHERE anken_id = $a ORDER BY created_at DESC, id DESC";
                cmd.Parameters.AddWithValue("$a", ankenId);
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        list.Add(new AnkenNote
                        {
                            Id = r.GetInt64(0),
                            AnkenId = ankenId,
                            SupplierId = r.IsDBNull(1) ? (long?)null : r.GetInt64(1),
                            CreatedAt = DateTime.ParseExact(r.GetString(2), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                            Text = r.GetString(3),
                            Source = r.GetString(4),
                        });
                    }
                }
            }

            return list;
        }

        /// <summary>全案件のメモ（横断検索用）。</summary>
        public IReadOnlyList<AnkenNote> ListAllNotes()
        {
            var list = new List<AnkenNote>();
            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT id, anken_id, supplier_id, created_at, text, source FROM anken_note ORDER BY created_at DESC, id DESC";
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        list.Add(new AnkenNote
                        {
                            Id = r.GetInt64(0),
                            AnkenId = r.GetInt64(1),
                            SupplierId = r.IsDBNull(2) ? (long?)null : r.GetInt64(2),
                            CreatedAt = DateTime.ParseExact(r.GetString(3), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                            Text = r.GetString(4),
                            Source = r.GetString(5),
                        });
                    }
                }
            }

            return list;
        }

        public void UpdateNote(long noteId, string text)
        {
            var t = (text ?? "").Trim();
            if (t.Length == 0)
            {
                throw new ArgumentException("メモが空です。");
            }

            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "UPDATE anken_note SET text = $t WHERE id = $id";
                cmd.Parameters.AddWithValue("$t", t);
                cmd.Parameters.AddWithValue("$id", noteId);
                cmd.ExecuteNonQuery();
            }
        }

        public void DeleteNote(long noteId)
        {
            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM anken_note WHERE id = $id";
                cmd.Parameters.AddWithValue("$id", noteId);
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>メモの数（案件画面のボタンに出す）。</summary>
        public int CountNotes(long ankenId)
        {
            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM anken_note WHERE anken_id = $a";
                cmd.Parameters.AddWithValue("$a", ankenId);
                return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
        }

        // ---- 見積の内訳（調達先 × 数量パターン） ----

        /// <summary>
        /// 1社の1つの数量パターンについて、内訳を置き換える。空の項目名・金額なしの行は保存しない。
        /// その調達先が案件に加えられていないときは、InvalidOperationException。
        /// </summary>
        public void SaveBreakdown(long ankenId, long supplierId, long patternId, IEnumerable<BreakdownLine> lines)
        {
            using (var conn = Open())
            using (var tx = conn.BeginTransaction())
            {
                using (var del = conn.CreateCommand())
                {
                    del.Transaction = tx;
                    del.CommandText = "DELETE FROM quote_breakdown WHERE anken_id = $a AND supplier_id = $s AND pattern_id = $p";
                    del.Parameters.AddWithValue("$a", ankenId);
                    del.Parameters.AddWithValue("$s", supplierId);
                    del.Parameters.AddWithValue("$p", patternId);
                    del.ExecuteNonQuery();
                }

                var seq = 0;
                foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l.Item)))
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = "INSERT INTO quote_breakdown (anken_id, supplier_id, pattern_id, seq, item, amount) VALUES ($a, $s, $p, $seq, $i, $m)";
                        cmd.Parameters.AddWithValue("$a", ankenId);
                        cmd.Parameters.AddWithValue("$s", supplierId);
                        cmd.Parameters.AddWithValue("$p", patternId);
                        cmd.Parameters.AddWithValue("$seq", seq++);
                        cmd.Parameters.AddWithValue("$i", line.Item.Trim());
                        cmd.Parameters.AddWithValue("$m", line.Amount.ToString(CultureInfo.InvariantCulture));
                        try
                        {
                            cmd.ExecuteNonQuery();
                        }
                        catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 19)
                        {
                            throw new InvalidOperationException("この案件に、その調達先が加えられていません。");
                        }
                    }
                }

                tx.Commit();
            }
        }

        /// <summary>案件の内訳を、(調達先ID, 数量パターンID) ごとに返す。行は入力した順。</summary>
        public IReadOnlyDictionary<KeyValuePair<long, long>, IReadOnlyList<BreakdownLine>> ListBreakdowns(long ankenId)
        {
            var map = new Dictionary<KeyValuePair<long, long>, List<BreakdownLine>>();
            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT supplier_id, pattern_id, item, amount FROM quote_breakdown WHERE anken_id = $a ORDER BY supplier_id, pattern_id, seq";
                cmd.Parameters.AddWithValue("$a", ankenId);
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        var key = new KeyValuePair<long, long>(r.GetInt64(0), r.GetInt64(1));
                        List<BreakdownLine>? list;
                        if (!map.TryGetValue(key, out list))
                        {
                            list = new List<BreakdownLine>();
                            map[key] = list;
                        }

                        list.Add(new BreakdownLine { Item = r.GetString(2), Amount = decimal.Parse(r.GetString(3), CultureInfo.InvariantCulture) });
                    }
                }
            }

            return map.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<BreakdownLine>)kv.Value);
        }

        // ---- 客先への提出単価 ----

        /// <summary>案件の提出単価を、渡した内容に置き換える（単価も調達先も空の行は保存しない）。</summary>
        public void SaveClientPrices(long ankenId, IEnumerable<ClientPrice> prices)
        {
            using (var conn = Open())
            using (var tx = conn.BeginTransaction())
            {
                using (var del = conn.CreateCommand())
                {
                    del.Transaction = tx;
                    del.CommandText = "DELETE FROM client_price WHERE anken_id = $a";
                    del.Parameters.AddWithValue("$a", ankenId);
                    del.ExecuteNonQuery();
                }

                foreach (var p in prices.Where(x => x.SellingPrice.HasValue || x.AdoptedSupplierId.HasValue))
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = "INSERT INTO client_price (anken_id, pattern_id, selling_price, adopted_supplier_id) VALUES ($a, $p, $price, $s)";
                        cmd.Parameters.AddWithValue("$a", ankenId);
                        cmd.Parameters.AddWithValue("$p", p.PatternId);
                        cmd.Parameters.AddWithValue("$price", p.SellingPrice.HasValue ? (object)p.SellingPrice.Value.ToString(CultureInfo.InvariantCulture) : DBNull.Value);
                        cmd.Parameters.AddWithValue("$s", (object?)p.AdoptedSupplierId ?? DBNull.Value);
                        cmd.ExecuteNonQuery();
                    }
                }

                tx.Commit();
            }
        }

        public IReadOnlyList<ClientPrice> ListClientPrices(long ankenId)
        {
            var list = new List<ClientPrice>();
            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT pattern_id, selling_price, adopted_supplier_id FROM client_price WHERE anken_id = $a";
                cmd.Parameters.AddWithValue("$a", ankenId);
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        list.Add(new ClientPrice
                        {
                            PatternId = r.GetInt64(0),
                            SellingPrice = r.IsDBNull(1) ? (decimal?)null : decimal.Parse(r.GetString(1), CultureInfo.InvariantCulture),
                            AdoptedSupplierId = r.IsDBNull(2) ? (long?)null : r.GetInt64(2),
                        });
                    }
                }
            }

            return list;
        }

        // ---- 結果（受注・失注など） ----

        /// <summary>結果を入力する。状態も同時に変える。採用した調達先は、受注のときだけ意味がある（それ以外はnullにする）。</summary>
        public void SetAnkenResult(long ankenId, string status, DateTime? resultDate, string note, long? adoptedSupplierId)
        {
            if (!AnkenStatus.All.Contains(status))
            {
                throw new ArgumentException("知らない状態です: " + status);
            }

            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "UPDATE anken SET status = $s, result_date = $d, result_note = $n, adopted_supplier_id = $sup WHERE id = $id";
                cmd.Parameters.AddWithValue("$s", status);
                cmd.Parameters.AddWithValue("$d", resultDate.HasValue ? (object)resultDate.Value.ToString(DateFormat, CultureInfo.InvariantCulture) : DBNull.Value);
                cmd.Parameters.AddWithValue("$n", (note ?? "").Trim());
                cmd.Parameters.AddWithValue("$sup", status == AnkenStatus.Won && adoptedSupplierId.HasValue ? (object)adoptedSupplierId.Value : DBNull.Value);
                cmd.Parameters.AddWithValue("$id", ankenId);
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>調達先の得意分野を保存する（正式名・略称などの更新とは別）。</summary>
        public void SetSupplierSpecialty(long supplierId, string specialty)
        {
            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "UPDATE supplier SET specialty = $s WHERE id = $id";
                cmd.Parameters.AddWithValue("$s", (specialty ?? "").Trim());
                cmd.Parameters.AddWithValue("$id", supplierId);
                cmd.ExecuteNonQuery();
            }
        }
    }
}
