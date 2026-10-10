using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;

namespace AnkenDesk.Core
{
    /// <summary>SQLiteの業務データ。接続は操作ごとに開閉する（接続プールは破棄時に空にする）。</summary>
    public sealed partial class AnkenDb : IDisposable
    {
        private const int SchemaVersion = 7;
        private const string DateFormat = "yyyy-MM-dd";

        private readonly string _connectionString;

        public string Path { get; }

        public AnkenDb(string path)
        {
            Path = path;
            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            _connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
            Migrate();
        }

        public void Dispose()
        {
            // 接続プールがファイルを掴んだままにしないようにする。
            SqliteConnection.ClearAllPools();
        }

        // ---- 設定 ----

        public string? GetSetting(string key)
        {
            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT value FROM settings WHERE key = $key";
                cmd.Parameters.AddWithValue("$key", key);
                var v = cmd.ExecuteScalar();
                return v == null ? null : Convert.ToString(v, CultureInfo.InvariantCulture);
            }
        }

        public void SetSetting(string key, string value)
        {
            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO settings (key, value) VALUES ($key, $value) "
                    + "ON CONFLICT(key) DO UPDATE SET value = excluded.value";
                cmd.Parameters.AddWithValue("$key", key);
                cmd.Parameters.AddWithValue("$value", value);
                cmd.ExecuteNonQuery();
            }
        }

        // ---- 得意先・種別 ----

        public IReadOnlyList<Client> ListClients()
        {
            var list = new List<Client>();
            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT id, name FROM client ORDER BY name";
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        list.Add(new Client { Id = r.GetInt64(0), Name = r.GetString(1) });
                    }
                }
            }

            return list;
        }

        public Client? GetClient(long id)
        {
            foreach (var c in ListClients())
            {
                if (c.Id == id)
                {
                    return c;
                }
            }

            return null;
        }

        public Client AddClient(string name)
        {
            var n = CheckClientName(name);
            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO client (name) VALUES ($name); SELECT last_insert_rowid()";
                cmd.Parameters.AddWithValue("$name", n);
                try
                {
                    var id = Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
                    return new Client { Id = id, Name = n };
                }
                catch (SqliteException ex) when (IsUniqueViolation(ex))
                {
                    throw new InvalidOperationException("同じ名前の得意先・種別が既にあります。");
                }
            }
        }

        /// <summary>名前を変える。登録済みの案件のフォルダ（フォルダの相対パスは案件ごとに保存済み）は動かさない。</summary>
        public void RenameClient(long id, string newName)
        {
            var n = CheckClientName(newName);
            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "UPDATE client SET name = $name WHERE id = $id";
                cmd.Parameters.AddWithValue("$name", n);
                cmd.Parameters.AddWithValue("$id", id);
                try
                {
                    cmd.ExecuteNonQuery();
                }
                catch (SqliteException ex) when (IsUniqueViolation(ex))
                {
                    throw new InvalidOperationException("同じ名前の得意先・種別が既にあります。");
                }
            }
        }

        /// <summary>案件が1件も無い得意先・種別だけ削除できる。</summary>
        public void DeleteClient(long id)
        {
            using (var conn = Open())
            {
                using (var count = conn.CreateCommand())
                {
                    count.CommandText = "SELECT COUNT(*) FROM anken WHERE client_id = $id";
                    count.Parameters.AddWithValue("$id", id);
                    if (Convert.ToInt64(count.ExecuteScalar(), CultureInfo.InvariantCulture) > 0)
                    {
                        throw new InvalidOperationException("案件が登録されているため、削除できません。");
                    }
                }

                using (var del = conn.CreateCommand())
                {
                    del.CommandText = "DELETE FROM client WHERE id = $id";
                    del.Parameters.AddWithValue("$id", id);
                    del.ExecuteNonQuery();
                }
            }
        }

        // ---- 案件 ----

        /// <summary>案件を保存する（数量パターン・見積依頼書の項目も同じトランザクション）。</summary>
        public AnkenRecord InsertAnken(AnkenInput input, string folderPath)
        {
            var client = GetClient(input.ClientId);
            if (client == null)
            {
                throw new InvalidOperationException("得意先・種別が見つかりません。");
            }

            using (var conn = Open())
            using (var tx = conn.BeginTransaction())
            {
                long id;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "INSERT INTO anken (client_id, request_date, part_number, part_name, note, reply_due_date, folder_path, created_at) "
                        + "VALUES ($client, $req, $part, $name, $note, $due, $folder, $created); SELECT last_insert_rowid()";
                    cmd.Parameters.AddWithValue("$client", input.ClientId);
                    cmd.Parameters.AddWithValue("$req", input.RequestDate.ToString(DateFormat, CultureInfo.InvariantCulture));
                    cmd.Parameters.AddWithValue("$part", input.PartNumber.Trim());
                    cmd.Parameters.AddWithValue("$name", input.PartName.Trim());
                    cmd.Parameters.AddWithValue("$note", input.Note.Trim());
                    cmd.Parameters.AddWithValue("$due", input.ReplyDueDate.ToString(DateFormat, CultureInfo.InvariantCulture));
                    cmd.Parameters.AddWithValue("$folder", folderPath);
                    cmd.Parameters.AddWithValue("$created", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                    try
                    {
                        id = Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
                    }
                    catch (SqliteException ex) when (IsUniqueViolation(ex))
                    {
                        throw new InvalidOperationException("同じフォルダの案件が既に登録されています。");
                    }
                }

                var seq = 0;
                foreach (var q in input.Quantities)
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = "INSERT INTO quantity_pattern (anken_id, seq, kind, quantity, unit) VALUES ($a, $seq, $kind, $qty, $unit)";
                        cmd.Parameters.AddWithValue("$a", id);
                        cmd.Parameters.AddWithValue("$seq", seq++);
                        cmd.Parameters.AddWithValue("$kind", q.Kind);
                        cmd.Parameters.AddWithValue("$qty", q.Quantity.ToString(CultureInfo.InvariantCulture));
                        cmd.Parameters.AddWithValue("$unit", q.Unit);
                        cmd.ExecuteNonQuery();
                    }
                }

                foreach (var kv in input.Items)
                {
                    if (string.IsNullOrWhiteSpace(kv.Value))
                    {
                        continue;
                    }

                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = "INSERT INTO anken_item (anken_id, name, value) VALUES ($a, $name, $value)";
                        cmd.Parameters.AddWithValue("$a", id);
                        cmd.Parameters.AddWithValue("$name", kv.Key);
                        cmd.Parameters.AddWithValue("$value", kv.Value.Trim());
                        cmd.ExecuteNonQuery();
                    }
                }

                tx.Commit();

                return new AnkenRecord
                {
                    Id = id,
                    ClientId = client.Id,
                    ClientName = client.Name,
                    RequestDate = input.RequestDate.Date,
                    PartNumber = input.PartNumber.Trim(),
                    PartName = input.PartName.Trim(),
                    Note = input.Note.Trim(),
                    ReplyDueDate = input.ReplyDueDate.Date,
                    FolderPath = folderPath,
                };
            }
        }

        /// <summary>依頼日の新しい順。</summary>
        public IReadOnlyList<AnkenRecord> ListAnkens()
        {
            var list = new List<AnkenRecord>();
            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT a.id, a.client_id, c.name, a.request_date, a.part_number, a.part_name, a.note, a.reply_due_date, a.folder_path, a.status, a.result_date, a.result_note, a.adopted_supplier_id, a.calendar_entry_id "
                    + "FROM anken a JOIN client c ON c.id = a.client_id ORDER BY a.request_date DESC, a.id DESC";
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        list.Add(new AnkenRecord
                        {
                            Id = r.GetInt64(0),
                            ClientId = r.GetInt64(1),
                            ClientName = r.GetString(2),
                            RequestDate = ParseDate(r.GetString(3)),
                            PartNumber = r.GetString(4),
                            PartName = r.GetString(5),
                            Note = r.GetString(6),
                            ReplyDueDate = ParseDate(r.GetString(7)),
                            FolderPath = r.GetString(8),
                            Status = r.GetString(9),
                            ResultDate = r.IsDBNull(10) ? (DateTime?)null : ParseDate(r.GetString(10)),
                            ResultNote = r.GetString(11),
                            AdoptedSupplierId = r.IsDBNull(12) ? (long?)null : r.GetInt64(12),
                            CalendarEntryId = r.IsDBNull(13) ? null : r.GetString(13),
                        });
                    }
                }
            }

            return list;
        }

        public IReadOnlyList<QuantityPattern> ListQuantities(long ankenId)
        {
            var list = new List<QuantityPattern>();
            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT kind, quantity, unit, id FROM quantity_pattern WHERE anken_id = $a ORDER BY seq";
                cmd.Parameters.AddWithValue("$a", ankenId);
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        list.Add(new QuantityPattern
                        {
                            Kind = r.GetString(0),
                            Quantity = decimal.Parse(r.GetString(1), CultureInfo.InvariantCulture),
                            Unit = r.GetString(2),
                            Id = r.GetInt64(3),
                        });
                    }
                }
            }

            return list;
        }

        public IReadOnlyDictionary<string, string> GetItems(long ankenId)
        {
            var dict = new Dictionary<string, string>();
            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT name, value FROM anken_item WHERE anken_id = $a";
                cmd.Parameters.AddWithValue("$a", ankenId);
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        dict[r.GetString(0)] = r.GetString(1);
                    }
                }
            }

            return dict;
        }

        // ---- 内部 ----

        private SqliteConnection Open()
        {
            var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA foreign_keys = ON";
                cmd.ExecuteNonQuery();
            }

            return conn;
        }

        private void Migrate()
        {
            using (var conn = Open())
            {
                long version;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA user_version";
                    version = Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
                }

                if (version > SchemaVersion)
                {
                    throw new InvalidOperationException("DBが新しい版のアプリで作られています。アプリを新しくしてください。");
                }

                if (version < 1)
                {
                    using (var tx = conn.BeginTransaction())
                    {
                        Exec(conn, tx, "CREATE TABLE settings (key TEXT PRIMARY KEY, value TEXT NOT NULL)");
                        Exec(conn, tx, "CREATE TABLE client (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL UNIQUE)");
                        Exec(conn, tx, "CREATE TABLE anken ("
                            + "id INTEGER PRIMARY KEY AUTOINCREMENT, "
                            + "client_id INTEGER NOT NULL REFERENCES client(id), "
                            + "request_date TEXT NOT NULL, part_number TEXT NOT NULL, part_name TEXT NOT NULL, note TEXT NOT NULL, "
                            + "reply_due_date TEXT NOT NULL, folder_path TEXT NOT NULL UNIQUE, created_at TEXT NOT NULL)");
                        Exec(conn, tx, "CREATE TABLE quantity_pattern ("
                            + "id INTEGER PRIMARY KEY AUTOINCREMENT, "
                            + "anken_id INTEGER NOT NULL REFERENCES anken(id), seq INTEGER NOT NULL, "
                            + "kind TEXT NOT NULL, quantity TEXT NOT NULL, unit TEXT NOT NULL)");
                        Exec(conn, tx, "CREATE TABLE anken_item ("
                            + "anken_id INTEGER NOT NULL REFERENCES anken(id), name TEXT NOT NULL, value TEXT NOT NULL, "
                            + "PRIMARY KEY (anken_id, name))");
                        Exec(conn, tx, "PRAGMA user_version = 1");
                        tx.Commit();
                    }
                }

                if (version < 2)
                {
                    using (var tx = conn.BeginTransaction())
                    {
                        Exec(conn, tx, "CREATE TABLE supplier ("
                            + "id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL UNIQUE, short_name TEXT NOT NULL, "
                            + "email TEXT NOT NULL, address TEXT NOT NULL)");
                        Exec(conn, tx, "CREATE TABLE anken_supplier ("
                            + "anken_id INTEGER NOT NULL REFERENCES anken(id), supplier_id INTEGER NOT NULL REFERENCES supplier(id), "
                            + "sent_at TEXT, received_at TEXT, "
                            + "extra_cost TEXT NOT NULL DEFAULT '', relaxation TEXT NOT NULL DEFAULT '', note TEXT NOT NULL DEFAULT '', "
                            + "PRIMARY KEY (anken_id, supplier_id))");
                        Exec(conn, tx, "CREATE TABLE quote ("
                            + "anken_id INTEGER NOT NULL, supplier_id INTEGER NOT NULL, "
                            + "pattern_id INTEGER NOT NULL REFERENCES quantity_pattern(id), "
                            + "unit_price TEXT, lead_time_days INTEGER, "
                            + "PRIMARY KEY (anken_id, supplier_id, pattern_id), "
                            + "FOREIGN KEY (anken_id, supplier_id) REFERENCES anken_supplier(anken_id, supplier_id) ON DELETE CASCADE)");
                        Exec(conn, tx, "PRAGMA user_version = 2");
                        tx.Commit();
                    }
                }

                if (version < 3)
                {
                    using (var tx = conn.BeginTransaction())
                    {
                        Exec(conn, tx, "CREATE TABLE answer_history ("
                            + "id INTEGER PRIMARY KEY AUTOINCREMENT, anken_id INTEGER NOT NULL, supplier_id INTEGER NOT NULL, "
                            + "version INTEGER NOT NULL, archived_at TEXT NOT NULL, sent_at TEXT, received_at TEXT, "
                            + "extra_cost TEXT NOT NULL, relaxation TEXT NOT NULL, note TEXT NOT NULL, files TEXT NOT NULL, "
                            + "UNIQUE (anken_id, supplier_id, version), "
                            + "FOREIGN KEY (anken_id, supplier_id) REFERENCES anken_supplier(anken_id, supplier_id) ON DELETE CASCADE)");
                        Exec(conn, tx, "CREATE TABLE quote_history ("
                            + "history_id INTEGER NOT NULL REFERENCES answer_history(id) ON DELETE CASCADE, "
                            + "pattern_id INTEGER NOT NULL REFERENCES quantity_pattern(id), "
                            + "unit_price TEXT, lead_time_days INTEGER, "
                            + "PRIMARY KEY (history_id, pattern_id))");
                        Exec(conn, tx, "PRAGMA user_version = 3");
                        tx.Commit();
                    }
                }

                if (version < 4)
                {
                    using (var tx = conn.BeginTransaction())
                    {
                        // メールの記録。本文はコピーしない（HANDOFF.md §4）。調達先を案件から外しても記録は残す。
                        Exec(conn, tx, "CREATE TABLE mail_log ("
                            + "id INTEGER PRIMARY KEY AUTOINCREMENT, anken_id INTEGER NOT NULL REFERENCES anken(id), supplier_id INTEGER, "
                            + "kind TEXT NOT NULL, to_address TEXT NOT NULL, subject TEXT NOT NULL, status TEXT NOT NULL, "
                            + "entry_id TEXT, sent_at TEXT, msg_path TEXT, created_at TEXT NOT NULL)");
                        Exec(conn, tx, "PRAGMA user_version = 4");
                        tx.Commit();
                    }
                }

                if (version < 5)
                {
                    using (var tx = conn.BeginTransaction())
                    {
                        // 案件の進み具合。外した調達先の控え（戻せるように、その時点の回答と単価を文字で残す）。
                        Exec(conn, tx, "ALTER TABLE anken ADD COLUMN status TEXT NOT NULL DEFAULT '見積中'");
                        Exec(conn, tx, "CREATE TABLE removed_supplier ("
                            + "id INTEGER PRIMARY KEY AUTOINCREMENT, anken_id INTEGER NOT NULL, supplier_id INTEGER NOT NULL, "
                            + "sent_at TEXT, received_at TEXT, extra_cost TEXT NOT NULL, relaxation TEXT NOT NULL, note TEXT NOT NULL, "
                            + "quotes TEXT NOT NULL, removed_at TEXT NOT NULL)");
                        Exec(conn, tx, "PRAGMA user_version = 5");
                        tx.Commit();
                    }
                }

                if (version < 6)
                {
                    using (var tx = conn.BeginTransaction())
                    {
                        // 結果入力、調達先の得意分野、メモ、見積の内訳、客先への提出単価。
                        Exec(conn, tx, "ALTER TABLE anken ADD COLUMN result_date TEXT");
                        Exec(conn, tx, "ALTER TABLE anken ADD COLUMN result_note TEXT NOT NULL DEFAULT ''");
                        Exec(conn, tx, "ALTER TABLE anken ADD COLUMN adopted_supplier_id INTEGER");
                        Exec(conn, tx, "ALTER TABLE supplier ADD COLUMN specialty TEXT NOT NULL DEFAULT ''");
                        Exec(conn, tx, "ALTER TABLE removed_supplier ADD COLUMN breakdowns TEXT NOT NULL DEFAULT ''");
                        Exec(conn, tx, "CREATE TABLE anken_note ("
                            + "id INTEGER PRIMARY KEY AUTOINCREMENT, anken_id INTEGER NOT NULL REFERENCES anken(id), supplier_id INTEGER, "
                            + "created_at TEXT NOT NULL, text TEXT NOT NULL, source TEXT NOT NULL DEFAULT '')");
                        Exec(conn, tx, "CREATE TABLE quote_breakdown ("
                            + "anken_id INTEGER NOT NULL, supplier_id INTEGER NOT NULL, pattern_id INTEGER NOT NULL REFERENCES quantity_pattern(id), "
                            + "seq INTEGER NOT NULL, item TEXT NOT NULL, amount TEXT NOT NULL, "
                            + "PRIMARY KEY (anken_id, supplier_id, pattern_id, seq), "
                            + "FOREIGN KEY (anken_id, supplier_id) REFERENCES anken_supplier(anken_id, supplier_id) ON DELETE CASCADE)");
                        Exec(conn, tx, "CREATE TABLE client_price ("
                            + "anken_id INTEGER NOT NULL REFERENCES anken(id), pattern_id INTEGER NOT NULL REFERENCES quantity_pattern(id), "
                            + "selling_price TEXT, adopted_supplier_id INTEGER, PRIMARY KEY (anken_id, pattern_id))");
                        Exec(conn, tx, "PRAGMA user_version = 6");
                        tx.Commit();
                    }
                }

                if (version < 7)
                {
                    using (var tx = conn.BeginTransaction())
                    {
                        // Outlookの予定表に入れた回答期限の予定（あとで更新・削除できるように、EntryIDを持つ）。
                        Exec(conn, tx, "ALTER TABLE anken ADD COLUMN calendar_entry_id TEXT");
                        Exec(conn, tx, "PRAGMA user_version = 7");
                        tx.Commit();
                    }
                }
            }
        }

        private static void Exec(SqliteConnection conn, SqliteTransaction tx, string sql)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();
            }
        }

        private static string CheckClientName(string name)
        {
            var n = (name ?? "").Trim();
            if (n.Length == 0)
            {
                throw new ArgumentException("名前が空です。");
            }

            if (!Sanitizer.IsValidName(n))
            {
                throw new ArgumentException("フォルダ名に使えない文字（\\ / : * ? \" < > |）は使えません。");
            }

            return n;
        }

        private static bool IsUniqueViolation(SqliteException ex)
        {
            // SQLITE_CONSTRAINT = 19
            return ex.SqliteErrorCode == 19;
        }

        private static DateTime ParseDate(string s)
        {
            return DateTime.ParseExact(s, DateFormat, CultureInfo.InvariantCulture);
        }
    }
}
