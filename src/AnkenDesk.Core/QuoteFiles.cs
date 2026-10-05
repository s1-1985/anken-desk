using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AnkenDesk.Core
{
    /// <summary>
    /// 調達先の見積書ファイルの保存と、旧版フォルダへの移動。
    /// 保存先は案件フォルダの「5.調達先見積もり」。名前は「略称　元のファイル名」（HANDOFF.md §3.3）。
    /// 上書きはしない（同名があれば連番）。ファイルの削除はしない（旧版は移すだけ）。
    /// </summary>
    public static class QuoteFiles
    {
        public static string QuoteDir(string ankenFullPath)
        {
            return Path.Combine(ankenFullPath, FolderNames.Subfolders[4]);
        }

        public static string OldDir(string ankenFullPath)
        {
            return Path.Combine(QuoteDir(ankenFullPath), FolderNames.OldVersionFolder);
        }

        /// <summary>「略称　元のファイル名」。使えない文字は「_」にする。</summary>
        public static string BuildFileName(string shortName, string originalFileName)
        {
            var s = Sanitizer.FileName(shortName);
            var o = Sanitizer.FileName(originalFileName);
            if (s.Length == 0)
            {
                throw new ArgumentException("略称が空です。", nameof(shortName));
            }

            if (o.Length == 0)
            {
                throw new ArgumentException("ファイル名が空です。", nameof(originalFileName));
            }

            return s + FolderNames.Separator + o;
        }

        /// <summary>同名のファイルがあれば「名前 (2).拡張子」「名前 (3).拡張子」…と連番を付けた、まだ無いパスを返す。</summary>
        public static string UniquePath(string dir, string fileName)
        {
            var path = Path.Combine(dir, fileName);
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                return path;
            }

            var stem = Path.GetFileNameWithoutExtension(fileName);
            var ext = Path.GetExtension(fileName);
            for (var n = 2; ; n++)
            {
                path = Path.Combine(dir, stem + " (" + n + ")" + ext);
                if (!File.Exists(path) && !Directory.Exists(path))
                {
                    return path;
                }
            }
        }

        /// <summary>見積書を「5.調達先見積もり」へ<b>コピー</b>して保存する（元のファイルは動かさない）。保存した先のフルパスを返す。</summary>
        public static string Save(string ankenFullPath, string shortName, string sourcePath)
        {
            if (!Directory.Exists(ankenFullPath))
            {
                throw new DirectoryNotFoundException("案件フォルダが見つかりません: " + ankenFullPath);
            }

            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("保存するファイルが見つかりません。", sourcePath);
            }

            var dir = QuoteDir(ankenFullPath);
            Directory.CreateDirectory(dir);
            var dest = UniquePath(dir, BuildFileName(shortName, Path.GetFileName(sourcePath)));
            File.Copy(sourcePath, dest, false);
            return dest;
        }

        /// <summary>その調達先の、今の見積書（5番のフォルダ直下で「略称　」で始まるファイル）。名前の順。</summary>
        public static IReadOnlyList<string> ListCurrent(string ankenFullPath, string shortName)
        {
            return ListWithPrefix(QuoteDir(ankenFullPath), shortName);
        }

        /// <summary>その調達先の、旧版の見積書（旧版フォルダの中）。</summary>
        public static IReadOnlyList<string> ListOld(string ankenFullPath, string shortName)
        {
            return ListWithPrefix(OldDir(ankenFullPath), shortName);
        }

        /// <summary>見積書を旧版フォルダへ<b>移す</b>。移す前と後のフルパスの組を返す。削除はしない。</summary>
        public static IReadOnlyList<KeyValuePair<string, string>> MoveToOld(string ankenFullPath, IEnumerable<string> fileNames)
        {
            var moves = new List<KeyValuePair<string, string>>();
            var oldDir = OldDir(ankenFullPath);
            try
            {
                foreach (var name in fileNames)
                {
                    var from = Path.Combine(QuoteDir(ankenFullPath), name);
                    if (!File.Exists(from))
                    {
                        continue;
                    }

                    Directory.CreateDirectory(oldDir);
                    var to = UniquePath(oldDir, name);
                    File.Move(from, to);
                    moves.Add(new KeyValuePair<string, string>(from, to));
                }
            }
            catch
            {
                Undo(moves);
                throw;
            }

            return moves;
        }

        /// <summary>MoveToOld で移したものを、元の場所へ戻す（失敗した順に、できるだけ戻す）。</summary>
        public static void Undo(IEnumerable<KeyValuePair<string, string>> moves)
        {
            foreach (var m in moves.Reverse())
            {
                try
                {
                    if (File.Exists(m.Value) && !File.Exists(m.Key))
                    {
                        File.Move(m.Value, m.Key);
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        private static IReadOnlyList<string> ListWithPrefix(string dir, string shortName)
        {
            if (!Directory.Exists(dir))
            {
                return new List<string>();
            }

            var prefix = Sanitizer.FileName(shortName) + FolderNames.Separator;
            return Directory.GetFiles(dir)
                .Select(Path.GetFileName)
                .Where(n => n != null && n.StartsWith(prefix, StringComparison.Ordinal))
                .Select(n => n!)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();
        }
    }
}
