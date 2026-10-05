using System;
using System.Drawing;
using System.Windows.Forms;
using AnkenDesk.Core;

namespace AnkenDesk.App
{
    /// <summary>登録済みの案件の、品名・回答希望期日・見積依頼書に載せる項目を直す。品番・依頼日・数量は直せない（フォルダ名に関わるため）。</summary>
    internal sealed class AnkenEditForm : Form
    {
        private readonly AppServices _services;
        private readonly long _ankenId;
        private readonly TextBox _partName = new TextBox();
        private readonly DateTimePicker _due = new DateTimePicker { Format = DateTimePickerFormat.Short };
        private readonly ItemFieldsPanel _fields = new ItemFieldsPanel();

        public AnkenEditForm(AppServices services, long ankenId)
        {
            _services = services;
            _ankenId = ankenId;
            var input = services.Db.LoadAnkenInput(ankenId);

            UiStyle.Apply(this);
            Text = "案件の項目を編集";
            var h = Math.Min(900, Screen.PrimaryScreen.WorkingArea.Height - 60);
            ClientSize = new Size(740, h);

            Controls.Add(new Label { Text = "品番 " + input.PartNumber + "（品番・依頼日・数量は、ここでは直せません）", Left = 16, Top = 12, Width = 700, Height = 28 });
            Controls.Add(new Label { Text = "品名", Left = 16, Top = 48, Width = 300, Height = 22 });
            _partName.SetBounds(16, 72, 400, 32);
            _partName.Text = input.PartName;
            Controls.Add(_partName);
            Controls.Add(new Label { Text = "回答希望期日", Left = 440, Top = 48, Width = 200, Height = 22 });
            _due.SetBounds(440, 72, 200, 32);
            _due.Value = input.ReplyDueDate;
            Controls.Add(_due);

            Controls.Add(new Label
            {
                Text = "見積依頼書に載せる項目",
                Left = 16, Top = 116, Width = 400, Height = 28,
                Font = new Font("BIZ UDPGothic", 12F, FontStyle.Bold),
            });
            var scroll = new Panel { Left = 16, Top = 148, Width = 708, Height = h - 148 - 72, AutoScroll = true, BackColor = Color.White };
            scroll.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            _fields.Left = 8;
            _fields.Top = 8;
            scroll.Controls.Add(_fields);
            Controls.Add(scroll);
            _fields.SetValues(input.Items);

            var save = UiStyle.CreateButton("保存", true, 130);
            save.Left = 458; save.Top = h - 56;
            save.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            save.Click += (s, e) => Save();
            var cancel = UiStyle.CreateButton("キャンセル", false, 130);
            cancel.Left = 594; cancel.Top = h - 56;
            cancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            cancel.Click += (s, e) => DialogResult = DialogResult.Cancel;
            Controls.AddRange(new Control[] { save, cancel });
        }

        private void Save()
        {
            try
            {
                _services.Db.UpdateAnkenDetails(_ankenId, _partName.Text, _due.Value.Date, _fields.GetValues());
                DialogResult = DialogResult.OK;
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(this, ex.Message, "案件の項目を編集", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
