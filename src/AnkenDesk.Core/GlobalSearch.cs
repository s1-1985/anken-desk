using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AnkenDesk.Core
{
    /// <summary>横断検索のヒット1件。</summary>
    public sealed class SearchHit
    {
        public AnkenRecord Anken { get; set; } = new AnkenRecord();

        /// <summary>案件／メモ／メール／回答／ファイル。</summary>
        public string Kind { get; set; } = "";
        public string Text { get; set; } = "";

        /// <summary>ファイルのとき、フルパス。</summary>
        public string? Path { get; set; }
    }

    /// <summary>
    /// 全案件を横断して、案件の情報・メモ・メールの件名・回答のメモ・ファイル名から探す。
    /// 空白で区切った語が「すべて」含まれるものがヒット（大文字小文字・全角半角の空白は区別しない）。読み取りだけ。
    /// </summary>
    public static class GlobalSearch
    {
        public static IReadOnlyList<SearchHit> Run(AnkenDb db, string workspaceRoot, string query, int maxHits = 500)
        {
            var tokens = (query ?? "").Split(new[] { ' ', '　' }, StringSplitOptions.RemoveEmptyEntries);
            var hits = new List<SearchHit>();
            if (tokens.Length == 0)
            {
                return hits;
            }

            var ankens = db.ListAnkens();
            var byId = ankens.ToDictionary(a => a.Id);
            var suppliersByAnken = db.ListAllAnkenSuppliers();

            Func<string, bool> match = text => tokens.All(t => (text ?? "").IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);

            foreach (var a in ankens)
            {
                var info = Home.Title(a) + " " + a.ClientName + " " + a.PartName + " " + a.Status + " " + a.ResultNote;
                if (match(info))
                {
                    hits.Add(new SearchHit { Anken = a, Kind = "案件", Text = Home.Title(a) + (string.IsNullOrWhiteSpace(a.PartName) ? "" : "　" + a.PartName) });
                }

                IReadOnlyList<AnkenSupplier>? list;
                if (suppliersByAnken.TryGetValue(a.Id, out list))
                {
                    foreach (var s in list)
                    {
                        var text = s.SupplierName + " " + s.ExtraCost + " " + s.Relaxation + " " + s.Note;
                        if (match(text) && (s.ExtraCost + s.Relaxation + s.Note).Length > 0)
                        {
                            hits.Add(new SearchHit { Anken = a, Kind = "回答", Text = s.ShortName + "：" + Join(s.ExtraCost, s.Relaxation, s.Note) });
                        }
                    }
                }
            }

            foreach (var n in db.ListAllNotes())
            {
                AnkenRecord? a;
                if (byId.TryGetValue(n.AnkenId, out a) && match(n.Text))
                {
                    hits.Add(new SearchHit { Anken = a, Kind = "メモ", Text = n.CreatedAt.ToString("yyyy/MM/dd") + "　" + OneLine(n.Text) });
                }
            }

            foreach (var m in db.ListAllMailLog())
            {
                AnkenRecord? a;
                if (byId.TryGetValue(m.AnkenId, out a) && match(m.Subject + " " + m.ToAddress))
                {
                    hits.Add(new SearchHit { Anken = a, Kind = "メール", Text = MailSender.KindText(m.Kind) + "　" + (m.SentAt ?? m.CreatedAt).ToString("yyyy/MM/dd") + "　" + m.Subject });
                }
            }

            // ファイル名（案件フォルダの中を、サブフォルダも含めて）。ヒットが多いときは、ここで打ち切る。
            foreach (var a in ankens)
            {
                if (hits.Count >= maxHits)
                {
                    break;
                }

                var dir = System.IO.Path.Combine(workspaceRoot, a.FolderPath);
                if (!Directory.Exists(dir))
                {
                    continue;
                }

                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).ToList();
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    continue;
                }

                foreach (var f in files)
                {
                    if (match(System.IO.Path.GetFileName(f)))
                    {
                        hits.Add(new SearchHit { Anken = a, Kind = "ファイル", Text = f.Substring(dir.Length).TrimStart(System.IO.Path.DirectorySeparatorChar), Path = f });
                    }
                }
            }

            return hits.Take(maxHits).OrderBy(h => h.Anken.RequestDate, Comparer<DateTime>.Create((x, y) => y.CompareTo(x)))
                .ThenBy(h => h.Anken.Id).ThenBy(h => KindOrder(h.Kind)).ToList();
        }

        private static int KindOrder(string kind)
        {
            switch (kind)
            {
                case "案件": return 0;
                case "メモ": return 1;
                case "メール": return 2;
                case "回答": return 3;
                default: return 4;
            }
        }

        private static string OneLine(string text)
        {
            var t = (text ?? "").Replace("\r", "").Replace("\n", " ");
            return t.Length > 80 ? t.Substring(0, 80) + "…" : t;
        }

        private static string Join(params string[] parts)
        {
            return string.Join(" / ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => OneLine(p)));
        }
    }
}
