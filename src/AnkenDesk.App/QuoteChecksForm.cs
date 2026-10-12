using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using AnkenDesk.Core;

namespace AnkenDesk.App
{
    /// <summary>比較表の自動チェックの結果。「確認」と「気づき」に分けて出す（機械的な検査なので、間違いとは限らない）。</summary>
    internal sealed class QuoteChecksForm : Form
    {
        public QuoteChecksForm(string title, IReadOnlyList<QuoteWarning> warnings)
        {
            UiStyle.Apply(this);
            Text = "比較表のチェック　" + title;
            ClientSize = new Size(900, 560);

            Controls.Add(new Label { Text = "比較表のチェック", Font = new Font("BIZ UDPGothic", 16F, FontStyle.Bold), Left = 16, Top = 10, Width = 500, Height = 38 });
            Controls.Add(new Label
            {
                Text = "入力ミス・見落としの目安を、機械的に探した結果です。間違いとは限りません（仕様の違いで差が出ていることもあります）。",
                Left = 16, Top = 50, Width = 868, Height = 26, ForeColor = Color.FromArgb(90, 96, 100),
            });

            var list = new ListView
            {
                Left = 16, Top = 84, Width = 868, Height = 400,
                View = View.Details, FullRowSelect = true, GridLines = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            };
            list.Columns.Add("種類", 90);
            list.Columns.Add("内容", 760);
            foreach (var w in warnings)
            {
                var item = new ListViewItem(w.Level == WarningLevel.Check ? "【確認】" : "気づき");
                item.SubItems.Add(w.Text);
                if (w.Level == WarningLevel.Check)
                {
                    item.Font = new Font("BIZ UDPGothic", 11F, FontStyle.Bold);
                }

                list.Items.Add(item);
            }

            Controls.Add(list);
            if (warnings.Count == 0)
            {
                Controls.Add(new Label { Text = "気になる点は見つかりませんでした。", Left = 16, Top = 494, Width = 600, Height = 30, Font = new Font("BIZ UDPGothic", 12F, FontStyle.Bold) });
            }

            var close = UiStyle.CreateButton("閉じる", false, 140);
            close.Left = 744; close.Top = 500;
            close.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            close.Click += (s, e) => Close();
            Controls.Add(close);
            CancelButton = close;
        }
    }
}
