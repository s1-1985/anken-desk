using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace AnkenDesk.App
{
    /// <summary>案件フォルダを作る場所（Work space）を変える。</summary>
    internal sealed class SettingsForm : Form
    {
        private readonly AppServices _services;
        private readonly TextBox _path;

        public SettingsForm(AppServices services)
        {
            _services = services;
            UiStyle.Apply(this);
            Text = "設定（Work spaceの場所）";
            ClientSize = new Size(720, 300);

            var help = new Label
            {
                Text = "案件を登録すると、ここに「得意先・種別のフォルダ」と「案件フォルダ」が作られます。\r\n"
                    + "初期値は、検証用のフォルダです。本物のWork spaceに変えるときは、確認が出ます。\r\n"
                    + "場所を変えても、すでにあるフォルダは移動しません。",
                Left = 16, Top = 16, Width = 688, Height = 80,
            };

            _path = new TextBox { Left = 16, Top = 110, Width = 580, Text = services.WorkspaceRoot };
            var browse = UiStyle.CreateButton("参照...", false, 100);
            browse.Left = 604; browse.Top = 104;
            browse.Click += (s, e) => Browse();

            var reset = UiStyle.CreateButton("検証用のフォルダに戻す", false, 240);
            reset.Left = 16; reset.Top = 160;
            reset.Click += (s, e) => _path.Text = AppServices.DefaultWorkspaceRoot();

            var save = UiStyle.CreateButton("保存", true, 140);
            save.Left = 424; save.Top = 236;
            save.Click += (s, e) => Save();

            var cancel = UiStyle.CreateButton("キャンセル", false, 140);
            cancel.Left = 570; cancel.Top = 236;
            cancel.Click += (s, e) => DialogResult = DialogResult.Cancel;

            Controls.AddRange(new Control[] { help, _path, browse, reset, save, cancel });
        }

        private void Browse()
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "案件フォルダを作る場所を選んでください";
                if (Directory.Exists(_path.Text))
                {
                    dlg.SelectedPath = _path.Text;
                }

                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _path.Text = dlg.SelectedPath;
                }
            }
        }

        private void Save()
        {
            var path = _path.Text.Trim();
            var isDefault = string.Equals(path, AppServices.DefaultWorkspaceRoot(), System.StringComparison.OrdinalIgnoreCase);

            if (isDefault)
            {
                Directory.CreateDirectory(path);
            }
            else if (!Directory.Exists(path))
            {
                MessageBox.Show(this, "そのフォルダが見つかりません。\r\n" + path, "設定", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!isDefault)
            {
                var ok = MessageBox.Show(this,
                    "検証用ではないフォルダに、案件フォルダを作る設定になります。\r\n\r\n" + path + "\r\n\r\nよろしいですか？",
                    "設定の確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
                if (ok != DialogResult.Yes)
                {
                    return;
                }
            }

            _services.WorkspaceRoot = path;
            DialogResult = DialogResult.OK;
        }
    }
}
