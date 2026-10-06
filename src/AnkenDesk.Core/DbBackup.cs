using System;
using System.IO;
using System.Linq;

namespace AnkenDesk.Core
{
    /// <summary>
    /// DBファイルの控え。起動時に、DBを開く前にコピーする（DBは1ファイルなので、閉じている間のコピーで足りる）。
    /// 1日に1つ。古いものから消して、最新の <c>keep</c> 個だけ残す。失敗しても、起動は止めない。
    /// </summary>
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
