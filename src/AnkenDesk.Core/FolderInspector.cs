using System.Collections.Generic;
using System.IO;

namespace AnkenDesk.Core
{
    /// <summary>案件フォルダの中身（15個のサブフォルダと旧版フォルダ）のファイル数を数える。読むだけで、何も変えない。</summary>
    public sealed class FolderEntry
    {
        public string Name { get; set; } = "";

        /// <summary>そのフォルダの直下にあるファイルの数（中のフォルダは数えない）。</summary>
        public int FileCount { get; set; }

        public bool Exists { get; set; }

        /// <summary>旧版フォルダ（「5.調達先見積もり」の中）か。</summary>
        public bool IsOldVersion { get; set; }
    }

    public static class FolderInspector
    {
        public static IReadOnlyList<FolderEntry> Scan(string ankenFullPath)
        {
            var list = new List<FolderEntry>();
            foreach (var name in FolderNames.Subfolders)
            {
                var path = Path.Combine(ankenFullPath, name);
                list.Add(Count(name, path, false));

                // 5.調達先見積もり の直後に、旧版フォルダがあれば並べる。
                if (name == FolderNames.Subfolders[4])
                {
                    var old = Path.Combine(path, FolderNames.OldVersionFolder);
                    if (Directory.Exists(old))
                    {
                        list.Add(Count(FolderNames.OldVersionFolder, old, true));
                    }
                }
            }

            return list;
        }

        private static FolderEntry Count(string name, string path, bool isOld)
        {
            var exists = Directory.Exists(path);
            return new FolderEntry
            {
                Name = name,
                Exists = exists,
                IsOldVersion = isOld,
                FileCount = exists ? Directory.GetFiles(path).Length : 0,
            };
        }
    }
}
