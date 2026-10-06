using System;
using System.IO;
using AnkenDesk.Core;

namespace AnkenDesk.App
{
    /// <summary>画面から共通で使うもの（DBとWork spaceの場所）。</summary>
    internal sealed class AppServices : IDisposable
    {
        private const string WorkspaceKey = "workspace_root";

        public AnkenDb Db { get; }

        public AppServices()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AnkenDesk");
            var dbPath = Path.Combine(dir, "anken.db");

            // DBを開く前に、1日1回、控えを取る（失敗しても起動は続ける）。
            DbBackup.CreateDaily(dbPath, Path.Combine(dir, "backup"), DateTime.Now);
            Db = new AnkenDb(dbPath);

            // 初回は、本物のWork spaceではなくテスト用のフォルダにする（CLAUDE.md）。
            if (string.IsNullOrEmpty(Db.GetSetting(WorkspaceKey)))
            {
                Db.SetSetting(WorkspaceKey, DefaultWorkspaceRoot());
            }
        }

        /// <summary>案件フォルダを作る場所。設定で変えられる。</summary>
        public string WorkspaceRoot
        {
            get { return Db.GetSetting(WorkspaceKey) ?? DefaultWorkspaceRoot(); }
            set { Db.SetSetting(WorkspaceKey, value); }
        }

        public static string DefaultWorkspaceRoot()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "案件デスク検証");
        }

        public bool IsDefaultWorkspace
        {
            get { return string.Equals(WorkspaceRoot, DefaultWorkspaceRoot(), StringComparison.OrdinalIgnoreCase); }
        }

        /// <summary>設定が検証用フォルダのとき、まだ無ければ作る（本物のWork spaceは作らない）。</summary>
        public void EnsureDefaultWorkspaceExists()
        {
            if (IsDefaultWorkspace)
            {
                Directory.CreateDirectory(WorkspaceRoot);
            }
        }

        public AnkenRegistrar CreateRegistrar()
        {
            return new AnkenRegistrar(Db, WorkspaceRoot);
        }

        public void Dispose()
        {
            Db.Dispose();
        }
    }
}
