using System;
using System.Drawing;
using System.Windows.Forms;
using AnkenDesk.Core;

namespace AnkenDesk.App
{
    /// <summary>見積依頼書に載せる項目の既定値。案件登録の入力欄に、最初から入れておく文章。</summary>
    internal sealed class ItemDefaultsForm : Form
    {
        private readonly AppServices _services;
        private readonly ItemFieldsPanel _fields = new ItemFieldsPanel();

        public ItemDefaultsForm(AppServices services)
        {
            _services = services;
            UiStyle.Apply(this);
            Text = "見積依頼書の既定値";
            var h = Math.Min(900, Screen.PrimaryScreen.WorkingArea.Height - 60);
            ClientSize = new Size(740, h);

            Controls.Add(new Label
            {
                Text = "案件を登録するとき、各項目に最初から入れておく文章です。\r\n変えても、すでに登録した案件は変わりません。",
                Left = 16, Top = 12, Width = 700, Height = 52,
            });

            var scroll = new Panel { Left = 16, Top = 72, Width = 708, Height = h - 72 - 72, AutoScroll = true, BackColor = Color.White };
            scroll.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            _fields.Left = 8;
            _fields.Top = 8;
            scroll.Controls.Add(_fields);
            Controls.Add(scroll);

            var reset = UiStyle.CreateButton("見積依頼書のもとの文章に戻す", false, 280);
            reset.Left = 16; reset.Top = h - 56;
            reset.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            reset.Click += (s, e) =>
            {
                var ok = MessageBox.Show(this, "変えた既定値を、すべてもとの文章に戻します。", "既定値を戻す",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
                if (ok == DialogResult.Yes)
                {
                    _services.Db.ResetItemDefaults();
                    _fields.SetValues(_services.Db.GetItemDefaults());
                }
            };

            var save = UiStyle.CreateButton("保存", true, 130);
            save.Left = 458; save.Top = h - 56;
            save.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            save.Click += (s, e) =>
            {
                foreach (var kv in _fields.GetValues())
                {
                    _services.Db.SetItemDefault(kv.Key, kv.Value);
                }

                DialogResult = DialogResult.OK;
            };

            var cancel = UiStyle.CreateButton("キャンセル", false, 130);
            cancel.Left = 594; cancel.Top = h - 56;
            cancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            cancel.Click += (s, e) => DialogResult = DialogResult.Cancel;

            Controls.AddRange(new Control[] { reset, save, cancel });
            _fields.SetValues(_services.Db.GetItemDefaults());
        }
    }
}
