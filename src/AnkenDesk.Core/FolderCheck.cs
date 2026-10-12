using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AnkenDesk.Core
{
    /// <summary>登録済みの案件の、フォルダのずれ。</summary>
    public sealed class FolderProblem
    {
        public AnkenRecord Anken { get; set; } = new AnkenRecord();

        /// <summary>案件フォルダそのものが見つからない（名前の変更・移動・削除のどれか。どれかは分からない）。</summary>
        public bool FolderMissing { get; set; }

        /// <summary>無いサブフォルダの名前（案件フォルダがあるときだけ）。</summary>
        public IReadOnlyList<string> MissingSubfolders { get; set; } = new List<string>();

        public string Text
        {
            get
            {
                if (FolderMissing)
                {
                    return "フォルダが見つかりません";
                }

                return "サブフォルダが足りません（" + MissingSubfolders.Count + "個）";
            }
        }
    }

    /// <summary>DBに登録した案件と、実際のフォルダを突き合わせる（読み取りだけ）。</summary>
    public static class FolderCheck
    {
        public static IReadOnlyList<FolderProblem> Run(string workspaceRoot, IEnumerable<AnkenRecord> ankens)
        {
            var result = new List<FolderProblem>();
            foreach (var a in ankens)
            {
                var full = Path.Combine(workspaceRoot, a.FolderPath);
                if (!Directory.Exists(full))
                {
                    result.Add(new FolderProblem { Anken = a, FolderMissing = true });
                    continue;
                }

                var missing = FolderNames.Subfolders.Where(s => !Directory.Exists(Path.Combine(full, s))).ToList();
                if (missing.Count > 0)
                {
                    result.Add(new FolderProblem { Anken = a, MissingSubfolders = missing });
                }
            }

            return result;
        }

        /// <summary>足りないサブフォルダを作る（案件フォルダがあるものだけ。既存のものは触らない）。作った数を返す。</summary>
        public static int CreateMissingSubfolders(string workspaceRoot, FolderProblem problem)
        {
            if (problem.FolderMissing)
            {
                return 0;
            }

            var full = Path.Combine(workspaceRoot, problem.Anken.FolderPath);
            var n = 0;
            foreach (var s in problem.MissingSubfolders)
            {
                var p = Path.Combine(full, s);
                if (!Directory.Exists(p))
                {
                    Directory.CreateDirectory(p);
                    n++;
                }
            }

            return n;
        }
    }
}
