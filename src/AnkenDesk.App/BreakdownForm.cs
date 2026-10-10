using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using AnkenDesk.Core;

namespace AnkenDesk.App
{
    /// <summary>1つの数量・1社の見積の内訳（材料費・加工費・処理費など）を入れる。項目名は選ぶことも、自由に書くこともできる。</summary>
    internal sealed class BreakdownForm : Form
    {
        private static readonly string[] CommonItems =
        {
            "材料費", "加工費", "表面処理費", "熱処理費", "検査費", "梱包・運搬費", "型・治具費（償却）", "諸経費", "その他",
        };

        private sealed class Row
        {
            public Panel Panel = null!;
            public ComboBox Item = null!;
            public TextBox Amount = null!;
        }

        private readonly List<Row> _rows = new List<Row>();
        private readonly FlowLayoutPanel _panel = new FlowLayoutPanel();
        private readonly Label _sum = new Label();
        private readonly CheckBox _apply = new CheckBox();

        /// <summary>入力した内訳（空の行を除く）。</summary>
        public List<BreakdownLine> Lines { get; } = new List<BreakdownLine>();

        public decimal Sum { get; private set; }

        /// <summary>合計を単価の欄に入れるか。</summary>
        public bool ApplySumToPrice
        {
            get { return _apply.Checked; }
        }

        public BreakdownForm(string title, IEnumerable<BreakdownLine> initial)
        {
            UiStyle.Apply(this);
            Text = "見積の内訳";
            ClientSize = new Size(640, 560);

            Controls.Add(new Label { Text = "見積の内訳", Font = new Font("BIZ UDPGothic", 14F, FontStyle.Bold), Left = 16, Top = 10, Width = 600, Height = 34 });
            Controls.Add(new Label { Text = title, Left = 16, Top = 46, Width = 608, Height = 26, ForeColor = Color.FromArgb(90, 96, 100) });

            _panel.SetBounds(16, 80, 608, 330);
            _panel.FlowDirection = FlowDirection.TopDown;
            _panel.WrapContents = false;
            _panel.AutoScroll = true;
            _panel.BackColor = Color.White;
            Controls.Add(_panel);

            var add = UiStyle.CreateButton("行を追加", false, 140);
            add.Left = 16; add.Top = 418;
            add.Click += (s, e) => AddRow("", "");
            Controls.Add(add);

            _sum.SetBounds(170, 424, 454, 30);
            _sum.Font = new Font("BIZ UDPGothic", 12F, FontStyle.Bold);
            _sum.TextAlign = ContentAlignment.MiddleRight;
            Controls.Add(_sum);

            _apply.SetBounds(16, 466, 608, 28);
            _apply.Text = "保存したら、合計を単価の欄に入れる";
            _apply.Checked = true;
            Controls.Add(_apply);

            var ok = UiStyle.CreateButton("保存", true, 140);
            ok.Left = 340; ok.Top = 504;
            ok.Click += (s, e) => Accept();
            var cancel = UiStyle.CreateButton("キャンセル", false, 140);
            cancel.Left = 486; cancel.Top = 504;
            cancel.Click += (s, e) => DialogResult = DialogResult.Cancel;
            Controls.AddRange(new Control[] { ok, cancel });
            AcceptButton = ok;
            CancelButton = cancel;

            foreach (var l in initial)
            {
                AddRow(l.Item, l.Amount.ToString("0.####", CultureInfo.InvariantCulture));
            }

            if (_rows.Count == 0)
            {
                AddRow("材料費", "");
                AddRow("加工費", "");
            }

            UpdateSum();
        }

        private void AddRow(string item, string amount)
        {
            var row = new Row { Panel = new Panel { Width = 580, Height = 44 } };
            row.Item = new ComboBox { Left = 4, Top = 6, Width = 260, DropDownStyle = ComboBoxStyle.DropDown, Text = item };
            row.Item.Items.AddRange(CommonItems);
            row.Amount = new TextBox { Left = 276, Top = 6, Width = 150, TextAlign = HorizontalAlignment.Right, Text = amount };
            row.Amount.TextChanged += (s, e) => UpdateSum();
            var del = UiStyle.CreateButton("削除", false, 90);
            del.Left = 440; del.Top = 2;
            del.Click += (s, e) =>
            {
                _rows.Remove(row);
                _panel.Controls.Remove(row.Panel);
                row.Panel.Dispose();
                UpdateSum();
            };
            row.Panel.Controls.AddRange(new Control[] { row.Item, row.Amount, del });
            _rows.Add(row);
            _panel.Controls.Add(row.Panel);
        }

        private static bool TryAmount(string text, out decimal value)
        {
            return decimal.TryParse(text.Replace(",", "").Replace("¥", "").Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out value);
        }

        private void UpdateSum()
        {
            decimal total = 0;
            foreach (var r in _rows)
            {
                decimal v;
                if (TryAmount(r.Amount.Text, out v))
                {
                    total += v;
                }
            }

            _sum.Text = "内訳の合計　" + Comparison.FormatPrice(total);
        }

        private void Accept()
        {
            Lines.Clear();
            decimal total = 0;
            foreach (var r in _rows)
            {
                var name = r.Item.Text.Trim();
                var amountText = r.Amount.Text.Trim();
                if (name.Length == 0 && amountText.Length == 0)
                {
                    continue; // 空の行は無視
                }

                decimal v;
                if (name.Length == 0 || !TryAmount(amountText, out v))
                {
                    MessageBox.Show(this, "項目名と金額（数字）を入れてください。\r\n（" + (name.Length == 0 ? "項目名が空です" : "「" + name + "」の金額") + "）",
                        "見積の内訳", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                Lines.Add(new BreakdownLine { Item = name, Amount = v });
                total += v;
            }

            Sum = total;
            DialogResult = DialogResult.OK;
        }
    }
}
