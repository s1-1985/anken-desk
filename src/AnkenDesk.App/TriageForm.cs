using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using AnkenDesk.Core;
using AnkenDesk.OutlookAccess;

namespace AnkenDesk.App
{
    /// <summary>
    /// 受信箱の最近のメールを、案件・調達先ごとに自動で仕分けて、まとめて取り込む。
    /// 件名に案件の品番があり、差出人がその案件の調達先のアドレスと同じメールは「確実」として最初からチェック。
    /// 品番だけが合うものは「要確認」（チェックなし）。取り込み済みのメールは出さない。取り込む前に、確認画面を出す。
    /// </summary>
    internal sealed class TriageForm : Form
    {
        private readonly AppServices _services;
        private readonly ListView _list = new ListView();
        private readonly ComboBox _days = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly Label _summary = new Label();
        private readonly CheckBox _saveMsg = new CheckBox();
        private readonly CheckBox _markReceived = new CheckBox();
        private readonly CheckBox _recordNote = new CheckBox();
        private readonly Button _import;
        private IReadOnlyList<TriageProposal> _plan = new List<TriageProposal>();
        private bool _loading;

        public TriageForm(AppServices services)
        {
            _services = services;
            UiStyle.Apply(this);
            Text = "受信メールの自動仕分け";
            ClientSize = new Size(1280, 780);

            Controls.Add(new Label { Text = "受信メールの自動仕分け", Font = new Font("BIZ UDPGothic", 18F, FontStyle.Bold), Left = 16, Top = 10, Width = 600, Height = 40 });
            Controls.Add(new Label
            {
                Text = "受信箱の最近のメール（添付つき）を、件名の品番と差出人から、案件・調達先に振り分けます。「確実」（品番が件名にあり、差出人がその案件の調達先）は最初からチェック済み、「要確認」は人が確かめてからチェックします。\r\n取り込み済みのメールは出ません。受信箱は読むだけで、メールは動かしません。",
                Left = 16, Top = 52, Width = 1248, Height = 54, ForeColor = Color.FromArgb(90, 96, 100),
            });

            Controls.Add(new Label { Text = "期間", Left = 16, Top = 118, Width = 50, Height = 28 });
            _days.SetBounds(70, 114, 130, 32);
            _days.Items.AddRange(new object[] { "3日以内", "7日以内", "14日以内", "30日以内" });
            _days.SelectedIndex = 1;
            Controls.Add(_days);

            var scan = UiStyle.CreateButton("受信箱を調べる", true, 200);
            scan.Left = 214; scan.Top = 110;
            scan.Click += (s, e) => Scan();
            Controls.Add(scan);

            _summary.SetBounds(430, 118, 830, 28);
            Controls.Add(_summary);

            _list.SetBounds(16, 158, 1248, 440);
            _list.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            _list.View = View.Details;
            _list.CheckBoxes = true;
            _list.FullRowSelect = true;
            _list.GridLines = true;
            _list.HideSelection = false;
            _list.Columns.Add("確かさ", 80);
            _list.Columns.Add("受信日時", 130);
            _list.Columns.Add("差出人", 190);
            _list.Columns.Add("件名", 300);
            _list.Columns.Add("取り込み先（案件）", 230);
            _list.Columns.Add("調達先", 100);
            _list.Columns.Add("取り込む添付", 190);
            _list.ItemCheck += (s, e) =>
            {
                if (_loading)
                {
                    return;
                }

                // 調達先が分からないメールは、まとめては取り込めない（案件画面か「受信メールから取り込む」で個別に）。
                if (e.NewValue == CheckState.Checked && _plan[e.Index].Candidate.Supplier == null)
                {
                    e.NewValue = CheckState.Unchecked;
                    MessageBox.Show(this, "このメールは、調達先が分からないので、まとめては取り込めません。\r\n左のメニュー「受信メールから取り込む」で、調達先を選んで取り込んでください。",
                        "受信メールの自動仕分け", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

                BeginInvoke((Action)UpdateButton);
            };
            Controls.Add(_list);

            _saveMsg.SetBounds(16, 612, 400, 28);
            _saveMsg.Text = "メール（.msg）も保存する";
            _saveMsg.Checked = true;
            _markReceived.SetBounds(16, 642, 500, 28);
            _markReceived.Text = "「回答受領日」を、メールの受信日で入れる";
            _markReceived.Checked = true;
            _recordNote.SetBounds(16, 672, 500, 28);
            _recordNote.Text = "メールの内容を、メモに残す";
            _recordNote.Checked = true;
            Controls.AddRange(new Control[] { _saveMsg, _markReceived, _recordNote });

            _import = UiStyle.CreateButton("チェックしたメールを取り込む", true, 320);
            _import.Left = 780; _import.Top = 720;
            _import.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            _import.Enabled = false;
            _import.Click += (s, e) => ImportChecked();
            var close = UiStyle.CreateButton("閉じる", false, 140);
            close.Left = 1124; close.Top = 720;
            close.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            close.Click += (s, e) => Close();
            Controls.AddRange(new Control[] { _import, close });
            CancelButton = close;
        }

        private int Days()
        {
            return new[] { 3, 7, 14, 30 }[Math.Max(0, _days.SelectedIndex)];
        }

        private void Scan()
        {
            var days = Days();
            IReadOnlyList<InboundMail> mails;
            try
            {
                mails = Background.Run<IReadOnlyList<InboundMail>>(this, "Outlookの受信箱を読んでいます。\r\nしばらくお待ちください。", () =>
                {
                    using (var inbox = new OutlookInbox())
                    {
                        return inbox.ListRecent(days, 300);
                    }
                });
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(this, "受信箱を読み込めませんでした。\r\n\r\n" + ex.Message, "受信メールの自動仕分け", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var db = _services.Db;
            _plan = InboundTriage.Plan(mails, db.ListAnkens(), db.ListAllAnkenSuppliers(), db.ListSuppliers(), db.ListImportedEntryIds());

            _loading = true;
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var p in _plan)
            {
                var item = new ListViewItem(p.Confident ? "確実" : "要確認");
                item.SubItems.Add(p.Mail.ReceivedAt.ToString("MM/dd HH:mm", CultureInfo.InvariantCulture));
                item.SubItems.Add(string.IsNullOrEmpty(p.Mail.SenderName) ? p.Mail.SenderAddress : p.Mail.SenderName);
                item.SubItems.Add(p.Mail.Subject);
                item.SubItems.Add(Home.Title(p.Candidate.Anken));
                item.SubItems.Add(p.Candidate.Supplier == null ? "（不明）" : p.Candidate.Supplier.ShortName);
                item.SubItems.Add(string.Join("、", p.AttachmentIndexes.Select(i => p.Mail.Attachments.First(a => a.Index == i).FileName)));
                item.Checked = p.Confident && p.Candidate.Supplier != null;
                if (!p.Confident)
                {
                    item.ForeColor = Color.FromArgb(120, 90, 0);
                }

                _list.Items.Add(item);
            }

            _list.EndUpdate();
            _loading = false;

            var confident = _plan.Count(p => p.Confident);
            _summary.Text = mails.Count + " 通を調べて、仕分けられたのは " + _plan.Count + " 通（確実 " + confident + " 通／要確認 " + (_plan.Count - confident) + " 通）";
            UpdateButton();
        }

        private void UpdateButton()
        {
            var n = _list.CheckedItems.Count;
            _import.Enabled = n > 0;
            _import.Text = n > 0 ? n + " 通を取り込む" : "チェックしたメールを取り込む";
        }

        private void ImportChecked()
        {
            var chosen = _list.CheckedIndices.Cast<int>().Select(i => _plan[i]).Where(p => p.Candidate.Supplier != null).ToList();
            if (chosen.Count == 0)
            {
                return;
            }

            var saveMsg = _saveMsg.Checked;
            var markReceived = _markReceived.Checked;
            var recordNote = _recordNote.Checked;

            var confirm = new StringBuilder();
            confirm.AppendLine(chosen.Count + " 通のメールを、次のように取り込みます。");
            confirm.AppendLine();
            foreach (var p in chosen.Take(12))
            {
                confirm.AppendLine("・" + Home.Title(p.Candidate.Anken) + " ← " + p.Candidate.Supplier!.ShortName + "（添付 " + p.AttachmentIndexes.Count + " 件）");
            }

            if (chosen.Count > 12)
            {
                confirm.AppendLine("・ほか " + (chosen.Count - 12) + " 通");
            }

            confirm.AppendLine();
            confirm.AppendLine("添付は「5.調達先見積もり」へ（略称つきの名前、上書きしない）。"
                + (saveMsg ? "メールは .msg でも保存。" : "") + (markReceived ? "回答受領日を入れる。" : "") + (recordNote ? "内容をメモに残す。" : ""));
            if (MessageBox.Show(this, confirm.ToString(), "取り込みの確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                return;
            }

            var services = _services;
            var suppliers = services.Db.ListSuppliers().ToDictionary(s => s.Id);
            var workspace = services.WorkspaceRoot;
            List<string> report;
            List<string> saved;
            try
            {
                var savedFiles = new List<string>();
                report = Background.Run<List<string>>(this, "メールから取り込んでいます。\r\nしばらくお待ちください。", () =>
                {
                    var lines = new List<string>();
                    using (var inbox = new OutlookInbox())
                    {
                        foreach (var p in chosen)
                        {
                            var anken = p.Candidate.Anken;
                            var sup = suppliers[p.Candidate.Supplier!.SupplierId];
                            try
                            {
                                var outcome = InboundImporter.Import(inbox, services.Db, anken, System.IO.Path.Combine(workspace, anken.FolderPath), sup, p.Mail,
                                    p.AttachmentIndexes, saveMsg, markReceived ? (DateTime?)p.Mail.ReceivedAt.Date : null, DateTime.Now, recordNote);
                                savedFiles.AddRange(outcome.SavedFiles);
                                lines.Add((outcome.Errors.Count == 0 ? "○ " : "△ ") + Home.Title(anken) + " ← " + sup.ShortName + "：保存 " + outcome.SavedFiles.Count + " 件"
                                    + (outcome.Errors.Count > 0 ? "（失敗 " + outcome.Errors.Count + " 件: " + string.Join(" / ", outcome.Errors) + "）" : ""));
                            }
                            catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException)
                            {
                                lines.Add("× " + Home.Title(anken) + " ← " + sup.ShortName + "：" + ex.Message);
                            }
                        }
                    }

                    return lines;
                });
                saved = savedFiles;
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(this, "取り込めませんでした。\r\n\r\n" + ex.Message, "受信メールの自動仕分け", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            ThumbnailService.WarmAsync(saved);
            MessageBox.Show(this, string.Join("\r\n", report), "取り込みの結果", MessageBoxButtons.OK, report.Any(l => !l.StartsWith("○")) ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            Scan(); // 取り込んだメールを一覧から外す
        }
    }
}
