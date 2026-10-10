using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using AnkenDesk.Core;
using AnkenDesk.OutlookAccess;

namespace AnkenDesk.App
{
    /// <summary>
    /// 受信箱のメールを検索して選び、添付の見積書を案件の「5.調達先見積もり」へ保存する（HANDOFF.md §6.3）。
    /// 取り込み先の案件・調達先は、件名の品番と差出人のアドレスから推定して初期値にするが、人が確認して選ぶ。
    /// 受信箱は読むだけで、メールを動かしたり消したりしない。
    /// </summary>
    internal sealed class ImportMailForm : Form
    {
        private sealed class AnkenItem
        {
            public AnkenRecord Anken = new AnkenRecord();

            public override string ToString()
            {
                return Home.Title(Anken) + "　／　" + Anken.ClientName;
            }
        }

        private sealed class SupplierItem
        {
            public Supplier Master = new Supplier();
            public AnkenSupplier Answer = new AnkenSupplier();

            public override string ToString()
            {
                return Master.ShortName + (Answer.IsAnswered ? "（回答済み）" : "");
            }
        }

        private readonly AppServices _services;
        private readonly long? _preselectAnkenId;

        private readonly TextBox _search = new TextBox();
        private readonly ComboBox _days = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly CheckBox _onlyWithAttachment = new CheckBox();
        private readonly DataGridView _grid = new DataGridView();
        private readonly Label _count = new Label();

        private readonly CheckedListBox _attachments = new CheckedListBox();
        private readonly ComboBox _ankenBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly ComboBox _supplierBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly CheckBox _markReceived = new CheckBox();
        private readonly DateTimePicker _receivedDate = new DateTimePicker { Format = DateTimePickerFormat.Short };
        private readonly CheckBox _saveMsg = new CheckBox();
        private readonly CheckBox _recordNote = new CheckBox();
        private readonly bool _fromDrop;
        private readonly Label _hint = new Label();
        private readonly Button _import;

        private List<InboundMail> _mails = new List<InboundMail>();
        private IReadOnlyList<AnkenRecord> _ankens = new List<AnkenRecord>();
        private IReadOnlyDictionary<long, IReadOnlyList<AnkenSupplier>> _ankenSuppliers = new Dictionary<long, IReadOnlyList<AnkenSupplier>>();
        private Dictionary<long, Supplier> _suppliers = new Dictionary<long, Supplier>();
        private Dictionary<string, InboundCandidate?> _candidates = new Dictionary<string, InboundCandidate?>();
        private InboundMail? _current;
        private bool _loading;

        /// <param name="fromDrop">Outlookからドラッグ&ドロップしたメール（Outlookで今選ばれているメール）を対象にする。</param>
        public ImportMailForm(AppServices services, long? preselectAnkenId, bool fromDrop = false)
        {
            _services = services;
            _preselectAnkenId = preselectAnkenId;
            _fromDrop = fromDrop;
            UiStyle.Apply(this);
            Text = fromDrop ? "ドロップしたメールを取り込む" : "受信メールから見積書を取り込む";
            var h = Math.Min(860, Screen.PrimaryScreen.WorkingArea.Height - 60);
            ClientSize = new Size(1280, h);

            Controls.Add(new Label
            {
                Text = "受信メールから見積書を取り込む",
                Font = new Font("BIZ UDPGothic", 18F, FontStyle.Bold),
                Left = 16, Top = 10, Width = 700, Height = 40,
            });
            Controls.Add(new Label
            {
                Text = "受信箱のメールを選び、添付の見積書を「5.調達先見積もり」へコピーして保存します。メールは動かしません。",
                Left = 16, Top = 50, Width = 1248, Height = 26,
                ForeColor = Color.FromArgb(90, 96, 100),
            });

            Controls.Add(new Label { Text = "検索", Left = 16, Top = 88, Width = 60, Height = 26 });
            _search.SetBounds(76, 84, 360, 32);
            _search.TextChanged += (s, e) => ApplyFilter();

            _days.SetBounds(448, 84, 150, 32);
            _days.Items.AddRange(new object[] { "7日以内", "14日以内", "30日以内", "90日以内" });
            _days.SelectedIndex = 2;

            _onlyWithAttachment.SetBounds(612, 86, 220, 30);
            _onlyWithAttachment.Text = "添付があるメールだけ";
            _onlyWithAttachment.Checked = !fromDrop; // ドロップしたメールは、添付が無くても内容を記録できる
            _onlyWithAttachment.CheckedChanged += (s, e) => ApplyFilter();

            var reload = UiStyle.CreateButton(fromDrop ? "選んだメールを読み直す" : "受信箱を読み込む", true, 220);
            reload.Left = 1044; reload.Top = 80;
            reload.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            reload.Click += (s, e) => LoadMails();

            _count.SetBounds(850, 88, 180, 26);
            _count.TextAlign = ContentAlignment.MiddleRight;

            Controls.AddRange(new Control[] { _search, _days, _onlyWithAttachment, reload, _count });

            BuildGrid(h);
            BuildBottom(h);

            _import = UiStyle.CreateButton("取り込む", true, 200);
            _import.Left = 848; _import.Top = h - 56;
            _import.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            _import.Click += (s, e) => DoImport();
            var close = UiStyle.CreateButton("閉じる", false, 140);
            close.Left = 1124; close.Top = h - 56;
            close.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            close.Click += (s, e) => Close();
            Controls.AddRange(new Control[] { _import, close });

            LoadMasters();
            Shown += (s, e) => LoadMails();
            UpdateButtons();
        }

        // ---- 画面の組み立て ----

        private void BuildGrid(int h)
        {
            _grid.SetBounds(16, 128, 1248, h - 128 - 270);
            _grid.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            _grid.ReadOnly = true;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.AllowUserToResizeRows = false;
            _grid.RowHeadersVisible = false;
            _grid.BackgroundColor = Color.White;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.MultiSelect = false;
            _grid.AutoGenerateColumns = false;
            _grid.DefaultCellStyle.Padding = new Padding(2);
            Col("受信日時", 150);
            Col("差出人", 200);
            var subject = Col("件名", 300);
            subject.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            Col("添付", 260);
            Col("取り込み先の候補", 260);
            _grid.SelectionChanged += (s, e) => OnSelect();
            Controls.Add(_grid);
        }

        private DataGridViewColumn Col(string header, int width)
        {
            var c = new DataGridViewTextBoxColumn { HeaderText = header, Width = width, SortMode = DataGridViewColumnSortMode.NotSortable };
            _grid.Columns.Add(c);
            return c;
        }

        private void BuildBottom(int h)
        {
            var p = new Panel { Left = 16, Top = h - 134 - 124, Width = 1248, Height = 124, BackColor = Color.White };
            p.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;

            p.Controls.Add(new Label { Text = "取り込む添付ファイル", Left = 12, Top = 6, Width = 300, Height = 22, Font = new Font("BIZ UDPGothic", 11F, FontStyle.Bold) });
            _attachments.SetBounds(12, 30, 380, 84);
            _attachments.CheckOnClick = true;
            _attachments.ItemCheck += (s, e) => BeginInvoke((Action)UpdateButtons);
            p.Controls.Add(_attachments);

            p.Controls.Add(new Label { Text = "取り込み先の案件", Left = 410, Top = 6, Width = 300, Height = 22, Font = new Font("BIZ UDPGothic", 11F, FontStyle.Bold) });
            _ankenBox.SetBounds(410, 30, 520, 32);
            _ankenBox.SelectedIndexChanged += (s, e) =>
            {
                if (!_loading)
                {
                    LoadSupplierBox(null);
                }
            };
            p.Controls.Add(_ankenBox);
            p.Controls.Add(new Label { Text = "調達先", Left = 410, Top = 70, Width = 80, Height = 24 });
            _supplierBox.SetBounds(490, 66, 440, 32);
            _supplierBox.SelectedIndexChanged += (s, e) => UpdateButtons();
            p.Controls.Add(_supplierBox);

            _markReceived.SetBounds(950, 30, 290, 28);
            _markReceived.Text = "「回答受領日」を入れる";
            _markReceived.Checked = true;
            p.Controls.Add(_markReceived);
            _receivedDate.SetBounds(970, 60, 160, 30);
            p.Controls.Add(_receivedDate);
            _saveMsg.SetBounds(950, 92, 290, 28);
            _saveMsg.Text = "メール（.msg）も保存する";
            _saveMsg.Checked = true;
            p.Controls.Add(_saveMsg);
            _recordNote.SetBounds(410, 98, 530, 24);
            _recordNote.Text = "メールの内容（件名・差出人・本文）を、メモに残す";
            _recordNote.Checked = true;
            _recordNote.CheckedChanged += (s, e) => UpdateButtons();
            p.Controls.Add(_recordNote);
            Controls.Add(p);

            _hint.SetBounds(16, h - 134, 800, 70);
            _hint.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            _hint.ForeColor = Color.FromArgb(90, 96, 100);
            Controls.Add(_hint);
        }

        // ---- 読み込み ----

        private void LoadMasters()
        {
            _ankens = _services.Db.ListAnkens();
            _ankenSuppliers = _services.Db.ListAllAnkenSuppliers();
            _suppliers = _services.Db.ListSuppliers().ToDictionary(s => s.Id);

            _loading = true;
            _ankenBox.Items.Clear();
            foreach (var a in _ankens)
            {
                _ankenBox.Items.Add(new AnkenItem { Anken = a });
            }

            _loading = false;
        }

        private int DaysSelected()
        {
            return new[] { 7, 14, 30, 90 }[Math.Max(0, _days.SelectedIndex)];
        }

        private void LoadMails()
        {
            var days = DaysSelected();
            try
            {
                var fromDrop = _fromDrop;
                var mails = Background.Run<IReadOnlyList<InboundMail>>(this,
                    fromDrop ? "Outlookで選んだメールを読んでいます。\r\nしばらくお待ちください。" : "Outlookの受信箱を読んでいます。\r\nしばらくお待ちください。", () =>
                {
                    using (var inbox = new OutlookInbox())
                    {
                        return fromDrop ? inbox.GetSelected(30) : inbox.ListRecent(days, 300);
                    }
                });
                if (_fromDrop && mails.Count == 0)
                {
                    MessageBox.Show(this, "Outlookで選ばれているメールが見つかりませんでした。\r\nOutlookでメールを選んでから、もう一度ドロップするか、「選んだメールを読み直す」を押してください。",
                        "ドロップしたメールを取り込む", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

                _mails = mails.ToList();
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(this, "受信箱を読み込めませんでした。\r\n\r\n" + ex.Message, "受信メールの取り込み", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            LoadMasters();
            _candidates = new Dictionary<string, InboundCandidate?>();
            foreach (var m in _mails)
            {
                _candidates[m.EntryId] = InboundMatcher.Suggest(m, _ankens, _ankenSuppliers, _suppliers.Values).FirstOrDefault();
            }

            ApplyFilter();
        }

        private void ApplyFilter()
        {
            var tokens = _search.Text.Split(new[] { ' ', '　' }, StringSplitOptions.RemoveEmptyEntries);
            var rows = _mails.Where(m =>
                (!_onlyWithAttachment.Checked || m.Attachments.Count > 0)
                && tokens.All(t => m.Subject.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0
                                   || m.SenderName.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0
                                   || m.SenderAddress.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();

            _grid.Rows.Clear();
            foreach (var m in rows)
            {
                InboundCandidate? c;
                _candidates.TryGetValue(m.EntryId, out c);
                var cand = c == null ? "—" : Home.Title(c.Anken) + (c.Supplier == null ? "" : "\r\n" + c.Supplier.ShortName);
                var idx = _grid.Rows.Add(
                    m.ReceivedAt.ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture),
                    string.IsNullOrEmpty(m.SenderName) ? m.SenderAddress : m.SenderName + "\r\n" + m.SenderAddress,
                    m.Subject,
                    m.Attachments.Count == 0 ? "—" : string.Join("\r\n", m.Attachments.Select(a => a.FileName)),
                    cand);
                _grid.Rows[idx].Tag = m;
                _grid.Rows[idx].Height = Math.Max(30, 22 * Math.Max(1, Math.Max(m.Attachments.Count, 2)));
            }

            _count.Text = rows.Count + " 通";
            if (_grid.Rows.Count == 0)
            {
                _current = null;
                _attachments.Items.Clear();
                UpdateButtons();
            }
            else
            {
                _grid.ClearSelection();
                _grid.Rows[0].Selected = true;
                OnSelect();
            }
        }

        // ---- 選択 ----

        private void OnSelect()
        {
            var row = _grid.SelectedRows.Count == 0 ? null : _grid.SelectedRows[0];
            var mail = row == null ? null : row.Tag as InboundMail;
            if (mail == null || ReferenceEquals(mail, _current))
            {
                return;
            }

            _current = mail;
            _attachments.Items.Clear();
            foreach (var a in mail.Attachments)
            {
                // PDFだけ最初からチェックを入れる（ほかのファイルは、人が選ぶ）。
                var isPdf = string.Equals(Path.GetExtension(a.FileName), ".pdf", StringComparison.OrdinalIgnoreCase);
                _attachments.Items.Add(a.FileName + "（" + FormatSize(a.Size) + "）", isPdf);
            }

            _receivedDate.Value = mail.ReceivedAt.Date;

            InboundCandidate? c;
            _candidates.TryGetValue(mail.EntryId, out c);
            long? ankenId = c != null ? c.Anken.Id : _preselectAnkenId;
            _loading = true;
            _ankenBox.SelectedIndex = -1;
            if (ankenId.HasValue)
            {
                for (var i = 0; i < _ankenBox.Items.Count; i++)
                {
                    if (((AnkenItem)_ankenBox.Items[i]).Anken.Id == ankenId.Value)
                    {
                        _ankenBox.SelectedIndex = i;
                        break;
                    }
                }
            }

            _loading = false;
            LoadSupplierBox(c != null && c.Supplier != null ? (long?)c.Supplier.SupplierId : null);
        }

        private void LoadSupplierBox(long? preselectSupplierId)
        {
            _supplierBox.Items.Clear();
            var anken = _ankenBox.SelectedItem as AnkenItem;
            if (anken != null)
            {
                IReadOnlyList<AnkenSupplier>? list;
                if (_ankenSuppliers.TryGetValue(anken.Anken.Id, out list))
                {
                    foreach (var a in list)
                    {
                        Supplier m;
                        if (_suppliers.TryGetValue(a.SupplierId, out m))
                        {
                            _supplierBox.Items.Add(new SupplierItem { Master = m, Answer = a });
                        }
                    }
                }
            }

            _supplierBox.SelectedIndex = -1;
            if (preselectSupplierId.HasValue)
            {
                for (var i = 0; i < _supplierBox.Items.Count; i++)
                {
                    if (((SupplierItem)_supplierBox.Items[i]).Master.Id == preselectSupplierId.Value)
                    {
                        _supplierBox.SelectedIndex = i;
                    }
                }
            }

            UpdateButtons();
        }

        private void UpdateButtons()
        {
            var problems = new List<string>();
            if (_current == null)
            {
                problems.Add("一覧からメールを選んでください。");
            }

            if (_ankenBox.SelectedItem == null)
            {
                problems.Add("取り込み先の案件を選んでください。");
            }
            else if (_supplierBox.Items.Count == 0)
            {
                problems.Add("この案件には、調達先が加えられていません（案件画面の「調達先を加える」で加えてください）。");
            }
            else if (_supplierBox.SelectedItem == null)
            {
                problems.Add("取り込み先の調達先を選んでください。");
            }

            if (_attachments.CheckedItems.Count == 0 && !_saveMsg.Checked && !_recordNote.Checked)
            {
                problems.Add("取り込む添付ファイルを選ぶか、「メール（.msg）も保存する」「メールの内容をメモに残す」にチェックを入れてください。");
            }

            _hint.Text = problems.Count == 0 ? "選んだ内容で取り込めます。" : string.Join("\r\n", problems);
            _import.Enabled = problems.Count == 0;
        }

        private static string FormatSize(long bytes)
        {
            if (bytes >= 1024 * 1024)
            {
                return (bytes / 1024.0 / 1024.0).ToString("0.#", CultureInfo.InvariantCulture) + " MB";
            }

            return Math.Max(1, bytes / 1024) + " KB";
        }

        // ---- 取り込み ----

        private void DoImport()
        {
            var mail = _current;
            var anken = _ankenBox.SelectedItem as AnkenItem;
            var supplier = _supplierBox.SelectedItem as SupplierItem;
            if (mail == null || anken == null || supplier == null)
            {
                return;
            }

            var indexes = new List<int>();
            for (var i = 0; i < mail.Attachments.Count; i++)
            {
                if (_attachments.GetItemChecked(i))
                {
                    indexes.Add(mail.Attachments[i].Index);
                }
            }

            var saveMsg = _saveMsg.Checked;
            var recordNote = _recordNote.Checked;
            DateTime? received = _markReceived.Checked ? (DateTime?)_receivedDate.Value.Date : null;
            var full = Path.Combine(_services.WorkspaceRoot, anken.Anken.FolderPath);

            var names = indexes.Select(i => QuoteFiles.BuildFileName(supplier.Master.ShortName, mail.Attachments.First(a => a.Index == i).FileName)).ToList();
            var confirm = new StringBuilder();
            confirm.AppendLine("次の内容で、受信メールから取り込みます。");
            confirm.AppendLine();
            confirm.AppendLine("案件: " + anken);
            confirm.AppendLine("調達先: " + supplier.Master.Name);
            confirm.AppendLine("メール: " + mail.Subject);
            confirm.AppendLine("保存先: " + Path.Combine(anken.Anken.FolderPath, FolderNames.Subfolders[4]));
            foreach (var n in names)
            {
                confirm.AppendLine("　・" + n);
            }

            if (saveMsg)
            {
                confirm.AppendLine("　・メール（.msg）→「" + InboundImporter.MailFolder + "」フォルダ");
            }

            if (recordNote)
            {
                confirm.AppendLine("　・メールの内容 → メモ（" + supplier.Master.ShortName + "のメモとして）");
            }

            confirm.AppendLine(received.HasValue ? "回答受領日: " + received.Value.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) + " を入れる" : "回答受領日: 入れない");
            var ok = MessageBox.Show(this, confirm.ToString(), "取り込みの確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (ok != DialogResult.Yes)
            {
                return;
            }

            var services = _services;
            var master = supplier.Master;
            var ankenRec = anken.Anken;
            ImportOutcome outcome;
            try
            {
                outcome = Background.Run<ImportOutcome>(this, "メールから取り込んでいます。\r\nしばらくお待ちください。", () =>
                {
                    using (var inbox = new OutlookInbox())
                    {
                        return InboundImporter.Import(inbox, services.Db, ankenRec, full, master, mail, indexes, saveMsg, received, DateTime.Now, recordNote);
                    }
                });
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(this, "取り込めませんでした。\r\n\r\n" + ex.Message, "受信メールの取り込み", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var sb = new StringBuilder();
            if (outcome.SavedFiles.Count > 0)
            {
                sb.AppendLine("保存した見積書: " + outcome.SavedFiles.Count + " 件");
                foreach (var f in outcome.SavedFiles)
                {
                    sb.AppendLine("　・" + Path.GetFileName(f));
                }
            }

            if (outcome.MsgPath != null)
            {
                sb.AppendLine("メール（.msg）を保存しました。");
            }

            if (outcome.NoteRecorded)
            {
                sb.AppendLine("メールの内容を、メモに残しました。");
            }

            if (outcome.ReceivedDateMarked)
            {
                sb.AppendLine("「回答受領日」を入れました（" + supplier.Master.ShortName + "は「回答済み」になります）。");
            }

            if (outcome.Errors.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("取り込めなかったもの:");
                foreach (var e in outcome.Errors)
                {
                    sb.AppendLine("　・" + e);
                }
            }

            MessageBox.Show(this, sb.ToString(), outcome.Errors.Count == 0 ? "取り込みました" : "取り込みの結果",
                MessageBoxButtons.OK, outcome.Errors.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);

            // 回答済みの状態が変わるので、調達先の欄と候補を読み直す。
            _ankenSuppliers = _services.Db.ListAllAnkenSuppliers();
            LoadSupplierBox(supplier.Master.Id);
        }
    }
}
