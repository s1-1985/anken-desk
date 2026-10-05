using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using AnkenDesk.Core;

namespace AnkenDesk.App
{
    /// <summary>案件画面（HANDOFF.md §5.2）。調達先ごとの回答を並べて比べる。段2aでは、数値は手で入力する。</summary>
    internal sealed class AnkenDetailForm : Form
    {
        private static readonly Color CheapestBack = ColorTranslator.FromHtml("#D7E8EF");

        private readonly AppServices _services;
        private readonly long _ankenId;

        private readonly Label _title = new Label();
        private readonly Label _hint = new Label();
        private readonly Label _status = new Label();
        private readonly Label _client = new Label();
        private readonly Label _requestDate = new Label();
        private readonly Label _dueDate = new Label();
        private readonly Label _progress = new Label();
        private readonly ListBox _folders = new ListBox();
        private readonly DataGridView _grid = new DataGridView();
        private readonly ListBox _history = new ListBox();

        private AnkenRecord _anken = null!;
        private IReadOnlyList<AnkenSupplier> _suppliers = new List<AnkenSupplier>();

        public AnkenDetailForm(AppServices services, long ankenId)
        {
            _services = services;
            _ankenId = ankenId;
            UiStyle.Apply(this);
            var height = Math.Min(940, Screen.PrimaryScreen.WorkingArea.Height - 60);
            ClientSize = new Size(1300, height);
            StartPosition = FormStartPosition.CenterParent;

            _title.SetBounds(16, 10, 660, 44);
            _title.AutoEllipsis = true;
            _title.Font = new Font("BIZ UDPGothic", 18F, FontStyle.Bold);

            var open = UiStyle.CreateButton("エクスプローラーで開く", false, 230);
            open.Left = 1054; open.Top = 10;
            open.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            open.Click += (s, e) => OpenFolder();
            var makeRequest = UiStyle.CreateButton("見積依頼書を作成", true, 190);
            makeRequest.Left = 856; makeRequest.Top = 10;
            makeRequest.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            makeRequest.Click += (s, e) => MakeRequestSheet();
            var editAnken = UiStyle.CreateButton("案件の項目を編集", false, 170);
            editAnken.Left = 678; editAnken.Top = 10;
            editAnken.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            editAnken.Click += (s, e) => EditAnken();

            // 操作ボタン（1段目: 調達先と回答、2段目: 見積書）
            var add = ToolButton("調達先を加える", true, 16, 60, 150);
            add.Click += (s, e) => AddSuppliers();
            var input = ToolButton("回答を入力・変更", true, 174, 60, 170);
            input.Click += (s, e) => EditAnswer();
            var requote = ToolButton("出し直しを受け取る", true, 352, 60, 200);
            requote.Click += (s, e) => Requote();
            var history = ToolButton("履歴を見る", false, 560, 60, 130);
            history.Click += (s, e) => ShowHistory();
            var remove = ToolButton("調達先を外す", false, 698, 60, 150);
            remove.Click += (s, e) => RemoveSupplier();
            var saveFile = ToolButton("見積書を保存...", false, 856, 60, 160);
            saveFile.Click += (s, e) => SaveQuoteFilesWithDialog();
            var preview = ToolButton("見積書を見る", false, 1024, 60, 150);
            preview.Click += (s, e) => PreviewQuotes();

            var mailRequest = ToolButton("見積依頼メールを作成", true, 16, 108, 230);
            mailRequest.Click += (s, e) => OpenMail(MailKind.Request);
            var mailReminder = ToolButton("催促メールを作成", true, 254, 108, 200);
            mailReminder.Click += (s, e) => OpenMail(MailKind.Reminder);

            _hint.SetBounds(16, 156, 1268, 24);
            _hint.ForeColor = Color.FromArgb(90, 96, 100);
            _hint.Text = "表の調達先の列にファイル（見積書のPDFなど）をドロップすると、「5.調達先見積もり」にコピーして保存します。";

            var band = new Panel { Left = 16, Top = 184, Width = 1268, Height = 80, BackColor = Color.White };
            Place(band, "得意先・種別", _client, 0, 330);
            Place(band, "依頼日", _requestDate, 330, 180);
            Place(band, "回答期限", _dueDate, 510, 300);
            Place(band, "回答状況", _progress, 810, 450);

            var bodyTop = 276;
            var historyHeight = 100;
            var bodyHeight = height - bodyTop - historyHeight - 64;

            var folderPanel = new Panel { Left = 16, Top = bodyTop, Width = 300, Height = bodyHeight, BackColor = Color.White };
            folderPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom;
            folderPanel.Controls.Add(new Label { Text = "フォルダ（ファイル数）", Left = 12, Top = 10, Width = 276, Height = 28, Font = new Font("BIZ UDPGothic", 12F, FontStyle.Bold) });
            _folders.SetBounds(8, 44, 284, bodyHeight - 52);
            _folders.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            _folders.IntegralHeight = false;
            _folders.SelectionMode = SelectionMode.None;
            _folders.TabStop = false;
            folderPanel.Controls.Add(_folders);

            _grid.SetBounds(328, bodyTop, 956, bodyHeight);
            _grid.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            _grid.ReadOnly = true;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.AllowUserToResizeRows = false;
            _grid.RowHeadersVisible = false;
            _grid.BackgroundColor = Color.White;
            _grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            _grid.MultiSelect = false;
            _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            _grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            _grid.DefaultCellStyle.Padding = new Padding(4);
            _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            _grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
            _grid.CellDoubleClick += (s, e) =>
            {
                if (e.ColumnIndex <= 0 || e.RowIndex < 0)
                {
                    return;
                }

                // 見積書PDFの行はプレビュー、ほかの行は回答の入力。
                var label = Convert.ToString(_grid.Rows[e.RowIndex].Cells[0].Value, CultureInfo.InvariantCulture) ?? "";
                if (label.StartsWith("見積書PDF", StringComparison.Ordinal))
                {
                    PreviewQuotes();
                }
                else
                {
                    EditAnswer();
                }
            };

            // ファイルのドロップ（調達先の列に落とすと、その調達先の見積書として保存する）
            _grid.AllowDrop = true;
            _grid.DragEnter += (s, e) =>
            {
                e.Effect = e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            };
            _grid.DragDrop += (s, e) => OnDropFiles(e);

            var historyTitle = new Label { Text = "経過", Left = 16, Top = height - historyHeight - 38, Width = 120, Height = 26, Font = new Font("BIZ UDPGothic", 12F, FontStyle.Bold) };
            historyTitle.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            _status.SetBounds(140, height - historyHeight - 38, 1140, 26);
            _status.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            _history.SetBounds(16, height - historyHeight - 8, 1268, historyHeight);
            _history.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            _history.IntegralHeight = false;
            _history.SelectionMode = SelectionMode.None;
            _history.TabStop = false;

            Controls.AddRange(new Control[] { _title, editAnken, makeRequest, open, mailRequest, mailReminder, add, input, requote, history, remove, saveFile, preview, _hint, band, folderPanel, _grid, historyTitle, _status, _history });

            Activated += (s, e) => Reload();
            Reload();
        }

        private static Button ToolButton(string text, bool primary, int x, int y, int width)
        {
            var b = UiStyle.CreateButton(text, primary, width);
            b.Left = x;
            b.Top = y;
            b.Margin = new Padding(0);
            return b;
        }

        private static void Place(Panel band, string caption, Label value, int x, int width)
        {
            band.Controls.Add(new Label { Text = caption, Left = x + 12, Top = 8, Width = width - 12, Height = 22, ForeColor = Color.FromArgb(90, 96, 100) });
            value.SetBounds(x + 12, 34, width - 12, 36);
            value.Font = new Font("BIZ UDPGothic", 12F, FontStyle.Bold);
            band.Controls.Add(value);
        }

        // ---- 表示 ----

        private void Reload()
        {
            var a = _services.Db.GetAnken(_ankenId);
            if (a == null)
            {
                MessageBox.Show(this, "案件が見つかりません。", "案件画面", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Close();
                return;
            }

            _anken = a;
            _suppliers = _services.Db.ListAnkenSuppliers(_ankenId);
            var patterns = _services.Db.ListQuantities(_ankenId);
            var quotes = _services.Db.ListQuotes(_ankenId);

            var folderName = Path.GetFileName(a.FolderPath);
            Text = "案件: " + folderName;
            _title.Text = folderName;
            _client.Text = a.ClientName;
            _requestDate.Text = a.RequestDate.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);

            int answered, total;
            Comparison.Progress(_suppliers.ToList(), out answered, out total);
            var due = a.ReplyDueDate.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
            _dueDate.Text = (total > 0 && answered == total)
                ? due + "（全社回答済み）"
                : due + "（" + Comparison.DueStatus(a.ReplyDueDate, DateTime.Today) + "）";
            if (total == 0)
            {
                _progress.Text = "調達先が未登録です";
            }
            else
            {
                var pending = _suppliers.Where(s => !s.IsAnswered).Select(s => s.ShortName).ToList();
                _progress.Text = answered + "/" + total + "社" + (pending.Count > 0 ? "（未回答: " + string.Join("、", pending) + "）" : "");
            }

            ReloadFolders();
            BuildGrid(patterns, quotes);
            BuildHistory();
        }

        private void ReloadFolders()
        {
            _folders.Items.Clear();
            foreach (var e in FolderInspector.Scan(Path.Combine(_services.WorkspaceRoot, _anken.FolderPath)))
            {
                var label = (e.IsOldVersion ? "　　└ " : "") + e.Name + (e.Exists ? "　" + e.FileCount : "　（フォルダなし）");
                _folders.Items.Add(label);
            }
        }

        private void BuildGrid(IReadOnlyList<QuantityPattern> patterns, IReadOnlyList<Quote> quotes)
        {
            _grid.Columns.Clear();
            _grid.Rows.Clear();

            var head = _grid.Columns[_grid.Columns.Add("item", "項目")];
            head.Width = 170;
            head.SortMode = DataGridViewColumnSortMode.NotSortable;
            foreach (var s in _suppliers)
            {
                var col = _grid.Columns[_grid.Columns.Add("s" + s.SupplierId, s.ShortName + "\r\n" + (s.IsAnswered ? "回答済み" : "未回答"))];
                col.Width = 200;
                col.Tag = s;
                col.SortMode = DataGridViewColumnSortMode.NotSortable;
            }

            var cheapest = new HashSet<string>(Comparison.CheapestQuotes(quotes).Select(q => Key(q.PatternId, q.SupplierId)));

            foreach (var p in patterns)
            {
                var cells = new List<object> { p.Kind + "\r\n" + p.Quantity.ToString("#,##0.####", CultureInfo.InvariantCulture) + p.Unit };
                var styles = new List<bool> { false };
                foreach (var s in _suppliers)
                {
                    var q = quotes.FirstOrDefault(x => x.PatternId == p.Id && x.SupplierId == s.SupplierId);
                    var isCheapest = q != null && cheapest.Contains(Key(p.Id, s.SupplierId));
                    cells.Add(QuoteText(q, s, isCheapest));
                    styles.Add(isCheapest);
                }

                AddRow(cells, styles);
            }

            AddTextRow("別費用（型・治具）", s => s.ExtraCost);
            AddTextRow("緩和条件", s => s.Relaxation);
            AddTextRow("備考", s => s.Note);
            AddTextRow("依頼送付日", s => s.SentAt.HasValue ? s.SentAt.Value.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) : "");
            AddTextRow("回答受領日", s => s.ReceivedAt.HasValue ? s.ReceivedAt.Value.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) : "");
            AddTextRow("見積書PDF（5番のフォルダ内）", s => QuotePdfNames(s));
        }

        private static string Key(long patternId, long supplierId)
        {
            return patternId + ":" + supplierId;
        }

        private static string QuoteText(Quote? q, AnkenSupplier s, bool isCheapest)
        {
            if (q == null)
            {
                return s.IsAnswered ? "—" : "—\r\n（未回答）";
            }

            var lines = new List<string>();
            if (isCheapest)
            {
                lines.Add("【最安】");
            }

            lines.Add(q.UnitPrice.HasValue ? Comparison.FormatPrice(q.UnitPrice.Value) : "単価なし");
            lines.Add(q.LeadTimeDays.HasValue ? "LT " + q.LeadTimeDays.Value + "日" : "LTなし");
            return string.Join("\r\n", lines);
        }

        private void AddTextRow(string label, Func<AnkenSupplier, string> get)
        {
            var cells = new List<object> { label };
            var styles = new List<bool> { false };
            foreach (var s in _suppliers)
            {
                var v = get(s);
                cells.Add(string.IsNullOrWhiteSpace(v) ? "—" : v);
                styles.Add(false);
            }

            AddRow(cells, styles);
        }

        private void AddRow(List<object> cells, List<bool> cheapest)
        {
            var idx = _grid.Rows.Add(cells.ToArray());
            var row = _grid.Rows[idx];
            row.Cells[0].Style.Font = new Font("BIZ UDPGothic", 11F, FontStyle.Bold);
            row.Cells[0].Style.BackColor = Color.FromArgb(244, 245, 243);
            for (var i = 0; i < cheapest.Count; i++)
            {
                if (cheapest[i])
                {
                    row.Cells[i].Style.BackColor = CheapestBack;
                    row.Cells[i].Style.Font = new Font("BIZ UDPGothic", 11F, FontStyle.Bold);
                }
            }
        }

        // 5.調達先見積もり の中で、「略称　」で始まるファイルの名前。旧版フォルダにあるものは、件数だけ添える。
        private string QuotePdfNames(AnkenSupplier s)
        {
            var full = Path.Combine(_services.WorkspaceRoot, _anken.FolderPath);
            var names = new List<string>(QuoteFiles.ListCurrent(full, s.ShortName));
            var old = QuoteFiles.ListOld(full, s.ShortName).Count;
            if (old > 0)
            {
                names.Add("（旧版 " + old + " 件）");
            }

            return string.Join("\r\n", names);
        }

        private void BuildHistory()
        {
            var events = new List<KeyValuePair<DateTime, string>>();
            events.Add(new KeyValuePair<DateTime, string>(_anken.RequestDate, "案件の依頼日"));
            foreach (var s in _suppliers)
            {
                if (s.SentAt.HasValue)
                {
                    events.Add(new KeyValuePair<DateTime, string>(s.SentAt.Value, "依頼を送付（" + s.ShortName + "）"));
                }

                if (s.ReceivedAt.HasValue)
                {
                    events.Add(new KeyValuePair<DateTime, string>(s.ReceivedAt.Value, "回答を受領（" + s.ShortName + "）"));
                }
            }

            // メールの記録（送ったもの・送信トレイに残ったもの・失敗したもの）。
            var supplierNames = _suppliers.ToDictionary(x => x.SupplierId, x => x.ShortName);
            foreach (var m in _services.Db.ListMailLog(_ankenId))
            {
                string name;
                var who = m.SupplierId.HasValue && supplierNames.TryGetValue(m.SupplierId.Value, out name) ? name : m.ToAddress;
                events.Add(new KeyValuePair<DateTime, string>(m.CreatedAt,
                    MailSender.KindText(m.Kind) + "メール（" + who + "）　" + m.Status + (m.MsgPath != null ? "　.msg保存済み" : "")));
            }

            _history.Items.Clear();
            foreach (var ev in events.OrderBy(x => x.Key))
            {
                var format = ev.Key.TimeOfDay == TimeSpan.Zero ? "yyyy/MM/dd" : "yyyy/MM/dd HH:mm";
                _history.Items.Add(ev.Key.ToString(format, CultureInfo.InvariantCulture) + "　" + ev.Value);
            }

            var pending = _suppliers.Where(s => !s.IsAnswered && s.SentAt.HasValue).ToList();
            foreach (var s in pending)
            {
                _history.Items.Add("　　　　　　　未回答（" + s.ShortName + "）");
            }
        }

        // ---- 操作 ----

        private AnkenSupplier? SelectedSupplier()
        {
            var cell = _grid.CurrentCell;
            if (cell == null || cell.ColumnIndex <= 0)
            {
                MessageBox.Show(this, "表の、調達先の列のマスを選んでください。", "案件画面", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return null;
            }

            return _grid.Columns[cell.ColumnIndex].Tag as AnkenSupplier;
        }

        private void AddSuppliers()
        {
            using (var dlg = new SupplierPickForm(_services, _suppliers.Select(s => s.SupplierId)))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                foreach (var s in dlg.Chosen)
                {
                    _services.Db.AddSupplierToAnken(_ankenId, s.Id);
                }
            }

            Reload();
        }

        private void EditAnswer()
        {
            var s = SelectedSupplier();
            if (s == null)
            {
                return;
            }

            var patterns = _services.Db.ListQuantities(_ankenId);
            if (patterns.Count == 0)
            {
                MessageBox.Show(this, "この案件には、見積依頼数量が登録されていません。", "案件画面", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            using (var dlg = new QuoteEditForm(_services, s, patterns, _services.Db.ListQuotes(_ankenId)))
            {
                dlg.ShowDialog(this);
            }

            Reload();
        }

        private void RemoveSupplier()
        {
            var s = SelectedSupplier();
            if (s == null)
            {
                return;
            }

            var ok = MessageBox.Show(this,
                "「" + s.SupplierName + "」をこの案件から外します。\r\n入力した単価・日付・備考と、旧版の履歴も消えます。\r\n（保存したPDFなどのファイルは消えません）",
                "調達先を外す", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (ok != DialogResult.Yes)
            {
                return;
            }

            _services.Db.RemoveSupplierFromAnken(_ankenId, s.SupplierId);
            Reload();
        }

        private string AnkenFullPath()
        {
            return Path.Combine(_services.WorkspaceRoot, _anken.FolderPath);
        }

        private void SetStatus(string text)
        {
            _status.Text = DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture) + "　" + text;
        }

        // 見積書をコピーして保存する。元のファイルは動かさない。
        private void SaveQuoteFiles(AnkenSupplier s, IEnumerable<string> paths)
        {
            var saved = new List<string>();
            var failed = new List<string>();
            foreach (var path in paths)
            {
                if (!File.Exists(path))
                {
                    failed.Add(Path.GetFileName(path) + "（ファイルではありません）");
                    continue;
                }

                try
                {
                    saved.Add(Path.GetFileName(QuoteFiles.Save(AnkenFullPath(), s.ShortName, path)));
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
                {
                    failed.Add(Path.GetFileName(path) + "（" + ex.Message + "）");
                }
            }

            if (saved.Count > 0)
            {
                SetStatus(s.ShortName + " の見積書を保存しました: " + string.Join("、", saved));
            }

            if (failed.Count > 0)
            {
                MessageBox.Show(this, "保存できなかったファイルがあります。\r\n\r\n" + string.Join("\r\n", failed), "見積書の保存", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            Reload();
        }

        private void OnDropFiles(DragEventArgs e)
        {
            var paths = e.Data == null ? null : e.Data.GetData(DataFormats.FileDrop) as string[];
            if (paths == null || paths.Length == 0)
            {
                return;
            }

            var pt = _grid.PointToClient(new Point(e.X, e.Y));
            var hit = _grid.HitTest(pt.X, pt.Y);
            var supplier = hit.ColumnIndex > 0 ? _grid.Columns[hit.ColumnIndex].Tag as AnkenSupplier : null;
            if (supplier == null)
            {
                MessageBox.Show(this, "表の、調達先の列にドロップしてください。", "見積書の保存", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            SaveQuoteFiles(supplier, paths);
        }

        private void SaveQuoteFilesWithDialog()
        {
            var s = SelectedSupplier();
            if (s == null)
            {
                return;
            }

            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = s.SupplierName + " の見積書を選んでください";
                dlg.Filter = "見積書（PDF・Excelなど）|*.*";
                dlg.Multiselect = true;
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    SaveQuoteFiles(s, dlg.FileNames);
                }
            }
        }

        private void PreviewQuotes()
        {
            var s = SelectedSupplier();
            if (s == null)
            {
                return;
            }

            var full = AnkenFullPath();
            var items = new List<KeyValuePair<string, string>>();
            foreach (var n in QuoteFiles.ListCurrent(full, s.ShortName))
            {
                items.Add(new KeyValuePair<string, string>(n, Path.Combine(QuoteFiles.QuoteDir(full), n)));
            }

            foreach (var n in QuoteFiles.ListOld(full, s.ShortName))
            {
                items.Add(new KeyValuePair<string, string>("【旧版】" + n, Path.Combine(QuoteFiles.OldDir(full), n)));
            }

            if (items.Count == 0)
            {
                MessageBox.Show(this, s.SupplierName + " の見積書は、まだ保存されていません。\r\n表の列にファイルをドロップするか、「見積書を保存...」で保存してください。",
                    "見積書を見る", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var dlg = new PdfPreviewForm(items))
            {
                dlg.ShowDialog(this);
            }
        }

        // 出し直し: 旧版を残す／残さないを選び、今ある見積書を旧版フォルダへ移して、新しい回答に差し替える。
        private void Requote()
        {
            var s = SelectedSupplier();
            if (s == null)
            {
                return;
            }

            var patterns = _services.Db.ListQuantities(_ankenId);
            var full = AnkenFullPath();
            var current = QuoteFiles.ListCurrent(full, s.ShortName);

            using (var dlg = new QuoteEditForm(_services, s, patterns, _services.Db.ListQuotes(_ankenId), true, current))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                var move = dlg.MoveOldFiles && current.Count > 0;
                var text = "出し直しの回答に差し替えます。\r\n\r\n"
                    + "・旧版を履歴として残す: " + (dlg.KeepHistory ? "残す" : "残さない（旧版の単価・日付・備考は消えます）") + "\r\n"
                    + "・旧版フォルダへ移す見積書: " + (move ? string.Join("、", current) : "なし") + "\r\n"
                    + "・新しい見積書の保存: " + (dlg.NewFilePath == null ? "なし" : Path.GetFileName(dlg.NewFilePath));
                var ok = MessageBox.Show(this, text, "出し直しの確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
                if (ok != DialogResult.Yes)
                {
                    return;
                }

                IReadOnlyList<KeyValuePair<string, string>> moves = new List<KeyValuePair<string, string>>();
                try
                {
                    if (move)
                    {
                        moves = QuoteFiles.MoveToOld(full, current);
                    }

                    var archived = string.Join("\n", moves.Select(m => Path.GetFileName(m.Value)));
                    _services.Db.SaveNewVersion(dlg.Answer, dlg.ResultQuotes, dlg.KeepHistory, archived);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
                {
                    QuoteFiles.Undo(moves);
                    MessageBox.Show(this, "出し直しを保存できませんでした。移した見積書は元に戻しました。\r\n\r\n" + ex.Message, "出し直し", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Reload();
                    return;
                }

                SetStatus(s.ShortName + " の出し直しを保存しました" + (moves.Count > 0 ? "（旧版の見積書 " + moves.Count + " 件を移しました）" : ""));
                if (dlg.NewFilePath != null)
                {
                    SaveQuoteFiles(s, new[] { dlg.NewFilePath });
                }
            }

            Reload();
        }

        private void ShowHistory()
        {
            var s = SelectedSupplier();
            if (s == null)
            {
                return;
            }

            using (var dlg = new AnswerHistoryForm(s.SupplierName, _services.Db.ListAnswerHistory(_ankenId, s.SupplierId), _services.Db.ListQuantities(_ankenId)))
            {
                dlg.ShowDialog(this);
            }
        }

        // メールで見積依頼／催促を送る。催促は、表で選んだ調達先があればその調達先、無ければ未回答の調達先を初期の選択にする。
        private void OpenMail(MailKind kind)
        {
            if (_suppliers.Count == 0)
            {
                MessageBox.Show(this, "この案件に、調達先が加えられていません。先に「調達先を加える」で加えてください。", "メール", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            IReadOnlyCollection<long>? preselect = null;
            var cell = _grid.CurrentCell;
            if (cell != null && cell.ColumnIndex > 0)
            {
                var chosen = _grid.Columns[cell.ColumnIndex].Tag as AnkenSupplier;
                if (chosen != null)
                {
                    preselect = new[] { chosen.SupplierId };
                }
            }

            using (var dlg = new ComposeMailForm(_services, _anken, kind, preselect))
            {
                dlg.ShowDialog(this);
            }

            Reload();
        }

        private void EditAnken()
        {
            using (var dlg = new AnkenEditForm(_services, _ankenId))
            {
                dlg.ShowDialog(this);
            }

            Reload();
        }

        // 見積依頼書（Excel）を、「4.調達先への見積依頼内容」に作る。作ったあと、Excelで開くかを聞く。
        private void MakeRequestSheet()
        {
            string path;
            try
            {
                path = QuoteRequestSheet.SaveToAnkenFolder(AnkenFullPath(), _services.Db.LoadAnkenInput(_ankenId), DateTime.Today);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
            {
                MessageBox.Show(this, "見積依頼書を作れませんでした。\r\n\r\n" + ex.Message, "見積依頼書を作成", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SetStatus("見積依頼書を作成しました: " + Path.GetFileName(path));
            Reload();
            var open = MessageBox.Show(this, "見積依頼書を作成しました。\r\n\r\n" + path + "\r\n\r\nExcelで開きますか？",
                "見積依頼書を作成", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (open == DialogResult.Yes)
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
        }

        private void OpenFolder()
        {
            var full = Path.Combine(_services.WorkspaceRoot, _anken.FolderPath);
            if (!Directory.Exists(full))
            {
                MessageBox.Show(this, "フォルダが見つかりません。\r\n" + full, "案件画面", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + full + "\"") { UseShellExecute = true });
        }
    }
}
