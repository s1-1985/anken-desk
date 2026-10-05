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
            ClientSize = new Size(1300, 880);
            StartPosition = FormStartPosition.CenterParent;

            _title.SetBounds(16, 10, 560, 44);
            _title.Font = new Font("BIZ UDPGothic", 18F, FontStyle.Bold);

            var add = UiStyle.CreateButton("調達先を加える", true, 170);
            add.Left = 590; add.Top = 12;
            add.Click += (s, e) => AddSuppliers();
            var input = UiStyle.CreateButton("回答を入力・変更", true, 190);
            input.Left = 768; input.Top = 12;
            input.Click += (s, e) => EditAnswer();
            var remove = UiStyle.CreateButton("調達先を外す", false, 150);
            remove.Left = 966; remove.Top = 12;
            remove.Click += (s, e) => RemoveSupplier();
            var open = UiStyle.CreateButton("エクスプローラーで開く", false, 230);
            open.Left = 1124; open.Top = 12;
            open.Click += (s, e) => OpenFolder();

            var band = new Panel { Left = 16, Top = 64, Width = 1268, Height = 80, BackColor = Color.White };
            Place(band, "得意先・種別", _client, 0, 330);
            Place(band, "依頼日", _requestDate, 330, 180);
            Place(band, "回答期限", _dueDate, 510, 300);
            Place(band, "回答状況", _progress, 810, 450);

            var folderPanel = new Panel { Left = 16, Top = 156, Width = 300, Height = 560, BackColor = Color.White };
            folderPanel.Controls.Add(new Label { Text = "フォルダ（ファイル数）", Left = 12, Top = 10, Width = 276, Height = 28, Font = new Font("BIZ UDPGothic", 12F, FontStyle.Bold) });
            _folders.SetBounds(8, 44, 284, 508);
            _folders.IntegralHeight = false;
            _folders.SelectionMode = SelectionMode.None;
            _folders.TabStop = false;
            folderPanel.Controls.Add(_folders);

            _grid.SetBounds(328, 156, 956, 560);
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
                if (e.ColumnIndex > 0 && e.RowIndex >= 0)
                {
                    EditAnswer();
                }
            };

            var historyTitle = new Label { Text = "経過", Left = 16, Top = 724, Width = 200, Height = 26, Font = new Font("BIZ UDPGothic", 12F, FontStyle.Bold) };
            _history.SetBounds(16, 752, 1268, 116);
            _history.IntegralHeight = false;
            _history.SelectionMode = SelectionMode.None;
            _history.TabStop = false;

            Controls.AddRange(new Control[] { _title, add, input, remove, open, band, folderPanel, _grid, historyTitle, _history });

            Activated += (s, e) => Reload();
            Reload();
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

        // 5.調達先見積もり の中で、「略称　」で始まるファイルの名前（段2bで、ドロップ保存した名前がここに出る）。
        private string QuotePdfNames(AnkenSupplier s)
        {
            var dir = Path.Combine(_services.WorkspaceRoot, _anken.FolderPath, FolderNames.Subfolders[4]);
            if (!Directory.Exists(dir))
            {
                return "";
            }

            var prefix = s.ShortName + FolderNames.Separator;
            var names = Directory.GetFiles(dir)
                .Select(Path.GetFileName)
                .Where(n => n != null && n.StartsWith(prefix, StringComparison.Ordinal))
                .Select(n => n!)
                .ToList();
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

            _history.Items.Clear();
            foreach (var ev in events.OrderBy(x => x.Key))
            {
                _history.Items.Add(ev.Key.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) + "　" + ev.Value);
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
                "「" + s.SupplierName + "」をこの案件から外します。\r\n入力した単価・日付・備考も消えます。\r\n（保存したPDFなどのファイルは消えません）",
                "調達先を外す", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (ok != DialogResult.Yes)
            {
                return;
            }

            _services.Db.RemoveSupplierFromAnken(_ankenId, s.SupplierId);
            Reload();
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
