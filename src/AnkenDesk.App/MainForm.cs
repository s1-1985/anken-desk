using System.Drawing;
using System.Windows.Forms;
using AnkenDesk.Core;

namespace AnkenDesk.App
{
    /// <summary>入口の画面。段1では、案件の登録と一覧、得意先・種別の管理、Work spaceの設定ができる。</summary>
    internal sealed class MainForm : Form
    {
        private readonly AppServices _services;
        private readonly Label _info;

        public MainForm(AppServices services)
        {
            _services = services;
            UiStyle.Apply(this);
            Text = AppInfo.Title;
            ClientSize = new Size(640, 520);
            StartPosition = FormStartPosition.CenterScreen;

            var title = new Label
            {
                Text = AppInfo.Name,
                Font = new Font("BIZ UDPGothic", 20F, FontStyle.Bold),
                Dock = DockStyle.Top,
                Height = 56,
                Padding = new Padding(16, 12, 0, 0),
            };

            _info = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 90,
                Padding = new Padding(16, 8, 16, 8),
            };

            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(16),
            };

            panel.Controls.Add(MenuButton("案件を登録", true, () => new RegisterForm(_services).ShowDialog(this)));
            panel.Controls.Add(MenuButton("案件一覧", false, () => new AnkenListForm(_services).ShowDialog(this)));
            panel.Controls.Add(MenuButton("得意先・種別の管理", false, () => new ClientsForm(_services).ShowDialog(this)));
            panel.Controls.Add(MenuButton("設定（Work spaceの場所）", false, () => new SettingsForm(_services).ShowDialog(this)));
            panel.Controls.Add(MenuButton("閉じる", false, Close));

            Controls.Add(panel);
            Controls.Add(_info);
            Controls.Add(title);

            Activated += (s, e) => RefreshInfo();
            RefreshInfo();
        }

        private Button MenuButton(string text, bool primary, System.Action action)
        {
            var b = UiStyle.CreateButton(text, primary, 360);
            b.Height = 48;
            b.Click += (s, e) =>
            {
                action();
                RefreshInfo();
            };
            return b;
        }

        private void RefreshInfo()
        {
            var kind = _services.IsDefaultWorkspace ? "（検証用のフォルダ）" : "";
            _info.Text = "案件フォルダを作る場所: " + _services.WorkspaceRoot + kind
                + "\r\nDBの場所: " + _services.Db.Path;
        }
    }
}
