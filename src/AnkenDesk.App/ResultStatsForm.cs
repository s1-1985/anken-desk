using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using AnkenDesk.Core;

namespace AnkenDesk.App
{
    /// <summary>勝敗・粗利の集計（得意先別・調達先別・月別・失注理由）。依頼日が期間内の案件を数える。</summary>
    internal sealed class ResultStatsForm : Form
    {
        private readonly AppServices _services;
        private readonly ComboBox _period = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly Label _overall = new Label();
        private readonly TabControl _tabs = new TabControl();
        private readonly ListView _client = NewRows("得意先");
        private readonly ListView _supplier = NewRows("調達先（受注で選んだ先）");
        private readonly ListView _month = NewRows("依頼月");
        private readonly ListView _reasons = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true };

        public ResultStatsForm(AppServices services)
        {
            _services = services;
            UiStyle.Apply(this);
            Text = "勝敗・粗利の集計";
            ClientSize = new Size(1040, 640);

            Controls.Add(new Label { Text = "勝敗・粗利の集計", Font = new Font("BIZ UDPGothic", 18F, FontStyle.Bold), Left = 16, Top = 10, Width = 500, Height = 40 });
            Controls.Add(new Label { Text = "期間（依頼日）", Left = 16, Top = 62, Width = 120, Height = 28 });
            _period.SetBounds(140, 58, 220, 32);
            _period.Items.AddRange(new object[] { "すべて", "今年", "昨年", "直近12か月" });
            _period.SelectedIndex = 0;
            _period.SelectedIndexChanged += (s, e) => Reload();
            Controls.Add(_period);

            _overall.SetBounds(16, 100, 1008, 56);
            _overall.Font = new Font("BIZ UDPGothic", 12F, FontStyle.Bold);
            Controls.Add(_overall);
            Controls.Add(new Label
            {
                Text = "受注率 = 受注 ÷（受注＋失注）。保留・進行中は含めません。粗利率は、提出単価と仕入単価（選んだ調達先）がそろった数量の平均です。",
                Left = 16, Top = 158, Width = 1008, Height = 26, ForeColor = Color.FromArgb(90, 96, 100),
            });

            _reasons.Columns.Add("失注・保留の理由", 420);
            _reasons.Columns.Add("件数", 100);
            AddTab("得意先別", _client);
            AddTab("調達先別", _supplier);
            AddTab("月別", _month);
            AddTab("失注・保留の理由", _reasons);
            _tabs.SetBounds(16, 190, 1008, 380);
            _tabs.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            Controls.Add(_tabs);

            var close = UiStyle.CreateButton("閉じる", false, 140);
            close.Left = 884; close.Top = 580;
            close.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            close.Click += (s, e) => Close();
            Controls.Add(close);
            CancelButton = close;
            Reload();
        }

        private void AddTab(string title, Control c)
        {
            var page = new TabPage(title);
            c.Dock = DockStyle.Fill;
            page.Controls.Add(c);
            _tabs.TabPages.Add(page);
        }

        private static ListView NewRows(string first)
        {
            var l = new ListView { View = View.Details, FullRowSelect = true, GridLines = true };
            l.Columns.Add(first, 300);
            l.Columns.Add("受注", 80);
            l.Columns.Add("失注", 80);
            l.Columns.Add("保留", 80);
            l.Columns.Add("進行中", 80);
            l.Columns.Add("受注率", 100);
            l.Columns.Add("平均粗利率", 120);
            return l;
        }

        private static string Pct(decimal? v)
        {
            return v.HasValue ? v.Value.ToString("0.0", CultureInfo.InvariantCulture) + "%" : "－";
        }

        private static void Fill(ListView l, IReadOnlyList<ResultStatRow> rows)
        {
            l.Items.Clear();
            foreach (var r in rows)
            {
                var item = new ListViewItem(r.Label);
                item.SubItems.Add(r.Won.ToString(CultureInfo.InvariantCulture));
                item.SubItems.Add(r.Lost.ToString(CultureInfo.InvariantCulture));
                item.SubItems.Add(r.OnHold.ToString(CultureInfo.InvariantCulture));
                item.SubItems.Add(r.Open.ToString(CultureInfo.InvariantCulture));
                item.SubItems.Add(Pct(r.WinRate));
                item.SubItems.Add(Pct(r.AverageMargin));
                l.Items.Add(item);
            }
        }

        private void Reload()
        {
            DateTime? from = null, to = null;
            var today = DateTime.Today;
            switch (_period.SelectedIndex)
            {
                case 1: from = new DateTime(today.Year, 1, 1); to = new DateTime(today.Year, 12, 31); break;
                case 2: from = new DateTime(today.Year - 1, 1, 1); to = new DateTime(today.Year - 1, 12, 31); break;
                case 3: from = today.AddMonths(-12); to = today; break;
            }

            var report = ResultStats.Compute(_services.Db, from, to);
            var o = report.Overall;
            _overall.Text = "受注 " + o.Won + "件　失注 " + o.Lost + "件　保留 " + o.OnHold + "件　進行中 " + o.Open + "件\r\n受注率 " + Pct(o.WinRate) + "　平均粗利率 " + Pct(o.AverageMargin);
            Fill(_client, report.ByClient);
            Fill(_supplier, report.BySupplier);
            Fill(_month, report.ByMonth);
            _reasons.Items.Clear();
            foreach (var kv in report.LossReasons)
            {
                var item = new ListViewItem(kv.Key);
                item.SubItems.Add(kv.Value.ToString(CultureInfo.InvariantCulture) + "件");
                _reasons.Items.Add(item);
            }
        }
    }
}
