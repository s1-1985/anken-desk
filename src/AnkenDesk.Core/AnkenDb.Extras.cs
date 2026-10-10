using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AnkenDesk.Core
{
    /// <summary>外した調達先の控え（戻す操作の元）。</summary>
    public sealed class RemovedSupplier
    {
        public long Id { get; set; }
        public long AnkenId { get; set; }
        public long SupplierId { get; set; }
        public string SupplierName { get; set; } = "";
        public DateTime RemovedAt { get; set; }
        public bool HadAnswer { get; set; }
    }

    public sealed partial class AnkenDb
    {
        public void SetAnkenStatus(long ankenId, string status)
        {
            if (!AnkenStatus.All.Contains(status))
            {
                throw new ArgumentException("知らない状態です: " + status);
            }

            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "UPDATE anken SET status = $s WHERE id = $id";
                cmd.Parameters.AddWithValue("$s", status);
                cmd.Parameters.AddWithValue("$id", ankenId);
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>案件フォルダの場所（Work spaceからの相対パス）を直す。フォルダの名前変更・移動に追従するため。</summary>
        public void UpdateAnkenFolder(long ankenId, string relativePath)
        {
            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "UPDATE anken SET folder_path = $p WHERE id = $id";
                cmd.Parameters.AddWithValue("$p", relativePath);
                cmd.Parameters.AddWithValue("$id", ankenId);
                try
                {
                    cmd.ExecuteNonQuery();
                }
                catch (Microsoft.Data.Sqlite.SqliteException ex) when (IsUniqueViolation(ex))
                {
                    throw new InvalidOperationException("同じフォルダの案件が既に登録されています。");
                }
            }
        }

        /// <summary>同じ得意先・種別の、直近の案件（依頼日の新しい順）。<paramref name="excludeAnkenId"/> は除く。</summary>
        public AnkenRecord? LatestAnkenOfClient(long clientId, long excludeAnkenId)
        {
            return ListAnkens().FirstOrDefault(a => a.ClientId == clientId && a.Id != excludeAnkenId);
        }

        /// <summary>
        /// 同じ得意先・種別の直近の案件（最大 <paramref name="takeAnkens"/> 件）で、見積を依頼した調達先のID。
        /// 直近のものほど先。依頼先として加えただけ（依頼送付日なし）のものも含める。
        /// </summary>
        public IReadOnlyList<long> RecentSupplierIdsForClient(long clientId, long excludeAnkenId, int takeAnkens = 3)
        {
            var result = new List<long>();
            var all = ListAllAnkenSuppliers();
            foreach (var a in ListAnkens().Where(x => x.ClientId == clientId && x.Id != excludeAnkenId).Take(takeAnkens))
            {
                IReadOnlyList<AnkenSupplier>? list;
                if (all.TryGetValue(a.Id, out list))
                {
                    foreach (var s in list)
                    {
                        if (!result.Contains(s.SupplierId))
                        {
                            result.Add(s.SupplierId);
                        }
                    }
                }
            }

            return result;
        }

        public IReadOnlyList<RemovedSupplier> ListRemovedSuppliers(long ankenId)
        {
            var list = new List<RemovedSupplier>();
            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT r.id, r.supplier_id, s.name, r.removed_at, r.received_at, r.quotes "
                    + "FROM removed_supplier r JOIN supplier s ON s.id = r.supplier_id WHERE r.anken_id = $a ORDER BY r.id DESC";
                cmd.Parameters.AddWithValue("$a", ankenId);
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        list.Add(new RemovedSupplier
                        {
                            Id = r.GetInt64(0),
                            AnkenId = ankenId,
                            SupplierId = r.GetInt64(1),
                            SupplierName = r.GetString(2),
                            RemovedAt = DateTime.ParseExact(r.GetString(3), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                            HadAnswer = !r.IsDBNull(4) || r.GetString(5).Length > 0,
                        });
                    }
                }
            }

            return list;
        }

        /// <summary>外した調達先を、外した時点の回答と単価ごと戻す。すでに加え直してあるときは、何もせず false。</summary>
        public bool RestoreRemovedSupplier(long removedId)
        {
            using (var conn = Open())
            using (var tx = conn.BeginTransaction())
            {
                long ankenId, supplierId;
                string? sent, recv;
                string extra, relax, note, quotes;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "SELECT anken_id, supplier_id, sent_at, received_at, extra_cost, relaxation, note, quotes FROM removed_supplier WHERE id = $id";
                    cmd.Parameters.AddWithValue("$id", removedId);
                    using (var r = cmd.ExecuteReader())
                    {
                        if (!r.Read())
                        {
                            return false;
                        }

                        ankenId = r.GetInt64(0);
                        supplierId = r.GetInt64(1);
                        sent = r.IsDBNull(2) ? null : r.GetString(2);
                        recv = r.IsDBNull(3) ? null : r.GetString(3);
                        extra = r.GetString(4);
                        relax = r.GetString(5);
                        note = r.GetString(6);
                        quotes = r.GetString(7);
                    }
                }

                long exists;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "SELECT COUNT(*) FROM anken_supplier WHERE anken_id = $a AND supplier_id = $s";
                    cmd.Parameters.AddWithValue("$a", ankenId);
                    cmd.Parameters.AddWithValue("$s", supplierId);
                    exists = Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
                }

                if (exists > 0)
                {
                    return false;
                }

                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "INSERT INTO anken_supplier (anken_id, supplier_id, sent_at, received_at, extra_cost, relaxation, note) VALUES ($a, $s, $sent, $recv, $e, $x, $n)";
                    cmd.Parameters.AddWithValue("$a", ankenId);
                    cmd.Parameters.AddWithValue("$s", supplierId);
                    cmd.Parameters.AddWithValue("$sent", (object?)sent ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$recv", (object?)recv ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$e", extra);
                    cmd.Parameters.AddWithValue("$x", relax);
                    cmd.Parameters.AddWithValue("$n", note);
                    cmd.ExecuteNonQuery();
                }

                foreach (var part in quotes.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var f = part.Split('|');
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = "INSERT OR IGNORE INTO quote (anken_id, supplier_id, pattern_id, unit_price, lead_time_days) "
                            + "SELECT $a, $s, $p, $price, $lt WHERE EXISTS (SELECT 1 FROM quantity_pattern WHERE id = $p)";
                        cmd.Parameters.AddWithValue("$a", ankenId);
                        cmd.Parameters.AddWithValue("$s", supplierId);
                        cmd.Parameters.AddWithValue("$p", long.Parse(f[0], CultureInfo.InvariantCulture));
                        cmd.Parameters.AddWithValue("$price", f[1].Length == 0 ? (object)DBNull.Value : f[1]);
                        cmd.Parameters.AddWithValue("$lt", f[2].Length == 0 ? (object)DBNull.Value : (object)int.Parse(f[2], CultureInfo.InvariantCulture));
                        cmd.ExecuteNonQuery();
                    }
                }

                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "DELETE FROM removed_supplier WHERE id = $id";
                    cmd.Parameters.AddWithValue("$id", removedId);
                    cmd.ExecuteNonQuery();
                }

                tx.Commit();
                return true;
            }
        }
    }
}
