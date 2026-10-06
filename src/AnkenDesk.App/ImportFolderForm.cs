using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using AnkenDesk.Core;

namespace AnkenDesk.App
{
    /// <summary>
    /// すでにあるフォルダを、案件としてDBに取り込む。
    /// 「試し読み」はフォルダを読むだけ。「取り込む」はDBにだけ書き、フォルダやファイルには触らない。
    /// </summary>
    internal sealed class ImportFolderForm : Form
    {
        private readonly AppServices _services;
        private readonly ListView _list = new ListView();
        private readonly Label _summary = new Label();
        private readonly Button _import;
        private IReadOnlyList<ImportCandidate> _candidates = new List<ImportCandidate>();

        public ImportFolderForm(AppServices services)
        {
            _services = services;
            UiStyle.Apply(this);
            Text = "既存のフォルダを取り込む";
            ClientSize = new Size(1180, 720);

            Controls.Add(new Label
            {
                Text = "既存のフォルダを取り込む",
                Font = new Font("BIZ UDPGothic", 18F, FontStyle.Bold),
                Left = 16, Top = 10, Width = 700, Height = 40,
            });
            Controls.Add(new Label
            {
                Text = "調べる場所: " + _services.WorkspaceRoot + "\r\n"
                    + "「試し読み」はフォルダを読むだけです。「取り込む」はアプリの記録（DB）にだけ書き、フォルダやファイルは作らず、動かさず、消しません。",
                Left = 16, Top = 52, Width = 1148, Height = 50,
                ForeColor = Color.FromArgb(90, 96, 100),
            });

            var scan = UiStyle.CreateButton("試し読み", true, 160);
            scan.Left = 16; scan.Top = 110;
            scan.Click += (s, e) => Scan();
            _summary.SetBounds(190, 114, 970, 32);
            _summary.TextAlign = ContentAlignment.MiddleLeft;
            Controls.AddRange(new Control[] { scan, _summary });

            _list.SetBounds(16, 158, 1148, 480);
            _list.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.GridLines = true;
            _list.HideSelection = false;
            _list.Columns.Add("状態", 200);
            _list.Columns.Add("得意先・種別", 200);
            _list.Columns.Add("フォルダ名", 330);
            _list.Columns.Add("品番", 150);
            _list.Columns.Add("備考", 160);
            _list.Columns.Add("不足", 90);
            Controls.Add(_list);

            _import = UiStyle.CreateButton("取り込む", true, 200);
            _import.Left = 744; _import.Top = 660;
            _import.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            _import.Enabled = false;
            _import.Click += (s, e) => DoImport();
            var close = UiStyle.CreateButton("閉じる", false, 140);
            close.Left = 1024; close.Top = 660;
            close.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            close.Click += (s, e) => Close();
            Controls.AddRange(new Control[] { _import, close });
        }

        private void Scan()
        {
            try
            {
                var root = _services.WorkspaceRoot;
                var registered = _services.Db.ListAnkens().Select(a => a.FolderPath).ToList();
                _candidates = Background.Run<IReadOnlyList<ImportCandidate>>(this, "フォルダを読んでいます。\r\nしばらくお待ちください。",
                    () => FolderImporter.Scan(root, registered));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "フォルダを読めませんでした。\r\n\r\n" + ex.Message, "案件デスク", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var c in _candidates)
            {
                var status = c.AlreadyRegistered ? "登録済み"
                    : c.Parsed == null ? "取り込めません（" + c.Problem + "）"
                    : "取り込めます";
                var item = new ListViewItem(status);
                item.SubItems.Add(c.ClientFolder);
                item.SubItems.Add(c.FolderName);
                item.SubItems.Add(c.Parsed == null ? "" : c.Parsed.PartNumber);
                item.SubItems.Add(c.Parsed == null ? "" : c.Parsed.Note);
                item.SubItems.Add(c.MissingSubfolders == 0 ? "" : c.MissingSubfolders + "個");
                if (c.Parsed == null && !c.AlreadyRegistered)
                {
                    item.ForeColor = UiStyle.Danger;
                }
                else if (c.AlreadyRegistered)
                {
                    item.ForeColor = Color.FromArgb(120, 126, 130);
                }

                _list.Items.Add(item);
            }

            _list.EndUpdate();

            var ok = _candidates.Count(c => c.CanImport);
            var done = _candidates.Count(c => c.AlreadyRegistered);
            var bad = _candidates.Count(c => c.Parsed == null && !c.AlreadyRegistered);
            _summary.Text = "全部で " + _candidates.Count + " 件　取り込める " + ok + " 件／登録済み " + done + " 件／取り込めない " + bad + " 件";
            _import.Enabled = ok > 0;
            _import.Text = ok > 0 ? ok + "件を取り込む" : "取り込む";
        }

        private void DoImport()
        {
            var targets = _candidates.Where(c => c.CanImport).ToList();
            if (targets.Count == 0)
            {
                return;
            }

            var newClients = targets.Select(t => t.ClientFolder).Distinct()
                .Count(n => !_services.Db.ListClients().Any(c => c.Name == n));
            var ask = MessageBox.Show(this,
                targets.Count + " 件の案件を、アプリの記録（DB）に登録します。\r\n"
                + "新しく作られる得意先・種別: " + newClients + " 件\r\n\r\n"
                + "・フォルダやファイルは、作りません・動かしません・消しません\r\n"
                + "・回答期限は分からないので、依頼日を入れます（あとで直せます）\r\n\r\n"
                + "よろしいですか？",
                "取り込みの確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (ask != DialogResult.Yes)
            {
                return;
            }

            List<string> failures;
            var n = FolderImporter.Apply(_services.Db, targets, out failures);
            var msg = n + " 件を取り込みました。";
            if (failures.Count > 0)
            {
                msg += "\r\n\r\n取り込めなかったもの:\r\n" + string.Join("\r\n", failures.Take(10));
            }

            MessageBox.Show(this, msg, "案件デスク", MessageBoxButtons.OK, failures.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            Scan();
        }
    }
}
