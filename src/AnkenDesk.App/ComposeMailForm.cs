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
    /// メールで見積依頼（または催促）を送る（HANDOFF.md §5.3）。調達先ごとに別々のメールにする。
    /// 送信前に確認画面を出す。Outlookがオフライン作業中のときは、送らずに知らせる。
    /// </summary>
    internal sealed class ComposeMailForm : Form
    {
        private const string NewSheetItem = "（新しく作成する: 現在の内容で）";

        private sealed class SupplierItem
        {
            public Supplier Master = new Supplier();
            public AnkenSupplier Answer = new AnkenSupplier();

            public override string ToString()
            {
                var mail = MailSender.IsValidAddressList(Master.Email) ? Master.Email : "〔メールアドレスが未登録・不正です〕";
                var sent = Answer.SentAt.HasValue ? "　依頼済み " + Answer.SentAt.Value.ToString("MM/dd", CultureInfo.InvariantCulture) : "";
                var answered = Answer.IsAnswered ? "　回答済み" : "";
                return Master.ShortName + "　" + mail + sent + answered;
            }
        }

        private readonly AppServices _services;
        private readonly AnkenRecord _anken;
        private readonly MailKind _kind;
        private readonly string _ankenFull;

        private readonly CheckedListBox _suppliers = new CheckedListBox();
        private readonly ComboBox _sheet = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly CheckedListBox _drawings = new CheckedListBox();
        private readonly CheckedListBox _extra = new CheckedListBox();
        private readonly TextBox _subject = new TextBox();
        private readonly TextBox _body = new TextBox();
        private readonly DateTimePicker _due = new DateTimePicker { Format = DateTimePickerFormat.Short };
        private readonly CheckBox _signature = new CheckBox();
        private readonly TextBox _preview = new TextBox();
        private readonly ComboBox _previewWho = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private bool _updatingWho;
        private readonly Label _problem = new Label();
        private readonly Button _send;
        private readonly Button _draft;

        private readonly Dictionary<string, string> _drawingPaths = new Dictionary<string, string>();

        public ComposeMailForm(AppServices services, AnkenRecord anken, MailKind kind, IReadOnlyCollection<long>? preselect)
        {
            _services = services;
            _anken = anken;
            _kind = kind;
            _ankenFull = Path.Combine(services.WorkspaceRoot, anken.FolderPath);

            UiStyle.Apply(this);
            var label = kind == MailKind.Request ? "見積依頼メールを作成" : "催促メールを作成";
            Text = label;
            var h = Math.Min(860, Screen.PrimaryScreen.WorkingArea.Height - 60);
            ClientSize = new Size(1240, h);

            Controls.Add(new Label
            {
                Text = label,
                Font = new Font("BIZ UDPGothic", 18F, FontStyle.Bold),
                Left = 16, Top = 10, Width = 500, Height = 40,
            });
            Controls.Add(new Label { Text = Home.Title(anken) + "　／　" + anken.ClientName, Left = 520, Top = 20, Width = 700, Height = 28 });

            BuildLeft(h);
            BuildMiddle(h);
            BuildRight(h);

            _problem.SetBounds(16, h - 112, 1208, 44);
            _problem.ForeColor = UiStyle.Danger;
            _problem.Font = new Font("BIZ UDPGothic", 11F, FontStyle.Bold);
            _problem.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;

            var cancel = UiStyle.CreateButton("キャンセル", false, 140);
            cancel.Left = 16; cancel.Top = h - 56;
            cancel.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            cancel.Click += (s, e) => DialogResult = DialogResult.Cancel;

            _draft = UiStyle.CreateButton("Outlookで下書きを開く", false, 250);
            _draft.Left = 760; _draft.Top = h - 56;
            _draft.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            _draft.Click += (s, e) => DoSend(false);

            _send = UiStyle.CreateButton("送信", true, 200);
            _send.Left = 1024; _send.Top = h - 56;
            _send.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            _send.Click += (s, e) => DoSend(true);

            Controls.AddRange(new Control[] { _problem, cancel, _draft, _send });

            LoadData(preselect);
            _suppliers.ItemCheck += (s, e) => BeginInvoke((Action)UpdatePreview);
            _subject.TextChanged += (s, e) => UpdatePreview();
            _body.TextChanged += (s, e) => UpdatePreview();
            _due.ValueChanged += (s, e) => UpdatePreview();
            UpdatePreview();
        }

        // ---- 画面の組み立て ----

        private void BuildLeft(int h)
        {
            var p = new Panel { Left = 16, Top = 60, Width = 400, Height = h - 190, BackColor = Color.White };
            p.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom;
            p.Controls.Add(Head("1　調達先を選ぶ", 12, 8));
            p.Controls.Add(new Label { Text = "調達先ごとに、別々のメールで送ります（ほかの調達先のアドレスは見えません）。", Left = 12, Top = 36, Width = 376, Height = 44 });
            _suppliers.SetBounds(12, 84, 376, 180);
            _suppliers.CheckOnClick = true;
            p.Controls.Add(_suppliers);

            p.Controls.Add(Head("添付ファイル", 12, 276));
            p.Controls.Add(new Label { Text = "見積依頼書（Excel）", Left = 12, Top = 306, Width = 376, Height = 22 });
            _sheet.SetBounds(12, 330, 376, 32);
            p.Controls.Add(_sheet);
            p.Controls.Add(new Label { Text = "図面（2.図面 のファイル）", Left = 12, Top = 372, Width = 376, Height = 22 });
            _drawings.SetBounds(12, 396, 376, 90);
            _drawings.CheckOnClick = true;
            p.Controls.Add(_drawings);
            p.Controls.Add(new Label { Text = "ほかに添付するファイル", Left = 12, Top = 494, Width = 376, Height = 22 });
            _extra.SetBounds(12, 518, 376, 56);
            _extra.CheckOnClick = true;
            p.Controls.Add(_extra);
            var add = UiStyle.CreateButton("ファイルを追加...", false, 200);
            add.Left = 12; add.Top = 582;
            add.Click += (s, e) => AddExtraFiles();
            p.Controls.Add(add);
            Controls.Add(p);
        }

        private void BuildMiddle(int h)
        {
            var p = new Panel { Left = 428, Top = 60, Width = 440, Height = h - 190, BackColor = Color.White };
            p.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom;
            p.Controls.Add(Head("2　メールの内容を確認", 12, 8));
            p.Controls.Add(new Label { Text = "件名", Left = 12, Top = 42, Width = 200, Height = 22 });
            _subject.SetBounds(12, 66, 416, 32);
            p.Controls.Add(_subject);
            p.Controls.Add(new Label { Text = "本文", Left = 12, Top = 106, Width = 200, Height = 22 });
            _body.SetBounds(12, 130, 416, h - 190 - 130 - 200);
            _body.Multiline = true;
            _body.AcceptsReturn = true;
            _body.ScrollBars = ScrollBars.Vertical;
            _body.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            p.Controls.Add(_body);

            var y = h - 190 - 190;
            var help = new Label
            {
                Text = "差し込み: " + string.Join(" ", MailTemplates.Placeholders) + "（調達先ごとに入れ替わります）",
                Left = 12, Top = y, Width = 416, Height = 44,
            };
            help.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            p.Controls.Add(help);

            var dueLabel = new Label { Text = "回答希望期日", Left = 12, Top = y + 52, Width = 130, Height = 24 };
            dueLabel.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            p.Controls.Add(dueLabel);
            _due.SetBounds(146, y + 48, 160, 32);
            _due.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            p.Controls.Add(_due);

            _signature.SetBounds(12, y + 90, 416, 30);
            _signature.Text = "Outlookの署名（テキスト版）を本文の末尾に入れる";
            _signature.Checked = true;
            _signature.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            p.Controls.Add(_signature);

            var save = UiStyle.CreateButton("この件名・本文を既定にする", false, 300);
            save.Left = 12; save.Top = y + 124;
            save.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            save.Click += (s, e) =>
            {
                _services.Db.SetMailTemplate(_kind, _subject.Text, _body.Text.Replace("\r\n", "\n"));
                MessageBox.Show(this, "次回から、この件名・本文を最初に出します。", "既定にしました", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            p.Controls.Add(save);
            Controls.Add(p);
        }

        private void BuildRight(int h)
        {
            var p = new Panel { Left = 880, Top = 60, Width = 344, Height = h - 190, BackColor = Color.White };
            p.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            p.Controls.Add(Head("送るメールの見え方", 12, 8));
            _previewWho.SetBounds(12, 38, 320, 28);
            _previewWho.SelectedIndexChanged += (s, e) =>
            {
                if (!_updatingWho)
                {
                    UpdatePreview();
                }
            };
            p.Controls.Add(_previewWho);
            _preview.SetBounds(12, 74, 320, h - 190 - 86);
            _preview.Multiline = true;
            _preview.ReadOnly = true;
            _preview.ScrollBars = ScrollBars.Vertical;
            _preview.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            p.Controls.Add(_preview);
            Controls.Add(p);
        }

        private static Label Head(string text, int x, int y)
        {
            return new Label { Text = text, Left = x, Top = y, Width = 380, Height = 28, Font = new Font("BIZ UDPGothic", 12F, FontStyle.Bold) };
        }

        // ---- データの読み込み ----

        private void LoadData(IReadOnlyCollection<long>? preselect)
        {
            var master = _services.Db.ListSuppliers().ToDictionary(s => s.Id);
            foreach (var a in _services.Db.ListAnkenSuppliers(_anken.Id))
            {
                Supplier m;
                if (!master.TryGetValue(a.SupplierId, out m))
                {
                    continue;
                }

                var item = new SupplierItem { Master = m, Answer = a };
                var check = preselect != null
                    ? preselect.Contains(a.SupplierId)
                    : (_kind == MailKind.Request ? !a.SentAt.HasValue : !a.IsAnswered);
                _suppliers.Items.Add(item, check);
            }

            string subject, body;
            _services.Db.GetMailTemplate(_kind, out subject, out body);
            _subject.Text = subject;
            _body.Text = body.Replace("\r\n", "\n").Replace("\n", "\r\n");
            _due.Value = _anken.ReplyDueDate;

            // 見積依頼書: 今ある「見積依頼」のExcelも選べる。初期値は、現在の内容で新しく作る。
            _sheet.Items.Add(NewSheetItem);
            var requestDir = Path.Combine(_ankenFull, FolderNames.Subfolders[3]);
            if (Directory.Exists(requestDir))
            {
                foreach (var f in Directory.GetFiles(requestDir, "*見積依頼*.xlsx").OrderByDescending(x => x, StringComparer.Ordinal))
                {
                    _sheet.Items.Add(Path.GetFileName(f));
                }
            }

            _sheet.SelectedIndex = 0;
            _sheet.Enabled = _kind == MailKind.Request;

            var drawingDir = Path.Combine(_ankenFull, FolderNames.Subfolders[1]);
            if (Directory.Exists(drawingDir))
            {
                foreach (var f in Directory.GetFiles(drawingDir).OrderBy(x => x, StringComparer.Ordinal))
                {
                    var name = Path.GetFileName(f);
                    _drawingPaths[name] = f;
                    _drawings.Items.Add(name, _kind == MailKind.Request);
                }
            }
        }

        private void AddExtraFiles()
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "添付するファイルを選んでください";
                dlg.Multiselect = true;
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    foreach (var f in dlg.FileNames)
                    {
                        _extra.Items.Add(f, true);
                    }
                }
            }
        }

        // ---- 状態の更新 ----

        private List<SupplierItem> Selected()
        {
            return _suppliers.CheckedItems.Cast<SupplierItem>().ToList();
        }

        private void UpdatePreview()
        {
            var selected = Selected();

            // 見え方を見る調達先を選べるようにする（選んだ調達先が変わったときだけ、一覧を作り直す）。
            var names = selected.Select(x => x.Master.Name).ToList();
            if (!names.SequenceEqual(_previewWho.Items.Cast<string>()))
            {
                _updatingWho = true;
                var keep = _previewWho.SelectedItem as string;
                _previewWho.Items.Clear();
                foreach (var n in names)
                {
                    _previewWho.Items.Add(n);
                }

                var at = keep == null ? -1 : names.IndexOf(keep);
                _previewWho.SelectedIndex = names.Count == 0 ? -1 : Math.Max(0, at);
                _updatingWho = false;
            }

            if (selected.Count == 0)
            {
                _preview.Text = "";
            }
            else
            {
                var first = selected[Math.Max(0, _previewWho.SelectedIndex)].Master;
                var sb = new StringBuilder();
                sb.AppendLine("宛先: " + first.Email);
                sb.AppendLine("件名: " + MailTemplates.Apply(_subject.Text, first, _anken, _due.Value.Date));
                sb.AppendLine();
                sb.AppendLine(MailTemplates.Apply(_body.Text, first, _anken, _due.Value.Date).Replace("\n", "\r\n"));
                _preview.Text = sb.ToString();
            }

            var problems = new List<string>();
            if (selected.Count == 0)
            {
                problems.Add("送る調達先を選んでください。");
            }

            var bad = selected.Where(s => !MailSender.IsValidAddressList(s.Master.Email)).Select(s => s.Master.ShortName).ToList();
            if (bad.Count > 0)
            {
                problems.Add("メールアドレスが未登録・不正な調達先があります: " + string.Join("、", bad) + "（調達先マスターで直してください）");
            }

            _problem.Text = problems.Count == 0 ? "" : "警告: " + string.Join("　", problems);
            var ok = problems.Count == 0;
            _send.Enabled = ok;
            _draft.Enabled = ok;
            _send.Text = selected.Count == 0 ? "送信" : selected.Count + "通を送信";
        }

        // ---- 送信 ----

        private void DoSend(bool send)
        {
            var selected = Selected();
            if (selected.Count == 0)
            {
                return;
            }

            // 見積依頼書は、まだ作らずに、選択だけ覚えておく（確認のあとで作る）。
            var newSheet = _kind == MailKind.Request && _sheet.SelectedIndex == 0;
            var existingSheet = _kind == MailKind.Request && _sheet.SelectedIndex > 0
                ? Path.Combine(_ankenFull, FolderNames.Subfolders[3], (string)_sheet.SelectedItem)
                : null;
            var others = new List<string>();
            foreach (var name in _drawings.CheckedItems.Cast<string>())
            {
                others.Add(_drawingPaths[name]);
            }

            others.AddRange(_extra.CheckedItems.Cast<string>());
            var missing = others.Where(f => !File.Exists(f)).ToList();
            if (missing.Count > 0)
            {
                MessageBox.Show(this, "見つからない添付ファイルがあります。\r\n\r\n" + string.Join("\r\n", missing), "添付ファイル", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var verb = send ? "送信" : "下書きを開き";
            var attachNames = new List<string>();
            if (newSheet) { attachNames.Add("見積依頼書（現在の内容で新しく作成）"); }
            if (existingSheet != null) { attachNames.Add(Path.GetFileName(existingSheet)); }
            attachNames.AddRange(others.Select(Path.GetFileName));

            var confirm = new StringBuilder();
            confirm.AppendLine(selected.Count + "通を、別々のメールで" + verb + "ます。" + (send ? "送信後は取り消せません。" : ""));
            confirm.AppendLine();
            foreach (var s in selected)
            {
                confirm.AppendLine("・" + s.Master.Name + " <" + s.Master.Email + ">");
            }

            confirm.AppendLine();
            confirm.AppendLine("件名: " + MailTemplates.Apply(_subject.Text, selected[0].Master, _anken, _due.Value.Date));
            confirm.AppendLine("添付: " + (attachNames.Count == 0 ? "なし" : string.Join("、", attachNames)));
            confirm.AppendLine("署名: " + (_signature.Checked ? "入れる" : "入れない"));
            var ok = MessageBox.Show(this, confirm.ToString(), send ? "送信の確認" : "下書きの確認",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (ok != DialogResult.Yes)
            {
                return;
            }

            // 見積依頼書を作る（確認のあと）。
            var attachments = new List<string>();
            try
            {
                if (newSheet)
                {
                    attachments.Add(QuoteRequestSheet.SaveToAnkenFolder(_ankenFull, _services.Db.LoadAnkenInput(_anken.Id), DateTime.Today));
                }
                else if (existingSheet != null)
                {
                    attachments.Add(existingSheet);
                }
            }
            catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
            {
                MessageBox.Show(this, "見積依頼書を作れませんでした。メールは送っていません。\r\n\r\n" + ex.Message, "見積依頼書", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            attachments.AddRange(others);

            var items = selected
                .Select(s => new KeyValuePair<Supplier, MailRequest>(s.Master,
                    MailSender.BuildRequest(_anken, _due.Value.Date, s.Master, _subject.Text, _body.Text, attachments, _signature.Checked)))
                .ToList();

            try
            {
                if (send)
                {
                    Send(items);
                }
                else
                {
                    OpenDrafts(items);
                }
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(this, "Outlookの操作でエラーになりました。\r\n\r\n" + ex.Message, "メール", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void Send(List<KeyValuePair<Supplier, MailRequest>> items)
        {
            var services = _services;
            var anken = _anken;
            var kind = _kind;
            var ankenFull = _ankenFull;
            var now = DateTime.Now;

            // COMのオブジェクトは、別スレッドの中で作って、その中で使い切る。
            var outcomes = Background.Run<IReadOnlyList<MailSendOutcome>?>(this, "Outlookでメールを送っています。\r\nしばらくお待ちください（最大で1通につき1分ほど）。", () =>
            {
                using (var gateway = new OutlookMailGateway())
                {
                    if (gateway.IsOffline())
                    {
                        return null;
                    }

                    return MailSender.SendAll(gateway, services.Db, anken, ankenFull, kind, items, now);
                }
            });

            if (outcomes == null)
            {
                MessageBox.Show(this,
                    "Outlookが「オフライン作業中」です。メールは送っていません。\r\n\r\n"
                    + "Outlookの「送受信」タブの「オフライン作業」を押して、オンラインに戻してから、もう一度送信してください。",
                    "オフライン作業中", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var sb = new StringBuilder();
            foreach (var o in outcomes)
            {
                sb.AppendLine(o.Supplier.ShortName + ": " + MailSender.StatusText(o.Result.Outcome) + (o.Result.MsgSaved ? "（.msgを保存しました）" : "")
                    + (o.Result.Outcome == MailOutcome.Sent ? "" : "　" + o.Result.Message));
            }

            var notSent = items.Count - outcomes.Count(o => o.Result.Outcome == MailOutcome.Sent);
            if (notSent > 0)
            {
                sb.AppendLine();
                sb.AppendLine("送信済みになれなかったメールがあるため、残りは送っていません。状況を確認してから、もう一度送ってください。");
                MessageBox.Show(this, sb.ToString(), "送信の結果", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.OK;
                return;
            }

            MessageBox.Show(this, sb.ToString(), "送信しました", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
        }

        private void OpenDrafts(List<KeyValuePair<Supplier, MailRequest>> items)
        {
            Background.Run<bool>(this, "Outlookで下書きを開いています。", () =>
            {
                // 下書きの画面は、人が閉じるまで残す。このインスタンスは、COM参照だけ解放して終わる。
                using (var gateway = new OutlookMailGateway())
                {
                    foreach (var item in items)
                    {
                        gateway.OpenDraft(item.Value);
                    }
                }

                return true;
            });

            MessageBox.Show(this, items.Count + "通の下書きをOutlookで開きました。内容を確認して、Outlookで送信してください。\r\n（この画面からの送信ではないので、メールの記録と依頼送付日は入りません）",
                "下書きを開きました", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
        }
    }
}
