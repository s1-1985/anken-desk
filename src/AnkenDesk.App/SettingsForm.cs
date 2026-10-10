using System;
using System.Linq;
using System.Drawing;
using AnkenDesk.Core;
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
            Text = "設定・DBの控え";
            ClientSize = new Size(720, 420);

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
            save.Left = 424; save.Top = 356;
            save.Click += (s, e) => Save();

            var cancel = UiStyle.CreateButton("キャンセル", false, 140);
            cancel.Left = 570; cancel.Top = 356;
            cancel.Click += (s, e) => DialogResult = DialogResult.Cancel;

            var backupHelp = new Label
            {
                Text = "DBの控え: 起動のたびに、1日1つ、最新14個を自動で残しています。\r\n間違えて消したり、おかしくなったときは、控えから戻せます（戻す前の状態も残ります）。",
                Left = 16, Top = 224, Width = 688, Height = 54,
            };
            var restore = UiStyle.CreateButton("DBの控えから戻す...", false, 260);
            restore.Left = 16; restore.Top = 284;
            restore.Click += (s, e) => RestoreFromBackup();
            var openBackup = UiStyle.CreateButton("控えのフォルダを開く", false, 240);
            openBackup.Left = 286; openBackup.Top = 284;
            openBackup.Click += (s, e) =>
            {
                var dir = Path.Combine(AppServices.DataDir(), "backup");
                Directory.CreateDirectory(dir);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = true });
            };

            Controls.AddRange(new Control[] { help, _path, browse, reset, backupHelp, restore, openBackup, save, cancel });
        }

        // 控えを選んで、復元を予約する。入れ替えは、次にアプリを開いたとき（DBを開く前）に行う。
        private void RestoreFromBackup()
        {
            var backups = DbBackup.List(Path.Combine(AppServices.DataDir(), "backup"));
            if (backups.Count == 0)
            {
                MessageBox.Show(this, "DBの控えがまだありません。", "DBの控えから戻す", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var lines = backups.Select(b => b.At.ToString("yyyy/MM/dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) + "　の控え　（" + (b.Size / 1024) + " KB）").ToList();
            using (var dlg = new PickListForm("DBの控えから戻す", "戻す控えを選んでください。\r\nそれ以降に入力した内容は、戻したあとは無くなります（戻す前のDBも、控えのフォルダに残します）。", lines, "この控えに戻す"))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                var chosen = backups[dlg.SelectedIndexResult];
                var ok = MessageBox.Show(this,
                    chosen.At.ToString("yyyy/MM/dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) + " の控えに戻します。\r\n\r\nアプリを再起動して入れ替えます。よろしいですか？",
                    "DBの控えから戻す", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (ok != DialogResult.Yes)
                {
                    return;
                }

                try
                {
                    DbBackup.RequestRestore(chosen.Path, _services.Db.Path);
                }
                catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException)
                {
                    MessageBox.Show(this, "戻せませんでした。\r\n\r\n" + ex.Message, "DBの控えから戻す", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            _services.Db.Dispose(); // DBのファイルを掴んだままにしない（入れ替えのため）
            Application.Restart(); // 起動時に、予約された控えと入れ替わる
            Environment.Exit(0);
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
