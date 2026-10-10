using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using AnkenDesk.Core;

namespace AnkenDesk.App
{
    /// <summary>過去の類似案件（同じ品番・近い品番）と、そのときの調達先ごとの単価。今回の単価との差も出す。AIは使わず、DBの品番を比べるだけ。</summary>
    internal sealed class SimilarForm : Form
    {
        private readonly AppServices _services;
        private readonly IReadOnlyList<SimilarAnken> _found;
        private readonly ListView _ankens = new ListView();
        private readonly ListView _prices = new ListView();
        private readonly Label _hint = new Label();

        public SimilarForm(AppServices services, AnkenRecord anken)
        {
            _services = services;
            UiStyle.Apply(this);
            Text = "過去の類似案件　" + Home.Title(anken);
            ClientSize = new Size(1180, 720);

            Controls.Add(new Label { Text = "過去の類似案件", Font = new Font("BIZ UDPGothic", 18F, FontStyle.Bold), Left = 16, Top = 10, Width = 500, Height = 40 });
            Controls.Add(new Label
            {
                Text = "品番「" + anken.PartNumber + "」と、同じ・近い品番の過去の案件です（空白やハイフンの違いは無視）。案件を選ぶと、そのときの単価が下に出ます。",
                Left = 16, Top = 52, Width = 1148, Height = 28, ForeColor = Color.FromArgb(90, 96, 100),
            });

            _found = SimilarAnkens.Find(services.Db, anken);

            _ankens.SetBounds(16, 88, 1148, 250);
            _ankens.View = View.Details;
            _ankens.FullRowSelect = true;
            _ankens.HideSelection = false;
            _ankens.MultiSelect = false;
            _ankens.Columns.Add("近さ", 130);
            _ankens.Columns.Add("依頼日", 110);
            _ankens.Columns.Add("得意先・種別", 220);
            _ankens.Columns.Add("品番", 200);
            _ankens.Columns.Add("備考", 200);
            _ankens.Columns.Add("状態", 120);
            _ankens.Columns.Add("単価の数", 90);
            _ankens.SelectedIndexChanged += (s, e) => ShowPrices();
            _ankens.DoubleClick += (s, e) => OpenSelected();
            Controls.Add(_ankens);

            foreach (var f in _found)
            {
                var item = new ListViewItem(KindText(f.Kind));
                item.SubItems.Add(f.Anken.RequestDate.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture));
                item.SubItems.Add(f.Anken.ClientName);
                item.SubItems.Add(f.Anken.PartNumber);
                item.SubItems.Add(f.Anken.Note);
                item.SubItems.Add(f.Anken.Status);
                item.SubItems.Add(f.Prices.Count.ToString(CultureInfo.InvariantCulture));
                _ankens.Items.Add(item);
            }

            Controls.Add(new Label { Text = "そのときの単価（仕入）", Font = new Font("BIZ UDPGothic", 12F, FontStyle.Bold), Left = 16, Top = 350, Width = 500, Height = 28 });
            _prices.SetBounds(16, 382, 1148, 270);
            _prices.View = View.Details;
            _prices.FullRowSelect = true;
            _prices.Columns.Add("調達先", 240);
            _prices.Columns.Add("数量", 200);
            _prices.Columns.Add("単価", 130);
            _prices.Columns.Add("リードタイム", 110);
            _prices.Columns.Add("今回の単価", 130);
            _prices.Columns.Add("今回との差", 200);
            Controls.Add(_prices);

            _hint.SetBounds(16, 660, 700, 30);
            Controls.Add(_hint);

            var open = UiStyle.CreateButton("この案件を開く", true, 200);
            open.Left = 760; open.Top = 664;
            open.Click += (s, e) => OpenSelected();
            var close = UiStyle.CreateButton("閉じる", false, 140);
            close.Left = 1024; close.Top = 664;
            close.Click += (s, e) => Close();
            Controls.AddRange(new Control[] { open, close });
            CancelButton = close;

            if (_found.Count == 0)
            {
                _hint.Text = "類似の過去案件はありません。";
            }
            else
            {
                _ankens.Items[0].Selected = true;
            }
        }

        private static string KindText(SimilarKind k)
        {
            return k == SimilarKind.Same ? "同じ品番" : k == SimilarKind.Contains ? "品番を含む" : "先頭が同じ";
        }

        private void ShowPrices()
        {
            _prices.Items.Clear();
            if (_ankens.SelectedIndices.Count == 0)
            {
                return;
            }

            var f = _found[_ankens.SelectedIndices[0]];
            foreach (var p in f.Prices)
            {
                var item = new ListViewItem(p.SupplierName);
                item.SubItems.Add(p.PatternText);
                item.SubItems.Add(Comparison.FormatPrice(p.Price));
                item.SubItems.Add(p.LeadTimeDays.HasValue ? p.LeadTimeDays.Value + "日" : "");
                item.SubItems.Add(p.CurrentPrice.HasValue ? Comparison.FormatPrice(p.CurrentPrice.Value) : "－");
                item.SubItems.Add(DiffText(p));
                if (p.DiffPercent.HasValue && Math.Abs(p.DiffPercent.Value) >= 10)
                {
                    item.ForeColor = UiStyle.Danger; // 色に加えて、文字（「高い」「安い」）でも示す
                }

                _prices.Items.Add(item);
            }

            _hint.Text = f.Prices.Count == 0 ? "この案件には、単価の記録がありません。" : "今回との差が10%以上のものは、赤字で示します。";
        }

        private static string DiffText(SimilarPrice p)
        {
            if (!p.DiffPercent.HasValue)
            {
                return "－";
            }

            var v = p.DiffPercent.Value;
            return (v > 0 ? "+" : "") + v.ToString("0.0", CultureInfo.InvariantCulture) + "%" + (v >= 10 ? "　高い" : v <= -10 ? "　安い" : "");
        }

        private void OpenSelected()
        {
            if (_ankens.SelectedIndices.Count == 0)
            {
                return;
            }

            var f = _found[_ankens.SelectedIndices[0]];
            using (var dlg = new AnkenDetailForm(_services, f.Anken.Id))
            {
                dlg.ShowDialog(this);
            }
        }
    }
}
