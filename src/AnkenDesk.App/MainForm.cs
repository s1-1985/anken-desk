using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using AnkenDesk.Core;

namespace AnkenDesk.App
{
    /// <summary>骨組みの確認用の画面。会社PCでSQLiteが動くかを確かめるだけで、業務の機能はまだない。</summary>
    internal sealed class MainForm : Form
    {
        private readonly TextBox _result;

        public MainForm()
        {
            Text = AppInfo.Title;
            ClientSize = new Size(640, 360);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("BIZ UDPGothic", 11F);

            var title = new Label
            {
                Text = AppInfo.Title + "　（確認用の画面です。業務の機能はまだありません）",
                Dock = DockStyle.Top,
                Height = 40,
                TextAlign = ContentAlignment.MiddleLeft,
            };

            _result = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
            };

            var check = new Button { Text = "SQLiteの動作確認", Dock = DockStyle.Bottom, Height = 40 };
            check.Click += (s, e) => RunSmokeTest();

            var close = new Button { Text = "閉じる", Dock = DockStyle.Bottom, Height = 40 };
            close.Click += (s, e) => Close();

            Controls.Add(_result);
            Controls.Add(title);
            Controls.Add(check);
            Controls.Add(close);
        }

        private static string DbPath()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AnkenDesk");
            return Path.Combine(dir, "smoke.db");
        }

        private void RunSmokeTest()
        {
            try
            {
                _result.Text = DbSmokeTest.Run(DbPath()).Replace("\n", "\r\n");
            }
            catch (Exception ex)
            {
                _result.Text = "結果: NG\r\n" + ex.GetType().FullName + "\r\n" + ex.Message + "\r\n\r\n" + ex;
            }
        }
    }
}
