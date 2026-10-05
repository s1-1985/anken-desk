using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using AnkenDesk.Core;

namespace AnkenDesk.App
{
    /// <summary>見積依頼書に載せる項目（材質・処理・荷姿…）の入力欄を、縦に並べたもの。既定値の編集と、案件の項目の編集で使う。</summary>
    internal sealed class ItemFieldsPanel : TableLayoutPanel
    {
        private static readonly string[] Multiline = { "検査内容", "備考" };

        private readonly Dictionary<string, TextBox> _boxes = new Dictionary<string, TextBox>();

        public ItemFieldsPanel()
        {
            ColumnCount = 2;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 520));
            foreach (var name in QuoteRequestItems.AllNames)
            {
                var tb = new TextBox { Width = 500, Margin = new Padding(0, 4, 0, 4) };
                if (Array.IndexOf(Multiline, name) >= 0)
                {
                    tb.Multiline = true;
                    tb.AcceptsReturn = true;
                    tb.Height = 64;
                    tb.ScrollBars = ScrollBars.Vertical;
                }

                _boxes[name] = tb;
                Controls.Add(new Label { Text = name, AutoSize = false, Width = 146, Height = 32, TextAlign = ContentAlignment.MiddleLeft });
                Controls.Add(tb);
            }
        }

        public void SetValues(IReadOnlyDictionary<string, string> values)
        {
            foreach (var kv in _boxes)
            {
                string v;
                kv.Value.Text = values.TryGetValue(kv.Key, out v) ? v.Replace("\r\n", "\n").Replace("\n", "\r\n") : "";
            }
        }

        public Dictionary<string, string> GetValues()
        {
            var d = new Dictionary<string, string>();
            foreach (var kv in _boxes)
            {
                d[kv.Key] = kv.Value.Text;
            }

            return d;
        }
    }
}
