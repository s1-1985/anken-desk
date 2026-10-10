using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using AnkenDesk.Core;

namespace AnkenDesk.App
{
    /// <summary>案件の結果（受注・失注・保留など）を入れる。受注のときは、発注先に決めた調達先も選ぶ。</summary>
    internal sealed class ResultForm : Form
    {
        private readonly AppServices _services;
        private readonly AnkenRecord _anken;
        private readonly ComboBox _status = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly DateTimePicker _date = new DateTimePicker { Format = DateTimePickerFormat.Short, ShowCheckBox = true };
        private readonly ComboBox _supplier = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly TextBox _note = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical };
        private readonly IReadOnlyList<AnkenSupplier> _suppliers;

        public ResultForm(AppServices services, AnkenRecord anken)
        {
            _services = services;
            _anken = anken;
            _suppliers = services.Db.ListAnkenSuppliers(anken.Id);
            UiStyle.Apply(this);
            Text = "結果を入力　" + Home.Title(anken);
            ClientSize = new Size(640, 520);

            Controls.Add(new Label { Text = "結果を入力", Font = new Font("BIZ UDPGothic", 16F, FontStyle.Bold), Left = 16, Top = 10, Width = 400, Height = 38 });

            Controls.Add(new Label { Text = "状態", Left = 16, Top = 58, Width = 200, Height = 24 });
            _status.SetBounds(16, 84, 240, 32);
            _status.Items.AddRange(AnkenStatus.All);
            _status.SelectedItem = anken.Status;
            _status.SelectedIndexChanged += (s, e) => UpdateEnabled();
            Controls.Add(_status);

            Controls.Add(new Label { Text = "結果の日（決まった日）", Left = 300, Top = 58, Width = 300, Height = 24 });
            _date.SetBounds(300, 84, 200, 32);
            if (anken.ResultDate.HasValue) { _date.Value = anken.ResultDate.Value; _date.Checked = true; } else { _date.Checked = false; }
            Controls.Add(_date);

            Controls.Add(new Label { Text = "発注先に決めた調達先（受注のとき）", Left = 16, Top = 130, Width = 500, Height = 24 });
            _supplier.SetBounds(16, 156, 480, 32);
            _supplier.Items.Add("（決めていない・不明）");
            var selected = 0;
            for (var i = 0; i < _suppliers.Count; i++)
            {
                _supplier.Items.Add(_suppliers[i].SupplierName);
                if (anken.AdoptedSupplierId == _suppliers[i].SupplierId)
                {
                    selected = i + 1;
                }
            }

            _supplier.SelectedIndex = selected;
            Controls.Add(_supplier);

            Controls.Add(new Label { Text = "メモ（理由・価格・競合など）", Left = 16, Top = 204, Width = 500, Height = 24 });
            _note.SetBounds(16, 230, 608, 200);
            _note.Text = anken.ResultNote.Replace("\r\n", "\n").Replace("\n", "\r\n");
            Controls.Add(_note);

            var ok = UiStyle.CreateButton("保存", true, 140);
            ok.Left = 334; ok.Top = 456;
            ok.Click += (s, e) => Save();
            var cancel = UiStyle.CreateButton("キャンセル", false, 140);
            cancel.Left = 484; cancel.Top = 456;
            cancel.Click += (s, e) => DialogResult = DialogResult.Cancel;
            Controls.AddRange(new Control[] { ok, cancel });
            CancelButton = cancel;
            UpdateEnabled();
        }

        private void UpdateEnabled()
        {
            _supplier.Enabled = (string?)_status.SelectedItem == AnkenStatus.Won;
        }

        private void Save()
        {
            var status = (string)_status.SelectedItem!;
            long? sup = _supplier.Enabled && _supplier.SelectedIndex > 0 ? _suppliers[_supplier.SelectedIndex - 1].SupplierId : (long?)null;
            _services.Db.SetAnkenResult(_anken.Id, status, _date.Checked ? _date.Value.Date : (DateTime?)null, _note.Text.Replace("\r\n", "\n"), sup);
            DialogResult = DialogResult.OK;
        }
    }
}
