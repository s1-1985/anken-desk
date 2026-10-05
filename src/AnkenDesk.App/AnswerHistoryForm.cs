using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using AnkenDesk.Core;

namespace AnkenDesk.App
{
    /// <summary>出し直し前の回答（旧版）を見る。読むだけで、変更はできない。</summary>
    internal sealed class AnswerHistoryForm : Form
    {
        private readonly IReadOnlyList<AnswerVersion> _versions;
        private readonly IReadOnlyList<QuantityPattern> _patterns;
        private readonly ListBox _list = new ListBox();
        private readonly TextBox _detail = new TextBox();

        public AnswerHistoryForm(string supplierName, IReadOnlyList<AnswerVersion> versions, IReadOnlyList<QuantityPattern> patterns)
        {
            _versions = versions;
            _patterns = patterns;
            UiStyle.Apply(this);
            Text = "旧版の履歴: " + supplierName;
            ClientSize = new Size(900, 560);

            Controls.Add(new Label { Text = supplierName + " の旧版（出し直し前の回答）", Left = 16, Top = 12, Width = 860, Height = 30, Font = new Font("BIZ UDPGothic", 13F, FontStyle.Bold) });

            _list.SetBounds(16, 52, 260, 440);
            _list.IntegralHeight = false;
            foreach (var v in versions)
            {
                _list.Items.Add("版" + v.Version + "　" + (v.ReceivedAt.HasValue ? v.ReceivedAt.Value.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) + " 受領" : "受領日なし"));
            }

            _list.SelectedIndexChanged += (s, e) => ShowSelected();

            _detail.SetBounds(292, 52, 592, 440);
            _detail.Multiline = true;
            _detail.ReadOnly = true;
            _detail.ScrollBars = ScrollBars.Vertical;

            var close = UiStyle.CreateButton("閉じる", false, 120);
            close.Left = 764; close.Top = 504;
            close.Click += (s, e) => Close();

            Controls.AddRange(new Control[] { _list, _detail, close });
            if (_list.Items.Count > 0)
            {
                _list.SelectedIndex = 0;
            }
            else
            {
                _detail.Text = "旧版はありません。";
            }
        }

        private void ShowSelected()
        {
            if (_list.SelectedIndex < 0)
            {
                return;
            }

            var v = _versions[_list.SelectedIndex];
            var sb = new StringBuilder();
            sb.AppendLine("版" + v.Version + "（" + v.ArchivedAt.ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture) + " に旧版にしました）");
            sb.AppendLine("依頼送付日: " + Date(v.SentAt) + "　回答受領日: " + Date(v.ReceivedAt));
            sb.AppendLine();
            sb.AppendLine("【数量ごとの単価】");
            foreach (var p in _patterns)
            {
                var q = v.Quotes.FirstOrDefault(x => x.PatternId == p.Id);
                var price = q != null && q.UnitPrice.HasValue ? Comparison.FormatPrice(q.UnitPrice.Value) : "—";
                var lt = q != null && q.LeadTimeDays.HasValue ? "LT " + q.LeadTimeDays.Value + "日" : "LT —";
                sb.AppendLine("　" + p.Kind + "　" + p.Quantity.ToString("#,##0.####", CultureInfo.InvariantCulture) + p.Unit + "：" + price + "　" + lt);
            }

            sb.AppendLine();
            sb.AppendLine("別費用: " + Dash(v.ExtraCost));
            sb.AppendLine("緩和条件: " + Dash(v.Relaxation));
            sb.AppendLine("備考: " + Dash(v.Note));
            sb.AppendLine();
            sb.AppendLine("【旧版フォルダへ移した見積書】");
            sb.AppendLine(string.IsNullOrEmpty(v.Files) ? "なし" : v.Files.Replace("\n", "\r\n"));
            _detail.Text = sb.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n");
        }

        private static string Date(DateTime? d)
        {
            return d.HasValue ? d.Value.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) : "—";
        }

        private static string Dash(string s)
        {
            return string.IsNullOrWhiteSpace(s) ? "—" : s;
        }
    }
}
