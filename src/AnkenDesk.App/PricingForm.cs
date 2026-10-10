using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using AnkenDesk.Core;

namespace AnkenDesk.App
{
    /// <summary>
    /// 客先への提出単価と粗利。数量ごとに、仕入にする調達先（初期値は最安）を選び、提出単価を入れる。
    /// 提出単価の代わりに、掛け率（仕入への上乗せ%）か粗利率（%）を入れても、提出単価を計算する。
    /// </summary>
    internal sealed class PricingForm : Form
    {
        private sealed class Row
        {
            public QuantityPattern Pattern = null!;
            public ComboBox Supplier = null!;
            public Label Cost = null!;
            public TextBox Selling = null!;
            public TextBox Markup = null!;
            public TextBox GrossRate = null!;
            public Label Profit = null!;
        }

        private readonly AppServices _services;
        private readonly AnkenRecord _anken;
        private readonly IReadOnlyList<AnkenSupplier> _suppliers;
        private readonly IReadOnlyList<Quote> _quotes;
        private readonly List<Row> _rows = new List<Row>();
        private bool _busy;

        public PricingForm(AppServices services, AnkenRecord anken)
        {
            _services = services;
            _anken = anken;
            _suppliers = services.Db.ListAnkenSuppliers(anken.Id);
            _quotes = services.Db.ListQuotes(anken.Id);
            var patterns = services.Db.ListQuantities(anken.Id);
            var saved = services.Db.ListClientPrices(anken.Id);
            var cheapest = Comparison.CheapestQuotes(_quotes);

            UiStyle.Apply(this);
            Text = "提出単価と粗利　" + Home.Title(anken);
            ClientSize = new Size(1180, Math.Min(760, 300 + patterns.Count * 52));

            Controls.Add(new Label { Text = "提出単価と粗利", Font = new Font("BIZ UDPGothic", 18F, FontStyle.Bold), Left = 16, Top = 10, Width = 500, Height = 40 });
            Controls.Add(new Label
            {
                Text = "数量ごとに、仕入にする調達先（初期値は最安）と、客先への提出単価を入れます。掛け率（仕入への上乗せ%）か粗利率を入れても、提出単価が計算されます。\r\n"
                    + "粗利率 = 粗利 ÷ 提出単価。掛け率 = 粗利 ÷ 仕入。",
                Left = 16, Top = 52, Width = 1148, Height = 50, ForeColor = Color.FromArgb(90, 96, 100),
            });

            var heads = new[] { "数量", "仕入にする調達先", "仕入単価", "提出単価", "掛け率%", "粗利率%", "粗利（1個）" };
            var xs = new[] { 16, 200, 470, 600, 740, 850, 960 };
            for (var i = 0; i < heads.Length; i++)
            {
                var width = i + 1 < xs.Length ? xs[i + 1] - xs[i] - 4 : 200;
                Controls.Add(new Label { Text = heads[i], Left = xs[i], Top = 110, Width = width, Height = 26, Font = new Font("BIZ UDPGothic", 10.5F, FontStyle.Bold) });
            }

            var y = 142;
            foreach (var p in patterns)
            {
                var row = new Row { Pattern = p };
                var sv = saved.FirstOrDefault(x => x.PatternId == p.Id);
                long? chosen = sv != null && sv.AdoptedSupplierId.HasValue
                    ? sv.AdoptedSupplierId
                    : cheapest.Where(c => c.PatternId == p.Id).Select(c => (long?)c.SupplierId).FirstOrDefault();

                Controls.Add(new Label { Text = ExcelExports.PatternText(p), Left = xs[0], Top = y + 4, Width = 180, Height = 30 });
                row.Supplier = new ComboBox { Left = xs[1], Top = y, Width = 260, DropDownStyle = ComboBoxStyle.DropDownList };
                row.Supplier.Items.Add("（決めていない）");
                var index = 0;
                for (var i = 0; i < _suppliers.Count; i++)
                {
                    row.Supplier.Items.Add(_suppliers[i].SupplierName);
                    if (chosen == _suppliers[i].SupplierId)
                    {
                        index = i + 1;
                    }
                }

                row.Supplier.SelectedIndex = index;
                row.Cost = new Label { Left = xs[2], Top = y + 4, Width = 120, Height = 30, Font = new Font("BIZ UDPGothic", 11F, FontStyle.Bold) };
                row.Selling = new TextBox { Left = xs[3], Top = y, Width = 120, TextAlign = HorizontalAlignment.Right };
                row.Markup = new TextBox { Left = xs[4], Top = y, Width = 90, TextAlign = HorizontalAlignment.Right };
                row.GrossRate = new TextBox { Left = xs[5], Top = y, Width = 90, TextAlign = HorizontalAlignment.Right };
                row.Profit = new Label { Left = xs[6], Top = y + 4, Width = 200, Height = 30, Font = new Font("BIZ UDPGothic", 11F, FontStyle.Bold) };
                if (sv != null && sv.SellingPrice.HasValue)
                {
                    row.Selling.Text = sv.SellingPrice.Value.ToString("0.####", CultureInfo.InvariantCulture);
                }

                var r = row;
                row.Supplier.SelectedIndexChanged += (s, e) => FromSelling(r);
                row.Selling.TextChanged += (s, e) => { if (!_busy) FromSelling(r); };
                row.Markup.TextChanged += (s, e) => { if (!_busy) FromMarkup(r); };
                row.GrossRate.TextChanged += (s, e) => { if (!_busy) FromGrossRate(r); };
                Controls.AddRange(new Control[] { row.Supplier, row.Cost, row.Selling, row.Markup, row.GrossRate, row.Profit });
                _rows.Add(row);
                y += 52;
            }

            // 全部の数量に、同じ掛け率を入れる
            Controls.Add(new Label { Text = "全部の数量に、同じ掛け率(%)を入れる", Left = 16, Top = y + 14, Width = 360, Height = 28 });
            var all = new TextBox { Left = 380, Top = y + 10, Width = 90, TextAlign = HorizontalAlignment.Right };
            var apply = UiStyle.CreateButton("入れる", false, 120);
            apply.Left = 480; apply.Top = y + 6;
            apply.Click += (s, e) =>
            {
                decimal m;
                if (!TryNum(all.Text, out m))
                {
                    MessageBox.Show(this, "掛け率は、数字で入れてください。", "提出単価と粗利", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                foreach (var r in _rows)
                {
                    _busy = true;
                    r.Markup.Text = m.ToString("0.####", CultureInfo.InvariantCulture);
                    _busy = false;
                    FromMarkup(r);
                }
            };
            Controls.AddRange(new Control[] { all, apply });

            var save = UiStyle.CreateButton("保存", true, 140);
            save.Left = 880; save.Top = ClientSize.Height - 56;
            save.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            save.Click += (s, e) => Save();
            var cancel = UiStyle.CreateButton("キャンセル", false, 140);
            cancel.Left = 1026; cancel.Top = ClientSize.Height - 56;
            cancel.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            cancel.Click += (s, e) => DialogResult = DialogResult.Cancel;
            Controls.AddRange(new Control[] { save, cancel });
            CancelButton = cancel;

            foreach (var r in _rows)
            {
                FromSelling(r);
            }
        }

        private static bool TryNum(string text, out decimal value)
        {
            return decimal.TryParse(text.Replace(",", "").Replace("¥", "").Replace("%", "").Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out value);
        }

        private decimal? CostOf(Row r)
        {
            if (r.Supplier.SelectedIndex <= 0)
            {
                return null;
            }

            var sid = _suppliers[r.Supplier.SelectedIndex - 1].SupplierId;
            var q = _quotes.FirstOrDefault(x => x.SupplierId == sid && x.PatternId == r.Pattern.Id);
            return q == null ? null : q.UnitPrice;
        }

        // 提出単価が元: 仕入単価・掛け率・粗利率・粗利を出す。
        private void FromSelling(Row r)
        {
            _busy = true;
            try
            {
                var cost = CostOf(r);
                r.Cost.Text = cost.HasValue ? Comparison.FormatPrice(cost.Value) : "－";
                decimal selling;
                if (!cost.HasValue || !TryNum(r.Selling.Text, out selling))
                {
                    r.Markup.Text = "";
                    r.GrossRate.Text = "";
                    r.Profit.Text = cost.HasValue ? "" : "仕入単価がありません";
                    return;
                }

                var markup = AnkenDesk.Core.Margin.MarkupPercent(selling, cost.Value);
                var rate = AnkenDesk.Core.Margin.GrossMarginPercent(selling, cost.Value);
                r.Markup.Text = markup.HasValue ? markup.Value.ToString("0.0", CultureInfo.InvariantCulture) : "";
                r.GrossRate.Text = rate.HasValue ? rate.Value.ToString("0.0", CultureInfo.InvariantCulture) : "";
                var profit = AnkenDesk.Core.Margin.GrossProfit(selling, cost.Value);
                r.Profit.Text = Comparison.FormatPrice(profit) + (profit < 0 ? "　赤字" : "");
                r.Profit.ForeColor = profit < 0 ? UiStyle.Danger : UiStyle.Text;
            }
            finally
            {
                _busy = false;
            }
        }

        private void FromMarkup(Row r)
        {
            var cost = CostOf(r);
            decimal m;
            if (!cost.HasValue || !TryNum(r.Markup.Text, out m))
            {
                return;
            }

            _busy = true;
            r.Selling.Text = AnkenDesk.Core.Margin.SellingFromMarkup(cost.Value, m).ToString("0.####", CultureInfo.InvariantCulture);
            _busy = false;
            FromSelling(r);
        }

        private void FromGrossRate(Row r)
        {
            var cost = CostOf(r);
            decimal m;
            if (!cost.HasValue || !TryNum(r.GrossRate.Text, out m))
            {
                return;
            }

            var selling = AnkenDesk.Core.Margin.SellingFromGrossMargin(cost.Value, m);
            if (!selling.HasValue)
            {
                return;
            }

            _busy = true;
            r.Selling.Text = selling.Value.ToString("0.####", CultureInfo.InvariantCulture);
            _busy = false;
            FromSelling(r);
        }

        private void Save()
        {
            var prices = new List<ClientPrice>();
            foreach (var r in _rows)
            {
                decimal? selling = null;
                if (r.Selling.Text.Trim().Length > 0)
                {
                    decimal v;
                    if (!TryNum(r.Selling.Text, out v) || v < 0)
                    {
                        MessageBox.Show(this, "提出単価は、0以上の数字で入れてください。\r\n（" + ExcelExports.PatternText(r.Pattern) + "）", "提出単価と粗利", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    selling = v;
                }

                prices.Add(new ClientPrice
                {
                    PatternId = r.Pattern.Id,
                    SellingPrice = selling,
                    AdoptedSupplierId = r.Supplier.SelectedIndex > 0 ? _suppliers[r.Supplier.SelectedIndex - 1].SupplierId : (long?)null,
                });
            }

            _services.Db.SaveClientPrices(_anken.Id, prices);
            DialogResult = DialogResult.OK;
        }
    }
}
