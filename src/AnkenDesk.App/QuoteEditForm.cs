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
        }

        private readonly AppServices _services;
        private readonly AnkenSupplier _answer;
        private readonly List<Row> _rows = new List<Row>();
        private readonly DateTimePicker _sent = new DateTimePicker { Format = DateTimePickerFormat.Short, ShowCheckBox = true };
        private readonly DateTimePicker _received = new DateTimePicker { Format = DateTimePickerFormat.Short, ShowCheckBox = true };
        private readonly TextBox _extra = new TextBox();
        private readonly TextBox _relax = new TextBox();
        private readonly TextBox _note = new TextBox();

        public QuoteEditForm(AppServices services, AnkenSupplier answer, IReadOnlyList<QuantityPattern> patterns, IReadOnlyList<Quote> quotes)
        {
            _services = services;
            _answer = answer;
            UiStyle.Apply(this);
            Text = "回答を入力: " + answer.SupplierName;
            ClientSize = new Size(760, 640);

            Controls.Add(new Label
            {
                Text = answer.SupplierName + " の回答",
                Font = new Font("BIZ UDPGothic", 14F, FontStyle.Bold),
                Left = 16, Top = 12, Width = 720, Height = 36,
            });

            var table = new TableLayoutPanel { Left = 16, Top = 56, Width = 720, AutoSize = true, ColumnCount = 3 };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
            table.Controls.Add(Head("数量"));
            table.Controls.Add(Head("単価（円）"));
            table.Controls.Add(Head("リードタイム（日）"));
            foreach (var p in patterns)
            {
                var row = new Row { Pattern = p, Price = new TextBox { Width = 200 }, LeadTime = new TextBox { Width = 180 } };
                var q = quotes.FirstOrDefault(x => x.PatternId == p.Id && x.SupplierId == answer.SupplierId);
                if (q != null)
                {
                    row.Price.Text = q.UnitPrice.HasValue ? q.UnitPrice.Value.ToString("0.####", CultureInfo.InvariantCulture) : "";
                    row.LeadTime.Text = q.LeadTimeDays.HasValue ? q.LeadTimeDays.Value.ToString(CultureInfo.InvariantCulture) : "";
                }

                _rows.Add(row);
                table.Controls.Add(new Label { Text = p.Kind + "　" + p.Quantity.ToString("#,##0.####", CultureInfo.InvariantCulture) + p.Unit, Width = 270, Height = 32, TextAlign = ContentAlignment.MiddleLeft });
                table.Controls.Add(row.Price);
                table.Controls.Add(row.LeadTime);
            }

            Controls.Add(table);

            var y = 56 + 40 * (patterns.Count + 1) + 24;
            AddLabeled("依頼送付日", _sent, 16, y, 200);
            AddLabeled("回答受領日（入れると「回答済み」になります）", _received, 300, y, 200);
            AddLabeled("別費用（型・治具など）", _extra, 16, y + 70, 720);
            AddLabeled("緩和条件", _relax, 16, y + 140, 720);
            AddLabeled("備考", _note, 16, y + 210, 720);

            if (answer.SentAt.HasValue) { _sent.Value = answer.SentAt.Value; _sent.Checked = true; } else { _sent.Checked = false; }
            if (answer.ReceivedAt.HasValue) { _received.Value = answer.ReceivedAt.Value; _received.Checked = true; } else { _received.Checked = false; }
            _extra.Text = answer.ExtraCost;
            _relax.Text = answer.Relaxation;
            _note.Text = answer.Note;

            ClientSize = new Size(760, Math.Max(640, y + 210 + 70 + 70));

            var save = UiStyle.CreateButton("保存", true, 140);
            save.Left = 458; save.Top = ClientSize.Height - 56;
            save.Click += (s, e) => Save();
            var cancel = UiStyle.CreateButton("キャンセル", false, 140);
            cancel.Left = 604; cancel.Top = ClientSize.Height - 56;
            cancel.Click += (s, e) => DialogResult = DialogResult.Cancel;
            Controls.AddRange(new Control[] { save, cancel });
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

            try
            {
                _services.Db.SaveSupplierAnswer(_answer, quotes);
                DialogResult = DialogResult.OK;
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(this, ex.Message, "回答を入力", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
