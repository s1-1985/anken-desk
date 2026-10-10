using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using AnkenDesk.Core;

namespace AnkenDesk.App
{
    /// <summary>1社分の回答を入力する（数量パターンごとの単価・リードタイムと、調達先ごとの項目）。</summary>
    internal sealed class QuoteEditForm : Form
    {
        private sealed class Row
        {
            public QuantityPattern Pattern = null!;
            public TextBox Price = null!;
            public TextBox LeadTime = null!;
            public Button Breakdown = null!;
        }

        private readonly AppServices _services;
        private readonly AnkenSupplier _answer;
        private readonly List<Row> _rows = new List<Row>();
        private readonly DateTimePicker _sent = new DateTimePicker { Format = DateTimePickerFormat.Short, ShowCheckBox = true };
        private readonly DateTimePicker _received = new DateTimePicker { Format = DateTimePickerFormat.Short, ShowCheckBox = true };
        private readonly TextBox _extra = new TextBox();
        private readonly TextBox _relax = new TextBox();
        private readonly TextBox _note = new TextBox();
        private readonly bool _requote;
        private readonly CheckBox _keep = new CheckBox();
        private readonly CheckBox _move = new CheckBox();
        private readonly Label _newFileLabel = new Label();

        // 数量パターンごとの内訳（パターンID → 行）。空の行のないものは、内訳なし。
        private readonly Dictionary<long, List<BreakdownLine>> _breakdowns = new Dictionary<long, List<BreakdownLine>>();

        /// <summary>出し直しのとき、入力した内訳。呼び出し側が、新しい版の保存のあとで保存する。</summary>
        public IReadOnlyDictionary<long, List<BreakdownLine>> ResultBreakdowns
        {
            get { return _breakdowns; }
        }

        /// <summary>入力した回答（調達先ごとの項目）。</summary>
        public AnkenSupplier Answer
        {
            get { return _answer; }
        }

        /// <summary>入力した、数量パターンごとの単価・リードタイム。出し直しのときは、呼び出し側が保存する。</summary>
        public List<Quote> ResultQuotes { get; } = new List<Quote>();

        /// <summary>出し直しのとき: 旧版を履歴として残すか（初期値は残す）。</summary>
        public bool KeepHistory
        {
            get { return _keep.Checked; }
        }

        /// <summary>出し直しのとき: 今ある見積書を旧版フォルダへ移すか。</summary>
        public bool MoveOldFiles
        {
            get { return _move.Checked; }
        }

        /// <summary>出し直しのとき: 新しい見積書として保存するファイル。選ばなければ null。</summary>
        public string? NewFilePath { get; private set; }

        /// <param name="requote">true なら「出し直しを受け取る」（旧版の扱いを選べる。DBへの保存は呼び出し側）。</param>
        /// <param name="currentFiles">出し直しのとき、今ある見積書のファイル名。</param>
        public QuoteEditForm(AppServices services, AnkenSupplier answer, IReadOnlyList<QuantityPattern> patterns, IReadOnlyList<Quote> quotes,
            bool requote = false, IReadOnlyList<string>? currentFiles = null)
        {
            _services = services;
            _answer = answer;
            _requote = requote;
            UiStyle.Apply(this);
            Text = (requote ? "出し直しを受け取る: " : "回答を入力: ") + answer.SupplierName;
            ClientSize = new Size(900, 640);

            Controls.Add(new Label
            {
                Text = answer.SupplierName + (requote ? " の出し直し（新しい回答）" : " の回答"),
                Font = new Font("BIZ UDPGothic", 14F, FontStyle.Bold),
                Left = 16, Top = 12, Width = 720, Height = 36,
            });

            if (!requote)
            {
                foreach (var kv in _services.Db.ListBreakdowns(answer.AnkenId))
                {
                    if (kv.Key.Key == answer.SupplierId)
                    {
                        _breakdowns[kv.Key.Value] = kv.Value.ToList();
                    }
                }
            }

            var table = new TableLayoutPanel { Left = 16, Top = 56, Width = 860, AutoSize = true, ColumnCount = 4 };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            table.Controls.Add(Head("数量"));
            table.Controls.Add(Head("単価（円）"));
            table.Controls.Add(Head("リードタイム（日）"));
            table.Controls.Add(Head("内訳"));
            foreach (var p in patterns)
            {
                var row = new Row { Pattern = p, Price = new TextBox { Width = 200 }, LeadTime = new TextBox { Width = 180 } };
                var q = requote ? null : quotes.FirstOrDefault(x => x.PatternId == p.Id && x.SupplierId == answer.SupplierId);
                if (q != null)
                {
                    row.Price.Text = q.UnitPrice.HasValue ? q.UnitPrice.Value.ToString("0.####", CultureInfo.InvariantCulture) : "";
                    row.LeadTime.Text = q.LeadTimeDays.HasValue ? q.LeadTimeDays.Value.ToString(CultureInfo.InvariantCulture) : "";
                }

                row.Breakdown = new Button { Width = 130, Height = 32, FlatStyle = FlatStyle.Flat };
                row.Breakdown.FlatAppearance.BorderColor = UiStyle.Primary;
                var rowRef = row;
                row.Breakdown.Click += (s, e) => EditBreakdown(rowRef);
                ShowBreakdownText(row);
                _rows.Add(row);
                table.Controls.Add(new Label { Text = p.Kind + "　" + p.Quantity.ToString("#,##0.####", CultureInfo.InvariantCulture) + p.Unit, Width = 270, Height = 32, TextAlign = ContentAlignment.MiddleLeft });
                table.Controls.Add(row.Price);
                table.Controls.Add(row.LeadTime);
                table.Controls.Add(row.Breakdown);
            }

            Controls.Add(table);

            var y = 56 + 40 * (patterns.Count + 1) + 24;
            AddLabeled("依頼送付日", _sent, 16, y, 200);
            AddLabeled("回答受領日（入れると「回答済み」になります）", _received, 300, y, 200);
            AddLabeled("別費用（型・治具など）", _extra, 16, y + 70, 720);
            AddLabeled("緩和条件", _relax, 16, y + 140, 720);
            AddLabeled("備考", _note, 16, y + 210, 720);

            if (answer.SentAt.HasValue) { _sent.Value = answer.SentAt.Value; _sent.Checked = true; } else { _sent.Checked = false; }
            if (requote)
            {
                // 出し直し: 受領日は今日から。別費用・緩和条件・備考は新しい回答として空から入れる（旧版は履歴に残る）。
                _received.Value = DateTime.Today;
                _received.Checked = true;
            }
            else
            {
                if (answer.ReceivedAt.HasValue) { _received.Value = answer.ReceivedAt.Value; _received.Checked = true; } else { _received.Checked = false; }
                _extra.Text = answer.ExtraCost;
                _relax.Text = answer.Relaxation;
                _note.Text = answer.Note;
            }

            var bottom = y + 210 + 70;
            if (requote)
            {
                var files = currentFiles ?? new List<string>();
                var ry = bottom + 8;
                Controls.Add(new Label { Text = "旧版（今の回答）の扱い", Left = 16, Top = ry, Width = 720, Height = 28, Font = new Font("BIZ UDPGothic", 12F, FontStyle.Bold) });
                _keep.SetBounds(16, ry + 32, 720, 32);
                _keep.Text = "旧版を履歴として残す（比較表には出ません。「履歴を見る」から開けます）";
                _keep.Checked = true;
                _move.SetBounds(16, ry + 66, 720, 32);
                _move.Text = files.Count == 0
                    ? "今ある見積書のファイルはありません"
                    : "今ある見積書 " + files.Count + " 件を、「" + FolderNames.OldVersionFolder + "」フォルダへ移す（削除はしません）";
                _move.Checked = files.Count > 0;
                _move.Enabled = files.Count > 0;
                var pick = UiStyle.CreateButton("新しい見積書を選ぶ...", false, 240);
                pick.Left = 16; pick.Top = ry + 104;
                pick.Click += (s, e) => PickNewFile();
                _newFileLabel.SetBounds(264, ry + 110, 472, 28);
                _newFileLabel.Text = "（選ばなくても、あとで保存できます）";
                Controls.AddRange(new Control[] { _keep, _move, pick, _newFileLabel });
                bottom = ry + 104 + 48;
            }

            ClientSize = new Size(900, Math.Max(640, bottom + 80));

            var save = UiStyle.CreateButton("保存", true, 140);
            save.Left = 598; save.Top = ClientSize.Height - 56;
            save.Click += (s, e) => Save();
            var cancel = UiStyle.CreateButton("キャンセル", false, 140);
            cancel.Left = 744; cancel.Top = ClientSize.Height - 56;
            cancel.Click += (s, e) => DialogResult = DialogResult.Cancel;
            Controls.AddRange(new Control[] { save, cancel });
        }

        private void ShowBreakdownText(Row row)
        {
            List<BreakdownLine>? lines;
            var n = _breakdowns.TryGetValue(row.Pattern.Id, out lines) ? lines.Count : 0;
            row.Breakdown.Text = n > 0 ? "内訳（" + n + "行）" : "内訳...";
            row.Breakdown.Font = new Font("BIZ UDPGothic", 10.5F, n > 0 ? FontStyle.Bold : FontStyle.Regular);
        }

        // 内訳を入れる。合計を単価の欄に入れる（選んだとき）。
        private void EditBreakdown(Row row)
        {
            List<BreakdownLine>? current;
            _breakdowns.TryGetValue(row.Pattern.Id, out current);
            var title = _answer.SupplierName + "　" + ExcelExports.PatternText(row.Pattern);
            using (var dlg = new BreakdownForm(title, current ?? new List<BreakdownLine>()))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                if (dlg.Lines.Count == 0)
                {
                    _breakdowns.Remove(row.Pattern.Id);
                }
                else
                {
                    _breakdowns[row.Pattern.Id] = dlg.Lines;
                    if (dlg.ApplySumToPrice)
                    {
                        row.Price.Text = dlg.Sum.ToString("0.####", CultureInfo.InvariantCulture);
                    }
                }
            }

            ShowBreakdownText(row);
        }

        private static Label Head(string text)
        {
            return new Label { Text = text, Width = 270, Height = 32, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("BIZ UDPGothic", 11F, FontStyle.Bold) };
        }

        private void AddLabeled(string caption, Control box, int x, int y, int width)
        {
            Controls.Add(new Label { Text = caption, Left = x, Top = y, Width = 440, Height = 24 });
            box.SetBounds(x, y + 28, width, 32);
            Controls.Add(box);
        }

        private void PickNewFile()
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "新しい見積書を選んでください";
                dlg.Filter = "見積書（PDF・Excelなど）|*.*";
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    NewFilePath = dlg.FileName;
                    _newFileLabel.Text = System.IO.Path.GetFileName(dlg.FileName);
                }
            }
        }

        private void Save()
        {
            var quotes = new List<Quote>();
            foreach (var r in _rows)
            {
                decimal? price = null;
                var priceText = r.Price.Text.Replace(",", "").Replace("¥", "").Trim();
                if (priceText.Length > 0)
                {
                    decimal p;
                    if (!decimal.TryParse(priceText, NumberStyles.Number, CultureInfo.InvariantCulture, out p) || p < 0)
                    {
                        MessageBox.Show(this, "単価は、0以上の数字で入力してください。\r\n（" + r.Pattern.Kind + "　" + r.Pattern.Quantity + r.Pattern.Unit + "）", "回答を入力", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    price = p;
                }

                int? lt = null;
                var ltText = r.LeadTime.Text.Replace(",", "").Trim();
                if (ltText.Length > 0)
                {
                    int d;
                    if (!int.TryParse(ltText, NumberStyles.Integer, CultureInfo.InvariantCulture, out d) || d < 0)
                    {
                        MessageBox.Show(this, "リードタイムは、0以上の整数（日数）で入力してください。\r\n（" + r.Pattern.Kind + "　" + r.Pattern.Quantity + r.Pattern.Unit + "）", "回答を入力", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    lt = d;
                }

                quotes.Add(new Quote { SupplierId = _answer.SupplierId, PatternId = r.Pattern.Id, UnitPrice = price, LeadTimeDays = lt });
            }

            _answer.SentAt = _sent.Checked ? _sent.Value.Date : (DateTime?)null;
            _answer.ReceivedAt = _received.Checked ? _received.Value.Date : (DateTime?)null;
            _answer.ExtraCost = _extra.Text;
            _answer.Relaxation = _relax.Text;
            _answer.Note = _note.Text;

            if (_requote)
            {
                // 出し直しは、旧版のPDFの移動とDBの保存を、呼び出し側が順に行う（内訳も、呼び出し側が保存する）。
                ResultQuotes.Clear();
                ResultQuotes.AddRange(quotes);
                DialogResult = DialogResult.OK;
                return;
            }

            try
            {
                _services.Db.SaveSupplierAnswer(_answer, quotes);
                foreach (var r in _rows)
                {
                    List<BreakdownLine>? lines;
                    _breakdowns.TryGetValue(r.Pattern.Id, out lines);
                    _services.Db.SaveBreakdown(_answer.AnkenId, _answer.SupplierId, r.Pattern.Id, lines ?? new List<BreakdownLine>());
                }

                DialogResult = DialogResult.OK;
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(this, ex.Message, "回答を入力", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
