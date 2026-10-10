using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using AnkenDesk.Core;

namespace AnkenDesk.App
{
    /// <summary>調達先ごとの実績（全案件を通した集計）。調達先を選ぶときの目安にする。</summary>
    internal sealed class SupplierStatsForm : Form
    {
        public SupplierStatsForm(AppServices services)
        {
            UiStyle.Apply(this);
            Text = "調達先の実績";
            ClientSize = new Size(1040, 560);

            Controls.Add(new Label
            {
                Text = "調達先の実績",
                Font = new Font("BIZ UDPGothic", 18F, FontStyle.Bold),
                Left = 16, Top = 10, Width = 500, Height = 40,
            });
            Controls.Add(new Label
            {
                Text = "登録した全案件の記録から数えています。回答までの日数は、依頼送付日と回答受領日の両方がある案件だけで平均します。\r\n「最安」は、2社以上が単価を出した数量で、いちばん安かった案件の数です（同額を含む）。件数が少ないうちは、目安として見てください。",
                Left = 16, Top = 50, Width = 1008, Height = 54,
                ForeColor = Color.FromArgb(90, 96, 100),
            });

            var list = new ListView
            {
                Left = 16, Top = 110, Width = 1008, Height = 390,
                View = View.Details, FullRowSelect = true, GridLines = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            };
            list.Columns.Add("調達先", 300);
            list.Columns.Add("略称", 90);
            list.Columns.Add("案件数", 90);
            list.Columns.Add("依頼済み", 100);
            list.Columns.Add("回答", 90);
            list.Columns.Add("回答率", 100);
            list.Columns.Add("回答までの平均日数", 160);
            list.Columns.Add("最安の案件", 100);
            Controls.Add(list);

            var stats = SupplierStats.Compute(services.Db.ListAllAnkenSuppliers(), id => services.Db.ListQuotes(id));
            foreach (var s in stats)
            {
                var item = new ListViewItem(s.Name);
                item.SubItems.Add(s.ShortName);
                item.SubItems.Add(s.Ankens.ToString(CultureInfo.InvariantCulture));
                item.SubItems.Add(s.Requested.ToString(CultureInfo.InvariantCulture));
                item.SubItems.Add(s.Answered.ToString(CultureInfo.InvariantCulture));
                item.SubItems.Add(s.AnswerRate.HasValue ? (s.AnswerRate.Value * 100).ToString("0", CultureInfo.InvariantCulture) + "%" : "－");
                item.SubItems.Add(s.AverageReplyDays.HasValue ? s.AverageReplyDays.Value.ToString("0.0", CultureInfo.InvariantCulture) + "日" : "－");
                item.SubItems.Add(s.CheapestAnkens.ToString(CultureInfo.InvariantCulture));
                list.Items.Add(item);
            }

            if (stats.Count == 0)
            {
                Controls.Add(new Label { Text = "まだ記録がありません。", Left = 16, Top = 510, Width = 600, Height = 30 });
            }

            var close = UiStyle.CreateButton("閉じる", false, 140);
            close.Left = 884; close.Top = 508;
            close.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            close.Click += (s, e) => Close();
            Controls.Add(close);
            CancelButton = close;
        }
    }
}
