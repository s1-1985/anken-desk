using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using AnkenDesk.Core;

namespace AnkenDesk.App
{
    /// <summary>登録済みの案件と、実際のフォルダのずれを調べる。調べるだけなら読み取りだけ。サブフォルダを作るときは確認を出す。</summary>
    internal sealed class FolderCheckForm : Form
    {
        private readonly AppServices _services;
        private readonly ListView _list = new ListView();
        private readonly Label _summary = new Label();
        private readonly Button _repair;
        private IReadOnlyList<FolderProblem> _problems = new List<FolderProblem>();

        public FolderCheckForm(AppServices services)
        {
            _services = services;
            UiStyle.Apply(this);
            Text = "登録済みの案件のフォルダを確認";
            ClientSize = new Size(1000, 640);

            Controls.Add(new Label
            {
                Text = "登録済みの案件のフォルダを確認",
                Font = new Font("BIZ UDPGothic", 18F, FontStyle.Bold),
                Left = 16, Top = 10, Width = 700, Height = 40,
            });
            Controls.Add(new Label
            {
                Text = "調べる場所: " + _services.WorkspaceRoot + "\r\n"
                    + "フォルダの名前を変えたり、動かしたり、消したりして、記録とずれていないかを調べます（読み取りだけ）。",
                Left = 16, Top = 52, Width = 968, Height = 50,
                ForeColor = Color.FromArgb(90, 96, 100),
            });

            var check = UiStyle.CreateButton("確認する", true, 160);
            check.Left = 16; check.Top = 110;
            check.Click += (s, e) => Check();
            _summary.SetBounds(190, 114, 790, 32);
            _summary.TextAlign = ContentAlignment.MiddleLeft;
            Controls.AddRange(new Control[] { check, _summary });

            _list.SetBounds(16, 158, 968, 400);
            _list.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.GridLines = true;
            _list.Columns.Add("状態", 260);
            _list.Columns.Add("得意先・種別", 220);
            _list.Columns.Add("案件", 460);
            _list.HideSelection = false;
            Controls.Add(_list);

            var repoint = UiStyle.CreateButton("選んだ案件のフォルダを指定し直す...", false, 380);
            repoint.Left = 16; repoint.Top = 580;
            repoint.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            repoint.Click += (s, e) => Repoint();
            Controls.Add(repoint);

            _repair = UiStyle.CreateButton("足りないサブフォルダを作る", true, 280);
            _repair.Left = 420; _repair.Top = 580;
            _repair.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            _repair.Enabled = false;
            _repair.Click += (s, e) => Repair();
            var close = UiStyle.CreateButton("閉じる", false, 140);
            close.Left = 844; close.Top = 580;
            close.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            close.Click += (s, e) => Close();
            Controls.AddRange(new Control[] { _repair, close });
        }

        private void Check()
        {
            try
            {
                var root = _services.WorkspaceRoot;
                var ankens = _services.Db.ListAnkens();
                _problems = Background.Run<IReadOnlyList<FolderProblem>>(this, "フォルダを調べています。\r\nしばらくお待ちください。",
                    () => FolderCheck.Run(root, ankens));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "調べられませんでした。\r\n\r\n" + ex.Message, "案件デスク", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var p in _problems)
            {
                var item = new ListViewItem(p.Text);
                item.SubItems.Add(p.Anken.ClientName);
                item.SubItems.Add(Home.Title(p.Anken) + "　（" + p.Anken.FolderPath + "）");
                if (p.FolderMissing)
                {
                    item.ForeColor = UiStyle.Danger;
                }

                _list.Items.Add(item);
            }

            _list.EndUpdate();
            var missing = _problems.Count(p => p.FolderMissing);
            var partial = _problems.Count - missing;
            _summary.Text = _problems.Count == 0
                ? "問題はありません（案件 " + _services.Db.ListAnkens().Count + " 件）"
                : "フォルダが見つからない " + missing + " 件／サブフォルダが足りない " + partial + " 件";
            _repair.Enabled = partial > 0;
        }

        // フォルダが見つからない案件の、新しい場所を指定する（名前を変えた・動かした場合）。DBの記録だけを直す。
        private void Repoint()
        {
            if (_list.SelectedIndices.Count == 0)
            {
                MessageBox.Show(this, "一覧から案件を選んでください。", "案件デスク", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var p = _problems[_list.SelectedIndices[0]];
            var root = _services.WorkspaceRoot.TrimEnd(System.IO.Path.DirectorySeparatorChar);
            using (var dlg = new FolderBrowserDialog { Description = "「" + Home.Title(p.Anken) + "」の、今のフォルダを選んでください", SelectedPath = root })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                var chosen = dlg.SelectedPath.TrimEnd(System.IO.Path.DirectorySeparatorChar);
                if (!chosen.StartsWith(root + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(this, "Work spaceの中のフォルダを選んでください。\r\n" + root, "案件デスク", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var relative = chosen.Substring(root.Length + 1);
                var ok = MessageBox.Show(this, "この案件のフォルダを、次の場所として記録します。\r\n\r\n" + relative + "\r\n\r\nフォルダやファイルは動かしません。よろしいですか？",
                    "フォルダを指定し直す", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (ok != DialogResult.Yes)
                {
                    return;
                }

                try
                {
                    _services.Db.UpdateAnkenFolder(p.Anken.Id, relative);
                }
                catch (InvalidOperationException ex)
                {
                    MessageBox.Show(this, ex.Message, "案件デスク", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            Check();
        }

        private void Repair()
        {
            var targets = _problems.Where(p => !p.FolderMissing).ToList();
            if (targets.Count == 0)
            {
                return;
            }

            var ask = MessageBox.Show(this,
                targets.Count + " 件の案件で、足りないサブフォルダ（空のフォルダ）を作ります。\r\n既にあるフォルダやファイルには触りません。\r\n\r\nよろしいですか？",
                "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (ask != DialogResult.Yes)
            {
                return;
            }

            var made = 0;
            try
            {
                foreach (var p in targets)
                {
                    made += FolderCheck.CreateMissingSubfolders(_services.WorkspaceRoot, p);
                }
            }
            catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException)
            {
                MessageBox.Show(this, "途中で止まりました。\r\n\r\n" + ex.Message, "案件デスク", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            MessageBox.Show(this, made + " 個のフォルダを作りました。", "案件デスク", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Check();
        }
    }
}
