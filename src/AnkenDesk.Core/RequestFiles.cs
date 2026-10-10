using System;
using System.Collections.Generic;
using System.IO;

namespace AnkenDesk.Core
{
    /// <summary>1つのファイルの取り込み結果。</summary>
    public sealed class RequestFileResult
    {
        public string Source { get; set; } = "";

        /// <summary>コピー先のフルパス。失敗したときはnull。</summary>
        public string? Destination { get; set; }
        public string Error { get; set; } = "";
    }

    /// <summary>
    /// 客先から届いた依頼ファイルを、案件フォルダへ振り分けてコピーする（HANDOFF §5.4）。
    /// Excel（回答用）は「1.客先見積依頼内容」、PDF（図面）は「2.図面」。
    /// それ以外（zip、CADなど）は「1.客先見積依頼内容」【仮置き】。元のファイルは動かさない（コピー）。同名は連番。
    /// </summary>
    public static class RequestFiles
    {
        private static readonly string[] ExcelExtensions = { ".xlsx", ".xlsm", ".xls" };
        private static readonly string[] DrawingExtensions = { ".pdf" };

        /// <summary>振り分け先のサブフォルダ名。</summary>
        public static string TargetSubfolder(string fileName)
        {
            var ext = Path.GetExtension(fileName) ?? "";
            foreach (var e in DrawingExtensions)
            {
                if (string.Equals(ext, e, StringComparison.OrdinalIgnoreCase))
                {
                    return FolderNames.Subfolders[1];
                }
            }

            return FolderNames.Subfolders[0];
        }

        /// <summary>Excelかどうか（画面の表示用）。</summary>
        public static bool IsExcel(string fileName)
        {
            var ext = Path.GetExtension(fileName) ?? "";
            foreach (var e in ExcelExtensions)
            {
                if (string.Equals(ext, e, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 指定のサブフォルダ（<see cref="FolderNames.Subfolders"/> の番号）へ、そのままの名前でコピーする。
        /// 同名は連番。1つの失敗で残りを止めない。「どこへ保存するか」を人が選ぶときに使う。
        /// </summary>
        public static IReadOnlyList<RequestFileResult> CopyToSubfolder(string ankenFullPath, int subfolderIndex, IEnumerable<string> sources)
        {
            if (subfolderIndex < 0 || subfolderIndex >= FolderNames.Subfolders.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(subfolderIndex));
            }

            var results = new List<RequestFileResult>();
            foreach (var src in sources)
            {
                var r = new RequestFileResult { Source = src };
                try
                {
                    if (!File.Exists(src))
                    {
                        throw new FileNotFoundException("ファイルが見つかりません。");
                    }

                    var dir = Path.Combine(ankenFullPath, FolderNames.Subfolders[subfolderIndex]);
                    Directory.CreateDirectory(dir);
                    var dest = QuoteFiles.UniquePath(dir, Path.GetFileName(src));
                    File.Copy(src, dest, false);
                    r.Destination = dest;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    r.Error = ex.Message;
                }

                results.Add(r);
            }

            return results;
        }

        /// <summary>案件フォルダへコピーする。1つの失敗で残りを止めない。</summary>
        public static IReadOnlyList<RequestFileResult> CopyInto(string ankenFullPath, IEnumerable<string> sources)
        {
            var results = new List<RequestFileResult>();
            foreach (var src in sources)
            {
                var r = new RequestFileResult { Source = src };
                try
                {
                    if (!File.Exists(src))
                    {
                        throw new FileNotFoundException("ファイルが見つかりません。");
                    }

                    var name = Path.GetFileName(src);
                    var dir = Path.Combine(ankenFullPath, TargetSubfolder(name));
                    Directory.CreateDirectory(dir);
                    var dest = QuoteFiles.UniquePath(dir, name);
                    File.Copy(src, dest, false);
                    r.Destination = dest;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    r.Error = ex.Message;
                }

                results.Add(r);
            }

            return results;
        }
    }
}
