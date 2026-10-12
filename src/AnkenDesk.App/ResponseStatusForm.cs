using System;
using System.Drawing;
using System.Windows.Forms;
using AnkenDesk.Core;

namespace AnkenDesk.App
{
    /// <summary>調達先の返事の状況を入れる: 辞退（見積を出さない）／検討中（回答予定日つき）／未回答に戻す。</summary>
    internal sealed class ResponseStatusForm : Form
    {
        private readonly AppServices _services;
        private readonly AnkenSupplier _s;
        private readonly RadioButton _none = new RadioButton();
        private readonly RadioButton _considering = new RadioButton();
        private readonly RadioButton _declined = new RadioButton();
        private readonly DateTimePicker _date = new DateTimePicker { Format = DateTimePickerFormat.Short };
        private readonly TextBox _reason = new TextBox();

        public ResponseStatusForm(AppServices services, AnkenSupplier supplier)
        {
            _services = services;
            _s = supplier;
            UiStyle.Apply(this);
            Text = "返事の状況: " + supplier.SupplierName;
            ClientSize = new Size(620, 420);

            Controls.Add(new Label { Text = supplier.SupplierName + " の返事の状況", Font = new Font("BIZ UDPGothic", 14F, FontStyle.Bold), Left = 16, Top = 12, Width = 580, Height = 36 });
            Controls.Add(new Label
            {
                Text = "見積の回答がまだでも、「辞退」や「検討中」と分かったときに入れます。辞退は催促の対象から外れ、回答待ちの数にも数えません。",
                Left = 16, Top = 52, Width = 588, Height = 50, ForeColor = Color.FromArgb(90, 96, 100),
            });

            _none.SetBounds(16, 112, 580, 32);
            _none.Text = "未回答（返事を待っている）";
            _considering.SetBounds(16, 148, 580, 32);
            _considering.Text = "検討中（回答予定日を聞いている）";
            _declined.SetBounds(16, 232, 580, 32);
            _declined.Text = "辞退（見積を出さない）";
            Controls.AddRange(new Control[] { _none, _considering, _declined });

            Controls.Add(new Label { Text = "回答予定日", Left = 48, Top = 186, Width = 120, Height = 28 });
            _date.SetBounds(172, 182, 180, 32);
            Controls.Add(_date);

            Controls.Add(new Label { Text = "辞退の理由（任意）", Left = 48, Top = 272, Width = 400, Height = 24 });
            _reason.SetBounds(48, 298, 548, 32);
            Controls.Add(_reason);

            if (supplier.IsDeclined)
            {
                _declined.Checked = true;
                _reason.Text = supplier.DeclineReason;
            }
            else if (supplier.IsConsidering)
            {
                _considering.Checked = true;
            }
            else
            {
                _none.Checked = true;
            }

            _date.Value = supplier.PromisedDate ?? DateTime.Today.AddDays(3);
            _considering.CheckedChanged += (s, e) => Enable();
            _declined.CheckedChanged += (s, e) => Enable();
            Enable();

            var ok = UiStyle.CreateButton("保存", true, 140);
            ok.Left = 314; ok.Top = 356;
            ok.Click += (s, e) => Save();
            var cancel = UiStyle.CreateButton("キャンセル", false, 140);
            cancel.Left = 464; cancel.Top = 356;
            cancel.Click += (s, e) => DialogResult = DialogResult.Cancel;
            Controls.AddRange(new Control[] { ok, cancel });
            CancelButton = cancel;
        }

        private void Enable()
        {
            _date.Enabled = _considering.Checked;
            _reason.Enabled = _declined.Checked;
        }

        private void Save()
        {
            var status = _declined.Checked ? ResponseStatus.Declined : _considering.Checked ? ResponseStatus.Considering : ResponseStatus.None;
            if (status == ResponseStatus.Declined && _s.IsAnswered)
            {
                var ok = MessageBox.Show(this, _s.SupplierName + " は、すでに回答を受け取っています（回答受領日あり）。\r\n辞退として記録しても、受領日が入っている間は「回答済み」のままです。\r\n\r\n続けますか？",
                    "返事の状況", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
                if (ok != DialogResult.Yes)
                {
                    return;
                }
            }

            try
            {
                _services.Db.SetSupplierResponse(_s.AnkenId, _s.SupplierId, status, status == ResponseStatus.Considering ? _date.Value.Date : (DateTime?)null, _reason.Text);
                DialogResult = DialogResult.OK;
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(this, ex.Message, "返事の状況", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
