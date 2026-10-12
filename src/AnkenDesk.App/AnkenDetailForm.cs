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
        private readonly ComboBox _statusBox = new ComboBox();
        private readonly PdfPreviewPanel _previewPanel = new PdfPreviewPanel { Visible = false };
        private bool _loadingStatus;

        // タブ（比較表／経過／メモ／ファイル／類似案件／チェック）。メモ以降は、開いたときに作る。
        private readonly TabControl _tabs = new TabControl();
        private readonly TabPage _tabCompare = new TabPage("比較表");
        private readonly TabPage _tabHistory = new TabPage("経過");
        private readonly TabPage _tabNotes = new TabPage("メモ");
        private readonly TabPage _tabFiles = new TabPage("ファイル");
        private readonly TabPage _tabSimilar = new TabPage("過去の類似案件");
        private readonly TabPage _tabChecks = new TabPage("チェック");
        private Form? _notesView;
        private Form? _galleryView;
        private Form? _similarView;
        private Form? _checksView;
        private ToolStripMenuItem? _previewItem;

        // 経過の各行に対応する .msg のフルパス（無い行はnull）。行をダブルクリックして開く。
        private readonly List<string?> _historyMsg = new List<string?>();

        private AnkenRecord _anken = null!;
        private IReadOnlyList<AnkenSupplier> _suppliers = new List<AnkenSupplier>();

        public AnkenDetailForm(AppServices services, long ankenId)
        {
            _services = services;
            _ankenId = ankenId;
            UiStyle.Apply(this);
            var height = Math.Min(900, Screen.PrimaryScreen.WorkingArea.Height - 60);
            ClientSize = new Size(1300, height);
            StartPosition = FormStartPosition.CenterParent;

            // 1段目: 戻る、案件名
            var back = UiStyle.CreateButton("← 戻る", false, 110);
            back.Left = 16; back.Top = 8;
            back.Click += (s, e) => Close();
            _title.SetBounds(140, 8, 1000, 40);
            _title.AutoEllipsis = true;
            _title.TextAlign = ContentAlignment.MiddleLeft;
            _title.Font = new Font("BIZ UDPGothic", 18F, FontStyle.Bold);
            _title.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            var titleBar = new Panel { Dock = DockStyle.Top, Height = 56 };
            titleBar.Controls.AddRange(new Control[] { back, _title });

            // 2段目: よく使う操作は直接ボタン、あとは用途ごとのメニューにまとめる。
            var answer = UiStyle.CreateButton("回答を入力・変更", true, 170);
            answer.Click += (s, e) => EditAnswer();
            var mailRequest = UiStyle.CreateButton("見積依頼メール", true, 160);
            mailRequest.Click += (s, e) => OpenMail(MailKind.Request);
            var mailReminder = UiStyle.CreateButton("催促メール", true, 130);
            mailReminder.Click += (s, e) => OpenMail(MailKind.Reminder);

            var menuRequest = MenuButton("依頼", 100,
                MenuEntry("調達先を加える", AddSuppliers),
                MenuEntry("調達先を外す", RemoveSupplier),
                MenuEntry("外した調達先を戻す", RestoreRemovedSupplier),
                null,
                MenuEntry("見積依頼書（Excel）を作成", MakeRequestSheet),
                MenuEntry("回答期限をOutlookの予定表へ", SyncCalendar));
            var menuAnswer = MenuButton("回答", 100,
                MenuEntry("出し直しを受け取る", Requote),
                MenuEntry("辞退・検討中を設定", SetResponse),
                MenuEntry("履歴を見る", ShowHistory),
                null,
                MenuEntry("見積書を保存...", SaveQuoteFilesWithDialog),
                MenuEntry("見積書を見る", PreviewQuotes),
                MenuEntry("受信メールから取り込む", ImportFromMail));
            var menuCalc = MenuButton("見積計算", 130,
                MenuEntry("提出単価と粗利", OpenPricing),
                MenuEntry("比較表をチェック", RunChecks),
                null,
                MenuEntry("比較表をExcelに出力", ExportComparison),
                MenuEntry("客先見積書を作成", MakeClientQuote));
            var menuAnken = MenuButton("案件", 100,
                MenuEntry("案件の項目を編集", EditAnken),
                MenuEntry("結果を入力", OpenResult),
                MenuEntry("この案件を元に登録", CopyAsNew),
                null,
                MenuEntry("エクスプローラーで開く", OpenFolder),
                MenuEntry("PDFプレビュー欄を出す／隠す", TogglePreview, out _previewItem));

            var statusCaption = new Label { Text = "状態", AutoSize = false, Width = 44, Height = UiStyle.ButtonHeight, TextAlign = ContentAlignment.MiddleRight, Margin = new Padding(24, 4, 0, 4) };
            _statusBox.Width = 150;
            _statusBox.Margin = new Padding(0, 9, 0, 0);
            _statusBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _statusBox.Items.AddRange(AnkenStatus.All);
            _statusBox.SelectedIndexChanged += (s, e) => ChangeAnkenStatus();

            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 56, Padding = new Padding(12, 4, 12, 4), WrapContents = false };
            toolbar.Controls.AddRange(new Control[] { answer, mailRequest, mailReminder, menuRequest, menuAnswer, menuCalc, menuAnken, statusCaption, _statusBox });

            // 案件の情報帯
            var band = new Panel { Dock = DockStyle.Top, Height = 76, BackColor = Color.White };
            Place(band, "得意先・種別", _client, 0, 330);
            Place(band, "依頼日", _requestDate, 330, 180);
            Place(band, "回答期限", _dueDate, 510, 300);
            Place(band, "回答状況", _progress, 810, 450);
            band.Padding = new Padding(16, 0, 0, 0);
            var bandHost = new Panel { Dock = DockStyle.Top, Height = 84, Padding = new Padding(16, 4, 16, 4) };
            band.Dock = DockStyle.Fill;
            bandHost.Controls.Add(band);

            _hint.Dock = DockStyle.Bottom;
            _hint.Height = 28;
            _hint.TextAlign = ContentAlignment.MiddleLeft;
            _hint.Padding = new Padding(16, 0, 0, 0);
            _hint.ForeColor = Color.FromArgb(90, 96, 100);
            _hint.Text = "ファイルは、比較表の調達先の列へドロップすると見積書として保存、それ以外は保存先を選べます。Outlookのメールも、どこへでもドロップできます。";

            // ---- 比較表のタブ ----
            _grid.Dock = DockStyle.Fill;
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
                e.Effect = MailDrop.IsOutlookMail(e.Data) || (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop)) ? DragDropEffects.Copy : DragDropEffects.None;
            };
            _grid.DragDrop += (s, e) =>
            {
                if (MailDrop.IsOutlookMail(e.Data))
                {
                    BeginInvoke((Action)OpenDroppedMail);
                    return;
                }

                OnDropFiles(e);
            };

            // 画面のどこにOutlookのメールを落としても、取り込み画面を開く（ファイルのドロップは、表の調達先の列だけ）
            AllowDrop = true;
            DragEnter += (s, e) => e.Effect = MailDrop.IsOutlookMail(e.Data) || (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop)) ? DragDropEffects.Copy : DragDropEffects.None;
            DragDrop += (s, e) =>
            {
                if (MailDrop.IsOutlookMail(e.Data))
                {
                    BeginInvoke((Action)OpenDroppedMail);
                    return;
                }

                var paths = e.Data == null ? null : e.Data.GetData(DataFormats.FileDrop) as string[];
                if (paths != null && paths.Length > 0)
                {
                    var copy = paths.ToArray();
                    BeginInvoke((Action)(() => SaveFilesWithChoice(copy))); // ドラッグが終わってから、保存先を聞く
                }
            };

            _previewPanel.Dock = DockStyle.Right;
            _previewPanel.Width = 460;
            _tabCompare.Controls.Add(_grid);
            _tabCompare.Controls.Add(_previewPanel);
            _grid.BringToFront();
            _tabCompare.Padding = new Padding(0, 8, 0, 0);
            _grid.CurrentCellChanged += (s, e) => UpdatePreviewPanel();

            // ---- 経過のタブ ----
            _status.Dock = DockStyle.Top;
            _status.Height = 28;
            _status.ForeColor = Color.FromArgb(90, 96, 100);
            _history.Dock = DockStyle.Fill;
            _history.IntegralHeight = false;
            _history.SelectionMode = SelectionMode.One;
            _history.DoubleClick += (s, e) => OpenHistoryMsg();
            _tabHistory.Controls.Add(_history);
            _tabHistory.Controls.Add(_status);
            _tabHistory.Padding = new Padding(0, 8, 0, 0);

            // ---- ファイルのタブ（左にフォルダのファイル数、右に画像） ----
            var folderPanel = new Panel { Dock = DockStyle.Left, Width = 300, BackColor = Color.White, Padding = new Padding(8) };
            _folders.Dock = DockStyle.Fill;
            _folders.IntegralHeight = false;
            _folders.SelectionMode = SelectionMode.None;
            _folders.TabStop = false;
            folderPanel.Controls.Add(_folders);
            folderPanel.Controls.Add(new Label { Text = "フォルダ（ファイル数）", Dock = DockStyle.Top, Height = 32, Font = new Font("BIZ UDPGothic", 12F, FontStyle.Bold) });
            _tabFiles.Controls.Add(folderPanel);
            _tabFiles.Padding = new Padding(0, 8, 0, 0);

            _tabs.Dock = DockStyle.Fill;
            _tabs.Font = new Font("BIZ UDPGothic", 11F);
            _tabs.Padding = new Point(18, 6);
            _tabs.Controls.AddRange(new Control[] { _tabCompare, _tabHistory, _tabNotes, _tabFiles, _tabSimilar, _tabChecks });
            _tabs.SelectedIndexChanged += (s, e) => EnsureTab();

            var tabHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 0, 16, 4) };
            tabHost.Controls.Add(_tabs);

            // Dockの積み順: 先に足したものが奥（下）。
            Controls.Add(tabHost);
            Controls.Add(_hint);
            Controls.Add(bandHost);
            Controls.Add(toolbar);
            Controls.Add(titleBar);

            Activated += (s, e) => Reload();
            Reload();
        }

        // Escで戻る。ただし、文字を入力しているとき（メモなど）は、入力を消さないために戻らない。
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape && !(ActiveControl is TextBoxBase) && !(ActiveControl is ComboBox))
            {
                var inner = ActiveControl as ContainerControl;
                if (inner == null || !(inner.ActiveControl is TextBoxBase))
                {
                    Close();
                    return true;
                }
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        // 用途ごとにまとめたメニューのボタン。押すと、下に項目が開く。
        private static Button MenuButton(string text, int width, params ToolStripItem?[] entries)
        {
            var b = UiStyle.CreateButton(text + " ▼", false, width);
            var menu = new ContextMenuStrip { Font = new Font("BIZ UDPGothic", 11F), ShowImageMargin = false };
            foreach (var e in entries)
            {
                if (e == null)
                {
                    menu.Items.Add(new ToolStripSeparator());
                }
                else
                {
                    e.Padding = new Padding(0, 6, 0, 6);
                    menu.Items.Add(e);
                }
            }

            b.Click += (s, e) => menu.Show(b, new Point(0, b.Height));
            return b;
        }

        private static ToolStripMenuItem MenuEntry(string text, Action action)
        {
            var item = new ToolStripMenuItem(text);
            item.Click += (s, e) => action();
            return item;
        }

        private static ToolStripMenuItem MenuEntry(string text, Action action, out ToolStripMenuItem item)
        {
            item = MenuEntry(text, action);
            item.CheckOnClick = false;
            return item;
        }

        // ---- タブ ----

        private void EnsureTab()
        {
            var tab = _tabs.SelectedTab;
            if (tab == _tabNotes)
            {
                if (_notesView == null)
                {
                    _notesView = new NotesForm(_services, _anken);
                    EmbeddedForm.Host(_tabNotes, _notesView);
                }
            }
            else if (tab == _tabFiles)
            {
                if (_galleryView == null)
                {
                    _galleryView = new FileGalleryForm(_services, _anken);
                    EmbeddedForm.Host(_tabFiles, _galleryView);
                    _galleryView.BringToFront();
                }
            }
            else if (tab == _tabSimilar)
            {
                ReplaceView(ref _similarView, _tabSimilar, new SimilarForm(_services, _anken));
            }
            else if (tab == _tabChecks)
            {
                BuildChecksTab();
            }

            if (tab != _tabNotes)
            {
                RefreshNoteCount();
            }
        }

        private static void ReplaceView(ref Form? slot, TabPage page, Form view)
        {
            if (slot != null)
            {
                page.Controls.Remove(slot);
                slot.Dispose();
            }

            slot = view;
            EmbeddedForm.Host(page, view);
        }

        // 比較表のチェック。開くたびに、今の内容で作り直す。
        private void BuildChecksTab()
        {
            var patterns = _services.Db.ListQuantities(_ankenId);
            if (_suppliers.Count == 0 || patterns.Count == 0)
            {
                ReplaceView(ref _checksView, _tabChecks, new QuoteChecksForm(Home.Title(_anken), new List<QuoteWarning>(), "調達先と見積依頼数量が要ります。"));
                return;
            }

            var warnings = QuoteChecks.Run(patterns, _suppliers, _services.Db.ListQuotes(_ankenId), DateTime.Today);
            ReplaceView(ref _checksView, _tabChecks, new QuoteChecksForm(Home.Title(_anken), warnings));
        }

        private void RefreshNoteCount()
        {
            var notes = _services.Db.CountNotes(_ankenId);
            _tabNotes.Text = notes > 0 ? "メモ（" + notes + "件）" : "メモ";
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
            RefreshNoteCount();
            _loadingStatus = true;
            _statusBox.SelectedItem = a.Status;
            _loadingStatus = false;
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
            _dueDate.Text = (total > 0 && answered > 0 && !_suppliers.Any(x => x.IsPending))
                ? due + "（全社回答済み）"
                : due + "（" + Comparison.DueStatus(a.ReplyDueDate, DateTime.Today) + "）";
            if (total == 0)
            {
                _progress.Text = "調達先が未登録です";
            }
            else
            {
                var declined = _suppliers.Count(s => s.IsDeclined && !s.IsAnswered);
                var pending = _suppliers.Where(s => s.IsPending).Select(s => s.ShortName + (s.IsConsidering ? "（検討中）" : "")).ToList();
                _progress.Text = answered + "/" + total + "社" + (declined > 0 ? "（辞退" + declined + "）" : "") + (pending.Count > 0 ? "（未回答: " + string.Join("、", pending) + "）" : "");
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
                var col = _grid.Columns[_grid.Columns.Add("s" + s.SupplierId, s.ShortName + "\r\n" + SupplierStateText(s))];
                col.Width = 200;
                col.Tag = s;
                col.SortMode = DataGridViewColumnSortMode.NotSortable;
            }

            var cheapest = new HashSet<string>(Comparison.CheapestQuotes(quotes).Select(q => Key(q.PatternId, q.SupplierId)));
            var breakdowns = _services.Db.ListBreakdowns(_ankenId);
            IReadOnlyDictionary<string, PreviousPrice> previous;
            try
            {
                previous = PriceHistory.PreviousQuotes(_services.Db, _anken);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException)
            {
                previous = new Dictionary<string, PreviousPrice>();
            }

            foreach (var p in patterns)
            {
                var cells = new List<object> { p.Kind + "\r\n" + p.Quantity.ToString("#,##0.####", CultureInfo.InvariantCulture) + p.Unit };
                var styles = new List<bool> { false };
                foreach (var s in _suppliers)
                {
                    var q = quotes.FirstOrDefault(x => x.PatternId == p.Id && x.SupplierId == s.SupplierId);
                    var isCheapest = q != null && cheapest.Contains(Key(p.Id, s.SupplierId));
                    IReadOnlyList<BreakdownLine>? bd;
                    breakdowns.TryGetValue(new KeyValuePair<long, long>(s.SupplierId, p.Id), out bd);
                    cells.Add(QuoteText(q, s, isCheapest, bd, previous, ExcelExports.PatternText(p)));
                    styles.Add(isCheapest);
                }

                AddRow(cells, styles);
            }

            AddTextRow("返事の状況", s => ResponseText(s));
            AddTextRow("見積の有効期限", s => QuoteValidity.Text(s, DateTime.Today));
            AddTextRow("別費用（型・治具）", s => s.ExtraCost);
            AddTextRow("緩和条件", s => s.Relaxation);
            AddTextRow("備考", s => s.Note);
            AddTextRow("依頼送付日", s => s.SentAt.HasValue ? s.SentAt.Value.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) : "");
            AddTextRow("回答受領日", s => s.ReceivedAt.HasValue ? s.ReceivedAt.Value.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) : "");
            AddTextRow("見積書PDF（5番のフォルダ内）", s => QuotePdfNames(s));
        }

        private static string SupplierStateText(AnkenSupplier s)
        {
            return s.IsAnswered ? "回答済み" : s.IsDeclined ? "辞退" : s.IsConsidering ? "検討中" : "未回答";
        }

        private static string ResponseText(AnkenSupplier s)
        {
            if (s.IsAnswered)
            {
                return "";
            }

            if (s.IsDeclined)
            {
                return "辞退" + (string.IsNullOrWhiteSpace(s.DeclineReason) ? "" : "：" + s.DeclineReason);
            }

            return s.IsConsidering ? "検討中" + (s.PromisedDate.HasValue ? "（" + s.PromisedDate.Value.ToString("M/d", CultureInfo.InvariantCulture) + "までに回答予定）" : "") : "";
        }

        private static string Key(long patternId, long supplierId)
        {
            return patternId + ":" + supplierId;
        }

        private static string QuoteText(Quote? q, AnkenSupplier s, bool isCheapest, IReadOnlyList<BreakdownLine>? breakdown = null,
            IReadOnlyDictionary<string, PreviousPrice>? previous = null, string patternText = "")
        {
            if (q == null)
            {
                return s.IsAnswered ? "—" : s.IsDeclined ? "—\r\n（辞退）" : s.IsConsidering ? "—\r\n（検討中）" : "—\r\n（未回答）";
            }

            var lines = new List<string>();
            if (isCheapest)
            {
                lines.Add("【最安】");
            }

            lines.Add(q.UnitPrice.HasValue ? Comparison.FormatPrice(q.UnitPrice.Value) : "単価なし");
            lines.Add(q.LeadTimeDays.HasValue ? "LT " + q.LeadTimeDays.Value + "日" : "LTなし");
            PreviousPrice prev;
            if (previous != null && q.UnitPrice.HasValue && previous.TryGetValue(PriceHistory.QuoteKey(s.SupplierId, patternText), out prev))
            {
                lines.Add(PreviousPrice.Format(prev, q.UnitPrice));
            }

            if (breakdown != null && breakdown.Count > 0)
            {
                // 内訳の先頭の2行だけ。全部は「回答を入力・変更」の「内訳」で見られる。
                var first = string.Join(" ", breakdown.Take(2).Select(b => b.Item + Comparison.FormatPrice(b.Amount)));
                lines.Add("内訳: " + first + (breakdown.Count > 2 ? " ほか" + (breakdown.Count - 2) + "行" : ""));
            }

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

        private sealed class HistoryEvent
        {
            public DateTime When;
            public string Text = "";
            public string? MsgPath;
        }

        private void BuildHistory()
        {
            var events = new List<HistoryEvent>();
            events.Add(new HistoryEvent { When = _anken.RequestDate, Text = "案件の依頼日" });
            foreach (var s in _suppliers)
            {
                if (s.SentAt.HasValue)
                {
                    events.Add(new HistoryEvent { When = s.SentAt.Value, Text = "依頼を送付（" + s.ShortName + "）" });
                }

                if (s.ReceivedAt.HasValue)
                {
                    events.Add(new HistoryEvent { When = s.ReceivedAt.Value, Text = "回答を受領（" + s.ShortName + "）" });
                }
            }

            // メールの記録（送ったもの・送信トレイに残ったもの・失敗したもの）。.msgがあれば、ダブルクリックで開く。
            var supplierNames = _suppliers.ToDictionary(x => x.SupplierId, x => x.ShortName);
            foreach (var m in _services.Db.ListMailLog(_ankenId))
            {
                string name;
                var who = m.SupplierId.HasValue && supplierNames.TryGetValue(m.SupplierId.Value, out name) ? name : m.ToAddress;
                events.Add(new HistoryEvent
                {
                    When = m.CreatedAt,
                    Text = MailSender.KindText(m.Kind) + "メール（" + who + "）　" + m.Status + (m.MsgPath != null ? "　.msg保存済み（ダブルクリックで開く）" : ""),
                    MsgPath = m.MsgPath == null ? null : Path.Combine(_services.WorkspaceRoot, m.MsgPath),
                });
            }

            _history.Items.Clear();
            _historyMsg.Clear();
            foreach (var ev in events.OrderBy(x => x.When))
            {
                var format = ev.When.TimeOfDay == TimeSpan.Zero ? "yyyy/MM/dd" : "yyyy/MM/dd HH:mm";
                _history.Items.Add(ev.When.ToString(format, CultureInfo.InvariantCulture) + "　" + ev.Text);
                _historyMsg.Add(ev.MsgPath);
            }

            var pending = _suppliers.Where(s => !s.IsAnswered && s.SentAt.HasValue).ToList();
            foreach (var s in pending)
            {
                _history.Items.Add("　　　　　　　未回答（" + s.ShortName + "）");
                _historyMsg.Add(null);
            }
        }

        private void OpenHistoryMsg()
        {
            var i = _history.SelectedIndex;
            var path = i >= 0 && i < _historyMsg.Count ? _historyMsg[i] : null;
            if (path == null)
            {
                return;
            }

            if (!File.Exists(path))
            {
                MessageBox.Show(this, ".msgが見つかりません。\r\n" + path, "案件デスク", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                MessageBox.Show(this, ".msgを開けませんでした。\r\n\r\n" + ex.Message, "案件デスク", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
            using (var dlg = new SupplierPickForm(_services, _suppliers.Select(s => s.SupplierId), _services.Db.RecentSupplierIdsForClient(_anken.ClientId, _ankenId)))
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

        private void SetResponse()
        {
            var s = SelectedSupplier();
            if (s == null)
            {
                return;
            }

            using (var dlg = new ResponseStatusForm(_services, s))
            {
                dlg.ShowDialog(this);
            }

            Reload();
        }

        private void RunChecks()
        {
            if (_tabs.SelectedTab == _tabChecks)
            {
                BuildChecksTab();
            }
            else
            {
                _tabs.SelectedTab = _tabChecks;
            }
        }

        private void MakeClientQuote()
        {
            using (var dlg = new ClientQuoteForm(_services, _anken))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK || dlg.SavedPath == null)
                {
                    return;
                }

                SetStatus("客先見積書を保存しました: " + Path.GetFileName(dlg.SavedPath));
                var open = MessageBox.Show(this, "客先見積書を保存しました。\r\n\r\n" + dlg.SavedPath + "\r\n\r\nExcelで開きますか？",
                    "客先見積書", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (open == DialogResult.Yes)
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dlg.SavedPath) { UseShellExecute = true });
                    }
                    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException)
                    {
                        MessageBox.Show(this, "開けませんでした。\r\n\r\n" + ex.Message, "客先見積書", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
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
                "「" + s.SupplierName + "」をこの案件から外します。\r\n入力した単価・日付・備考は、あとで「外した調達先を戻す」で戻せます（旧版の履歴は戻りません）。\r\n保存したPDFなどのファイルは消えません。",
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
            var savedPaths = new List<string>();
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
                    var dest = QuoteFiles.Save(AnkenFullPath(), s.ShortName, path);
                    saved.Add(Path.GetFileName(dest));
                    savedPaths.Add(dest);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
                {
                    failed.Add(Path.GetFileName(path) + "（" + ex.Message + "）");
                }
            }

            if (saved.Count > 0)
            {
                SetStatus(s.ShortName + " の見積書を保存しました: " + string.Join("、", saved));
                ThumbnailService.WarmAsync(savedPaths); // 画像を先に作っておく
            }

            if (failed.Count > 0)
            {
                MessageBox.Show(this, "保存できなかったファイルがあります。\r\n\r\n" + string.Join("\r\n", failed), "見積書の保存", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            Reload();
            UpdatePreviewPanel();
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
                SaveFilesWithChoice(paths); // 調達先の列でなければ、保存先を選んでもらう
                return;
            }

            SaveQuoteFiles(supplier, paths);
        }

        // 保存先を選んでもらってから、コピーする（客先の依頼ファイル／調達先の見積書／指定のサブフォルダ）。
        private void SaveFilesWithChoice(IReadOnlyList<string> paths)
        {
            var files = paths.Where(File.Exists).ToList();
            if (files.Count == 0)
            {
                MessageBox.Show(this, "ファイルをドロップしてください（フォルダは扱えません）。", "ファイルの保存", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var dlg = new FileTargetForm(files, _suppliers))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                if (dlg.Selected == FileTargetForm.Target.SupplierQuote)
                {
                    SaveQuoteFiles(dlg.Supplier!, files);
                    return;
                }

                var results = dlg.Selected == FileTargetForm.Target.ClientRequest
                    ? RequestFiles.CopyInto(AnkenFullPath(), files)
                    : RequestFiles.CopyToSubfolder(AnkenFullPath(), dlg.SubfolderIndex, files);
                var ok = results.Where(r => r.Destination != null).ToList();
                var failed = results.Where(r => r.Destination == null).ToList();
                ThumbnailService.WarmAsync(ok.Select(r => r.Destination!));
                SetStatus(ok.Count + " 個のファイルを保存しました");
                if (failed.Count > 0)
                {
                    MessageBox.Show(this, "保存できなかったファイルがあります。\r\n\r\n" + string.Join("\r\n", failed.Select(f => Path.GetFileName(f.Source) + "（" + f.Error + "）")),
                        "ファイルの保存", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

                Reload();
            }
        }

        // 回答期限を、Outlookの予定表に入れる（入れてあれば、期限の変更などを反映して更新する）。
        private void SyncCalendar()
        {
            var removeFirst = _anken.CalendarEntryId != null && AnkenStatus.IsClosed(_anken.Status);
            var anken = _anken;
            try
            {
                var entry = Background.Run<string?>(this, "Outlookの予定表に入れています。\r\nしばらくお待ちください。", () =>
                {
                    using (var cal = new AnkenDesk.OutlookAccess.OutlookCalendar())
                    {
                        if (removeFirst)
                        {
                            DeadlineCalendar.Remove(cal, _services.Db, anken);
                            return null;
                        }

                        return DeadlineCalendar.Sync(cal, _services.Db, anken);
                    }
                });
                SetStatus(entry == null ? "予定表の予定を削除しました（案件が終了・保留のため）" : "回答期限（" + anken.ReplyDueDate.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) + "）をOutlookの予定表に入れました");
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(this, "Outlookの予定表に入れられませんでした。\r\n\r\n" + ex.Message, "予定表", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            Reload();
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
                    foreach (var q in dlg.ResultQuotes)
                    {
                        List<BreakdownLine>? lines;
                        dlg.ResultBreakdowns.TryGetValue(q.PatternId, out lines);
                        _services.Db.SaveBreakdown(dlg.Answer.AnkenId, dlg.Answer.SupplierId, q.PatternId, lines ?? new List<BreakdownLine>());
                    }
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

        private void ImportFromMail()
        {
            using (var dlg = new ImportMailForm(_services, _ankenId))
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

        private void OpenPricing()
        {
            if (_suppliers.Count == 0 || _services.Db.ListQuantities(_ankenId).Count == 0)
            {
                MessageBox.Show(this, "調達先と見積依頼数量が要ります。先に、調達先を加えて回答を入力してください。", "提出単価と粗利", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var dlg = new PricingForm(_services, _anken))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    SetStatus("提出単価を保存しました");
                }
            }
        }

        private void OpenResult()
        {
            using (var dlg = new ResultForm(_services, _anken))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    SetStatus("結果を保存しました");
                }
            }

            Reload();
        }

        // OutlookからドラッグしたメールをOutlookの選択から読んで、取り込み画面を開く。
        // ドロップの処理の中でOutlookを呼ぶと、ドラッグが終わる前に待ち合わせが起きうるので、ドロップが終わってから開く。
        private void OpenDroppedMail()
        {
            using (var dlg = new ImportMailForm(_services, _ankenId, true))
            {
                dlg.ShowDialog(this);
            }

            Reload();
        }

        private void ChangeAnkenStatus()
        {
            if (_loadingStatus || _statusBox.SelectedItem == null)
            {
                return;
            }

            var status = (string)_statusBox.SelectedItem;
            if (status == _anken.Status)
            {
                return;
            }

            if (AnkenStatus.IsClosed(status))
            {
                var ok = MessageBox.Show(this, "状態を「" + status + "」にします。\r\nホームの「要対応」には出なくなります（あとで戻せます）。",
                    "案件の状態", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (ok != DialogResult.Yes)
                {
                    _loadingStatus = true;
                    _statusBox.SelectedItem = _anken.Status;
                    _loadingStatus = false;
                    return;
                }
            }

            _services.Db.SetAnkenStatus(_ankenId, status);
            SetStatus("案件の状態を「" + status + "」にしました");
            Reload();
        }

        // 比較表を、案件の「6.見積計算」にExcelで出力する。
        private void ExportComparison()
        {
            if (_suppliers.Count == 0)
            {
                MessageBox.Show(this, "調達先が加えられていません。", "比較表をExcelに出力", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string path;
            try
            {
                var dir = Path.Combine(AnkenFullPath(), FolderNames.Subfolders[5]);
                Directory.CreateDirectory(dir);
                path = QuoteFiles.UniquePath(dir, DateTime.Today.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + FolderNames.Separator + "比較表.xlsx");
                ExcelExports.WriteComparison(path, _anken, _services.Db.ListQuantities(_ankenId), _suppliers, _services.Db.ListQuotes(_ankenId));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
            {
                MessageBox.Show(this, "比較表を作れませんでした。\r\n\r\n" + ex.Message, "比較表をExcelに出力", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SetStatus("比較表を出力しました: " + Path.GetFileName(path));
            Reload();
            if (MessageBox.Show(this, "比較表を「6.見積計算」に出力しました。\r\n\r\n" + path + "\r\n\r\nExcelで開きますか？",
                    "比較表をExcelに出力", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
        }

        private void RestoreRemovedSupplier()
        {
            var removed = _services.Db.ListRemovedSuppliers(_ankenId);
            if (removed.Count == 0)
            {
                MessageBox.Show(this, "戻せる調達先はありません。", "外した調達先を戻す", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var lines = removed.Select(r => r.SupplierName + "　（" + r.RemovedAt.ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture) + " に外した"
                + (r.HadAnswer ? "・回答あり" : "") + "）").ToList();
            using (var dlg = new PickListForm("外した調達先を戻す", "戻す調達先を選んでください。外した時点の日付・単価・備考ごと戻ります（旧版の履歴は戻りません）。", lines, "戻す"))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                var chosen = removed[dlg.SelectedIndexResult];
                if (!_services.Db.RestoreRemovedSupplier(chosen.Id))
                {
                    MessageBox.Show(this, "戻せませんでした（すでに加え直してあります）。", "外した調達先を戻す", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    SetStatus("「" + chosen.SupplierName + "」を戻しました");
                }
            }

            Reload();
        }

        // この案件の数量・項目・調達先を引き継いで、新しい案件の登録画面を開く。
        private void CopyAsNew()
        {
            using (var dlg = new RegisterForm(_services, _ankenId))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    SetStatus("新しい案件を登録しました");
                }
            }
        }

        // PDFプレビュー欄の表示・非表示（比較表の右に出す）。
        private void TogglePreview()
        {
            _tabs.SelectedTab = _tabCompare;
            _previewPanel.Visible = !_previewPanel.Visible;
            if (_previewItem != null)
            {
                _previewItem.Checked = _previewPanel.Visible;
            }

            UpdatePreviewPanel();
        }

        private void UpdatePreviewPanel()
        {
            if (!_previewPanel.Visible)
            {
                return;
            }

            var cell = _grid.CurrentCell;
            var s = cell != null && cell.ColumnIndex > 0 ? _grid.Columns[cell.ColumnIndex].Tag as AnkenSupplier : null;
            if (s == null)
            {
                _previewPanel.Clear("調達先の列を選ぶと、見積書（PDF）の1ページ目がここに出ます。");
                return;
            }

            var pdf = QuoteFiles.ListCurrent(AnkenFullPath(), s.ShortName)
                .Select(f => Path.Combine(QuoteFiles.QuoteDir(AnkenFullPath()), f))
                .FirstOrDefault(f => string.Equals(Path.GetExtension(f), ".pdf", StringComparison.OrdinalIgnoreCase));
            _previewPanel.ShowPdf(pdf, "「" + s.ShortName + "」の見積書（PDF）はまだありません。");
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
