using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace AnkenDesk.Core
{
    /// <summary>フォルダ名「YYYYMMDD　品番[　備考]」を分解した結果。</summary>
    public sealed class ParsedFolderName
    {
        public DateTime RequestDate { get; set; }
        public string PartNumber { get; set; } = "";
        public string Note { get; set; } = "";
    }

    /// <summary>取り込み候補の1フォルダ。</summary>
    public sealed class ImportCandidate
    {
        /// <summary>得意先・種別のフォルダ名（Work space直下）。</summary>
        public string ClientFolder { get; set; } = "";
        public string FolderName { get; set; } = "";
        public string RelativePath { get; set; } = "";

        /// <summary>分解できたとき。できなければnull（理由は <see cref="Problem"/>）。</summary>
        public ParsedFolderName? Parsed { get; set; }
        public string Problem { get; set; } = "";
        public bool AlreadyRegistered { get; set; }

        /// <summary>15個のサブフォルダのうち、無いものの数（取り込みでは作らない）。</summary>
        public int MissingSubfolders { get; set; }

        public bool CanImport
        {
            get { return Parsed != null && !AlreadyRegistered; }
        }
    }

    /// <summary>
    /// 既存の案件フォルダを、DBへ取り込む（HANDOFF.md §8 #13）。
    /// 読み取りだけの <see cref="Scan"/> と、DBにだけ書く <see cref="Apply"/> に分けてある。
    /// どちらも、フォルダやファイルを作ったり、動かしたり、消したりしない。
    /// </summary>
    public static class FolderImporter
    {
        /// <summary>
        /// 「YYYYMMDD　品番」「YYYYMMDD　品番　備考」を分解する。区切りは全角スペース（HANDOFF §8 #1）。
        /// 備考の中の全角スペースは、そのまま備考に残す【仮置き】。
        /// </summary>
        public static ParsedFolderName? Parse(string folderName, out string problem)
        {
            problem = "";
            var name = folderName ?? "";
            if (name.Length < 10 || !name.Take(8).All(c => c >= '0' && c <= '9'))
            {
                problem = "先頭が8桁の日付ではありません";
                return null;
            }

            DateTime date;
            if (!DateTime.TryParseExact(name.Substring(0, 8), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            {
                problem = "日付として読めません";
                return null;
            }

            if (name[8] != FolderNames.Separator)
            {
                problem = "日付のあとが全角スペースではありません";
                return null;
            }

            var rest = name.Substring(9);
            var cut = rest.IndexOf(FolderNames.Separator);
            var part = (cut < 0 ? rest : rest.Substring(0, cut)).Trim();
            var note = cut < 0 ? "" : rest.Substring(cut + 1).Trim();
            if (part.Length == 0)
            {
                problem = "品番が空です";
                return null;
            }

            return new ParsedFolderName { RequestDate = date, PartNumber = part, Note = note };
        }

        /// <summary>
        /// Work space直下（得意先・種別）の、その下の案件フォルダを調べる（読み取りだけ）。
        /// 先頭が「.」「_」のフォルダは対象外。
        /// </summary>
        public static IReadOnlyList<ImportCandidate> Scan(string workspaceRoot, IEnumerable<string> registeredRelativePaths)
        {
            var registered = new HashSet<string>(registeredRelativePaths, StringComparer.OrdinalIgnoreCase);
            var result = new List<ImportCandidate>();
            if (!Directory.Exists(workspaceRoot))
            {
                return result;
            }

            foreach (var clientDir in SafeDirs(workspaceRoot))
            {
                var clientName = Path.GetFileName(clientDir);
                if (IsHidden(clientName))
                {
                    continue;
                }

                foreach (var ankenDir in SafeDirs(clientDir))
                {
                    var folderName = Path.GetFileName(ankenDir);
                    if (IsHidden(folderName))
                    {
                        continue;
                    }

                    var relative = Path.Combine(clientName, folderName);
                    string problem;
                    var parsed = Parse(folderName, out problem);
                    result.Add(new ImportCandidate
                    {
                        ClientFolder = clientName,
                        FolderName = folderName,
                        RelativePath = relative,
                        Parsed = parsed,
                        Problem = problem,
                        AlreadyRegistered = registered.Contains(relative),
                        MissingSubfolders = FolderNames.Subfolders.Count(s => !Directory.Exists(Path.Combine(ankenDir, s))),
                    });
                }
            }

            return result.OrderBy(c => c.ClientFolder, StringComparer.Ordinal).ThenBy(c => c.FolderName, StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// 取り込めるものだけをDBに登録する（得意先・種別が無ければ作る）。フォルダには触らない。
        /// 回答期限は分からないので、依頼日を入れる【仮置き】。数量・見積依頼書の項目は空。
        /// 1件の失敗で残りを止めない。戻り値は、登録できた件数。
        /// </summary>
        public static int Apply(AnkenDb db, IEnumerable<ImportCandidate> candidates, out List<string> failures)
        {
            failures = new List<string>();
            var clients = db.ListClients().ToDictionary(c => c.Name, c => c.Id, StringComparer.Ordinal);
            var count = 0;
            foreach (var c in candidates)
            {
                if (!c.CanImport)
                {
                    continue;
                }

                try
                {
                    long clientId;
                    if (!clients.TryGetValue(c.ClientFolder, out clientId))
                    {
                        clientId = db.AddClient(c.ClientFolder).Id;
                        clients[c.ClientFolder] = clientId;
                    }

                    var input = new AnkenInput
                    {
                        ClientId = clientId,
                        RequestDate = c.Parsed!.RequestDate,
                        PartNumber = c.Parsed.PartNumber,
                        Note = c.Parsed.Note,
                        ReplyDueDate = c.Parsed.RequestDate,
                    };
                    db.InsertAnken(input, c.RelativePath);
                    count++;
                }
                catch (Exception ex) when (ex is InvalidOperationException || ex is Microsoft.Data.Sqlite.SqliteException)
                {
                    failures.Add(c.RelativePath + ": " + ex.Message);
                }
            }

            return count;
        }

        private static bool IsHidden(string name)
        {
            return name.StartsWith(".", StringComparison.Ordinal) || name.StartsWith("_", StringComparison.Ordinal);
        }

        private static string[] SafeDirs(string path)
        {
            try
            {
                return Directory.GetDirectories(path);
            }
            catch (IOException)
            {
                return new string[0];
            }
            catch (UnauthorizedAccessException)
            {
                return new string[0];
            }
        }
    }
}
