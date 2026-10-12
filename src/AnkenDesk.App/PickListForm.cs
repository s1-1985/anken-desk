using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AnkenDesk.App
{
    /// <summary>一覧から1つ選ぶ小さな画面（外した調達先を戻す、DBの控えを選ぶ、など）。</summary>
    internal sealed class PickListForm : Form
    {
        private readonly ListBox _list = new ListBox();

        /// <summary>選ばれた行の番号。選ばれなければ -1。</summary>
        public int SelectedIndexResult { get; private set; } = -1;

        public PickListForm(string title, string prompt, IReadOnlyList<string> items, string okText)
        {
            UiStyle.Apply(this);
            Text = title;
            ClientSize = new Size(640, 480);

            Controls.Add(new Label { Text = prompt, Left = 16, Top = 12, Width = 608, Height = 50 });
            _list.SetBounds(16, 66, 608, 336);
            _list.IntegralHeight = false;
            foreach (var i in items)
            {
                _list.Items.Add(i);
            }

            if (_list.Items.Count > 0)
            {
                _list.SelectedIndex = 0;
            }

            _list.DoubleClick += (s, e) => Accept();
            Controls.Add(_list);

            var ok = UiStyle.CreateButton(okText, true, 200);
            ok.Left = 276; ok.Top = 420;
            ok.Click += (s, e) => Accept();
            var cancel = UiStyle.CreateButton("キャンセル", false, 130);
            cancel.Left = 494; cancel.Top = 420;
            cancel.Click += (s, e) => DialogResult = DialogResult.Cancel;
            Controls.AddRange(new Control[] { ok, cancel });
            AcceptButton = ok;
            CancelButton = cancel;
        }

        private void Accept()
        {
            if (_list.SelectedIndex < 0)
            {
                return;
            }

            SelectedIndexResult = _list.SelectedIndex;
            DialogResult = DialogResult.OK;
        }
    }
}
