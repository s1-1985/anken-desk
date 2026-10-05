using System;
using System.IO;

namespace AnkenDesk.Core
{
    /// <summary>同名のフォルダが既にある。</summary>
    public sealed class DuplicateFolderException : Exception
    {
        public DuplicateFolderException(string path)
            : base("同じ名前のフォルダが既にあります: " + path)
        {
        }
    }

    /// <summary>案件を登録する。フォルダ（案件フォルダと15個のサブフォルダ）を作り、DBに保存する。</summary>
    public sealed class AnkenRegistrar
    {
        private readonly AnkenDb _db;
        private readonly string _workspaceRoot;

        public AnkenRegistrar(AnkenDb db, string workspaceRoot)
        {
            _db = db;
            _workspaceRoot = workspaceRoot;
        }

        public string WorkspaceRoot
        {
            get { return _workspaceRoot; }
        }

        /// <summary>Work spaceからの相対パス（得意先・種別のフォルダ名\案件フォルダ名）。</summary>
        public static string RelativePath(Client client, DateTime requestDate, string partNumber, string? note)
        {
            return System.IO.Path.Combine(client.Name, FolderNames.BuildAnkenFolderName(requestDate, partNumber, note));
        }

        public string FullPath(string relativePath)
        {
            return System.IO.Path.Combine(_workspaceRoot, relativePath);
        }

        public bool FolderExists(string relativePath)
        {
            return Directory.Exists(FullPath(relativePath));
        }

        /// <summary>
        /// 登録する。同名のフォルダがあるときは <see cref="DuplicateFolderException"/>（何も作らない）。
        /// DBへの保存に失敗したら、今作った案件フォルダ（中身は空）を片付ける。
        /// </summary>
        public AnkenRecord Register(AnkenInput input)
        {
            if (!Directory.Exists(_workspaceRoot))
            {
                throw new DirectoryNotFoundException("Work spaceのフォルダが見つかりません: " + _workspaceRoot);
            }

            var client = _db.GetClient(input.ClientId);
            if (client == null)
            {
                throw new InvalidOperationException("得意先・種別が選ばれていません。");
            }

            var relative = RelativePath(client, input.RequestDate, input.PartNumber, input.Note);
            var full = FullPath(relative);
            if (Directory.Exists(full))
            {
                throw new DuplicateFolderException(full);
            }

            Directory.CreateDirectory(full);
            try
            {
                foreach (var sub in FolderNames.Subfolders)
                {
                    Directory.CreateDirectory(System.IO.Path.Combine(full, sub));
                }

                return _db.InsertAnken(input, relative);
            }
            catch
            {
                TryRemoveEmptyTree(full);
                throw;
            }
        }

        // 今作ったばかりの、空のフォルダだけを消す（ファイルがあれば消さない）。
        private static void TryRemoveEmptyTree(string full)
        {
            try
            {
                foreach (var sub in Directory.GetDirectories(full))
                {
                    if (Directory.GetFileSystemEntries(sub).Length == 0)
                    {
                        Directory.Delete(sub);
                    }
                }

                if (Directory.GetFileSystemEntries(full).Length == 0)
                {
                    Directory.Delete(full);
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
}
