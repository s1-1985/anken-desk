using System;
using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;

namespace AnkenDesk.Core
{
    /// <summary>
    /// SQLiteが動くかの確認用。DBを作り、1行書いて、読み戻す。業務データは扱わない。
    /// 会社PCで動くかが未確認だったため（HANDOFF.md §8 #11）、最初に小さく確かめる。
    /// </summary>
    public static class DbSmokeTest
    {
        /// <summary>結果を複数行の文字列で返す。例外は呼び出し側で表示する。</summary>
        public static string Run(string dbPath)
        {
            var dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var sb = new StringBuilder();
            sb.AppendLine("DBファイル: " + dbPath);

            var cs = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
            try
            {
                using (var conn = new SqliteConnection(cs))
                {
                    conn.Open();
                    sb.AppendLine("SQLiteのバージョン: " + Scalar(conn, "SELECT sqlite_version()"));

                    Exec(conn, "CREATE TABLE IF NOT EXISTS smoke (id INTEGER PRIMARY KEY AUTOINCREMENT, note TEXT NOT NULL, at TEXT NOT NULL)");

                    using (var insert = conn.CreateCommand())
                    {
                        insert.CommandText = "INSERT INTO smoke (note, at) VALUES ($note, $at)";
                        insert.Parameters.AddWithValue("$note", "日本語の書き込み確認");
                        insert.Parameters.AddWithValue("$at", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                        insert.ExecuteNonQuery();
                    }

                    sb.AppendLine("これまでの書き込み回数: " + Scalar(conn, "SELECT COUNT(*) FROM smoke"));
                    sb.AppendLine("最後の書き込み: " + Scalar(conn, "SELECT note || ' (' || at || ')' FROM smoke ORDER BY id DESC LIMIT 1"));
                }
            }
            finally
            {
                // 接続プールがファイルを掴んだままにしないようにする。
                SqliteConnection.ClearAllPools();
            }

            sb.AppendLine("結果: OK");
            return sb.ToString();
        }

        private static void Exec(SqliteConnection conn, string sql)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();
            }
        }

        private static string Scalar(SqliteConnection conn, string sql)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = sql;
                var v = cmd.ExecuteScalar();
                return v == null ? "" : Convert.ToString(v) ?? "";
            }
        }
    }
}
