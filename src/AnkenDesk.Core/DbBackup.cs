using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace AnkenDesk.Core
{
    /// <summary>
    /// DBファイルの控え。起動時に、DBを開く前にコピーする（DBは1ファイルなので、閉じている間のコピーで足りる）。
    /// 1日に1つ。古いものから消して、最新の <c>keep</c> 個だけ残す。失敗しても、起動は止めない。
    /// </summary>
    /// <summary>控えの1ファイル。</summary>
    public sealed class BackupFile
    {
        public string Path { get; set; } = "";
        public DateTime At { get; set; }
        public long Size { get; set; }
    }

    public static class DbBackup
    {
        public const string Prefix = "anken-";

        /// <summary>控えを作る。作ったパス、作らなかった（DBが無い・今日の分がある・失敗）ときはnull。</summary>
        public static string? CreateDaily(string dbPath, string backupDir, DateTime now, int keep = 14)
        {
            try
            {
                if (!File.Exists(dbPath) || new FileInfo(dbPath).Length == 0)
                {
                    return null;
                }

                Directory.CreateDirectory(backupDir);
                var day = now.ToString("yyyyMMdd");
                if (Directory.GetFiles(backupDir, Prefix + day + "-*.db").Length > 0)
                {
                    return null;
                }

                var target = Path.Combine(backupDir, Prefix + day + "-" + now.ToString("HHmmss") + ".db");
                File.Copy(dbPath, target, false);
                Prune(backupDir, keep);
                return target;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>控えの一覧（新しい順）。</summary>
        public static IReadOnlyList<BackupFile> List(string backupDir)
        {
            var list = new List<BackupFile>();
            if (!Directory.Exists(backupDir))
            {
                return list;
            }

            foreach (var f in Directory.GetFiles(backupDir, Prefix + "*.db"))
            {
                var name = Path.GetFileNameWithoutExtension(f); // anken-YYYYMMDD-HHmmss
                DateTime at;
                if (name.Length == Prefix.Length + 15
                    && DateTime.TryParseExact(name.Substring(Prefix.Length), "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out at))
                {
                    list.Add(new BackupFile { Path = f, At = at, Size = new FileInfo(f).Length });
                }
            }

            return list.OrderByDescending(b => b.At).ToList();
        }

        /// <summary>置き場所: DBと同じフォルダの、復元待ちの印（このファイルがあれば、次の起動のとき入れ替える）。</summary>
        public static string RestoreMarkerPath(string dbPath)
        {
            return Path.Combine(Path.GetDirectoryName(dbPath) ?? ".", "anken.restore.db");
        }

        /// <summary>復元の予約。選んだ控えを復元待ちとしてコピーするだけ（DBは触らない）。入れ替えは次の起動のとき。</summary>
        public static void RequestRestore(string backupPath, string dbPath)
        {
            if (!IsSqliteFile(backupPath))
            {
                throw new InvalidDataException("DBの控えとして読めません: " + backupPath);
            }

            File.Copy(backupPath, RestoreMarkerPath(dbPath), true);
        }

        /// <summary>
        /// 復元待ちがあれば、今のDBを「before-restore-…」として控えフォルダに残してから、入れ替える。DBを開く前に呼ぶ。
        /// 入れ替えたら true。
        /// </summary>
        public static bool ApplyPendingRestore(string dbPath, string backupDir, DateTime now)
        {
            var marker = RestoreMarkerPath(dbPath);
            if (!File.Exists(marker))
            {
                return false;
            }

            if (!IsSqliteFile(marker))
            {
                File.Delete(marker);
                return false;
            }

            if (File.Exists(dbPath))
            {
                Directory.CreateDirectory(backupDir);
                File.Copy(dbPath, Path.Combine(backupDir, "before-restore-" + now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".db"), true);
                File.Delete(dbPath);
            }

            File.Move(marker, dbPath);
            return true;
        }

        private static bool IsSqliteFile(string path)
        {
            try
            {
                var header = new byte[16];
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (fs.Read(header, 0, 16) < 16)
                    {
                        return false;
                    }
                }

                return System.Text.Encoding.ASCII.GetString(header, 0, 15) == "SQLite format 3";
            }
            catch (IOException)
            {
                return false;
            }
        }

        private static void Prune(string backupDir, int keep)
        {
            // 名前に日時が入っているので、名前の順が古い順。
            var old = Directory.GetFiles(backupDir, Prefix + "*.db").OrderBy(f => f, StringComparer.Ordinal).ToList();
            for (var i = 0; i < old.Count - keep; i++)
            {
                File.Delete(old[i]);
            }
        }
    }
}
