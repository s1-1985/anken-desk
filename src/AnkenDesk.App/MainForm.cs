using System.Drawing;
using System.Windows.Forms;
using AnkenDesk.Core;

namespace AnkenDesk.App
{
    /// <summary>骨組みの確認用の画面。ビルドして会社PCで起動できることを確かめるだけで、機能はまだない。</summary>
    internal sealed class MainForm : Form
    {
        public MainForm()
        {
            Text = AppInfo.Title;
            ClientSize = new Size(560, 240);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("BIZ UDPGothic", 11F);

            var label = new Label
            {
                Text = AppInfo.Title + "\r\n骨組みの確認用の画面です。機能はまだありません。",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
            };

            var close = new Button
            {
                Text = "閉じる",
                Dock = DockStyle.Bottom,
                Height = 40,
            };
            close.Click += (s, e) => Close();

            Controls.Add(label);
            Controls.Add(close);
        }
    }
}
