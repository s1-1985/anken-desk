namespace AnkenDesk.Core
{
    /// <summary>アプリ名とバージョン。画面の表示と、ビルドの動作確認に使う。</summary>
    public static class AppInfo
    {
        public const string Name = "案件デスク";
        public const string Version = "0.3.0";

        public static string Title
        {
            get { return Name + " " + Version; }
        }
    }
}
