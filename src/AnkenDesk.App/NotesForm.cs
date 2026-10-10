using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using AnkenDesk.Core;

namespace AnkenDesk.App
{
    /// <summary>案件のメモ。交渉の内容に限らず、なんでも書き留める。調達先に紐づけることもできる。受信メールの内容を記録したものも、ここに並ぶ。</summary>
    internal sealed class NotesForm : Form
    {
        private readonly AppServices _services;
        private readonly AnkenRecord _anken;
        private readonly ListView _list = new ListView();
        private readonly TextBox _view = new TextBox();
        private readonly TextBox _new = new TextBox();
        private readonly ComboBox _supplier = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private IReadOnlyList<AnkenNote> _notes = new List<AnkenNote>();
        private IReadOnlyList<AnkenSupplier> _suppliers = new List<AnkenSupplier>();

        public NotesForm(AppServices services, AnkenRecord anken)
        {
            _services = services;
            _anken = anken;
            UiStyle.Apply(this);
            Text = "メモ　" + Home.Title(anken);
            ClientSize = new Size(1100, 720);

            Controls.Add(new Label { Text = "メモ", Font = new Font("BIZ UDPGothic", 18F, FontStyle.Bold), Left = 16, Top = 10, Width = 300, Height = 40 });
            Controls.Add(new Label { Text = Home.Title(anken), Left = 120, Top = 20, Width = 960, Height = 26, ForeColor = Color.FromArgb(90, 96, 100) });

            _list.SetBounds(16, 58, 520, 400);
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.HideSelection = false;
            _list.MultiSelect = false;
            _list.Columns.Add("日時", 130);
            _list.Columns.Add("調達先", 90);
            _list.Columns.Add("内容", 280);
            _list.SelectedIndexChanged += (s, e) => ShowSelected();
            Controls.Add(_list);

            _view.SetBounds(548, 58, 536, 330);
            _view.Multiline = true;
            _view.ScrollBars = ScrollBars.Vertical;
            Controls.Add(_view);

            var update = UiStyle.CreateButton("このメモを直す", false, 200);
            update.Left = 548; update.Top = 398;
            update.Click += (s, e) => UpdateSelected();
            var delete = UiStyle.CreateButton("削除", false, 120);
            delete.Left = 760; delete.Top = 398;
            delete.Click += (s, e) => DeleteSelected();
            Controls.AddRange(new Control[] { update, delete });

            Controls.Add(new Label { Text = "新しいメモ（交渉の内容、電話の内容、気づいたこと、なんでも）", Left = 16, Top = 470, Width = 700, Height = 26 });
            _new.SetBounds(16, 498, 840, 150);
            _new.Multiline = true;
            _new.AcceptsReturn = true;
            _new.ScrollBars = ScrollBars.Vertical;
            Controls.Add(_new);

            Controls.Add(new Label { Text = "調達先（なくてもよい）", Left = 870, Top = 498, Width = 214, Height = 26 });
            _supplier.SetBounds(870, 526, 214, 32);
            Controls.Add(_supplier);

            var add = UiStyle.CreateButton("メモを追加", true, 214);
            add.Left = 870; add.Top = 570;
            add.Click += (s, e) => AddNote();
            Controls.Add(add);

            var close = UiStyle.CreateButton("閉じる", false, 140);
            close.Left = 944; close.Top = 664;
            close.Click += (s, e) => Close();
            Controls.Add(close);
            CancelButton = close;

            Reload();
        }

        private void Reload()
        {
            _suppliers = _services.Db.ListAnkenSuppliers(_anken.Id);
            _supplier.Items.Clear();
            _supplier.Items.Add("（なし）");
            foreach (var s in _suppliers)
            {
                _supplier.Items.Add(s.SupplierName);
            }

            _supplier.SelectedIndex = 0;

            _notes = _services.Db.ListNotes(_anken.Id);
            _list.BeginUpdate();
            _list.Items.Clear();
            var names = _suppliers.ToDictionary(s => s.SupplierId, s => s.ShortName);
            foreach (var n in _notes)
            {
                string who;
                var item = new ListViewItem(n.CreatedAt.ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture));
                item.SubItems.Add(n.SupplierId.HasValue && names.TryGetValue(n.SupplierId.Value, out who) ? who : "");
                var first = n.Text.Replace("\r", "").Split('\n')[0];
                item.SubItems.Add(first.Length > 60 ? first.Substring(0, 60) + "…" : first);
                _list.Items.Add(item);
            }

            _list.EndUpdate();
            _view.Text = "";
            if (_list.Items.Count > 0)
            {
                _list.Items[0].Selected = true;
            }
        }

        private AnkenNote? Selected()
        {
            return _list.SelectedIndices.Count == 0 ? null : _notes[_list.SelectedIndices[0]];
        }

        private void ShowSelected()
        {
            var n = Selected();
            _view.Text = n == null ? "" : n.Text.Replace("\r\n", "\n").Replace("\n", "\r\n");
        }

        private void AddNote()
        {
            if (_new.Text.Trim().Length == 0)
            {
                MessageBox.Show(this, "メモの内容を入れてください。", "メモ", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            long? supplierId = _supplier.SelectedIndex > 0 ? _suppliers[_supplier.SelectedIndex - 1].SupplierId : (long?)null;
            _services.Db.AddNote(_anken.Id, supplierId, _new.Text.Replace("\r\n", "\n"));
            _new.Text = "";
            Reload();
        }

        private void UpdateSelected()
        {
            var n = Selected();
            if (n == null)
            {
                return;
            }

            try
            {
                _services.Db.UpdateNote(n.Id, _view.Text.Replace("\r\n", "\n"));
            }
            catch (ArgumentException ex)
            {
                MessageBox.Show(this, ex.Message, "メモ", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var keep = _list.SelectedIndices[0];
            Reload();
            if (keep < _list.Items.Count)
            {
                _list.Items[keep].Selected = true;
            }
        }

        private void DeleteSelected()
        {
            var n = Selected();
            if (n == null)
            {
                return;
            }

            var ok = MessageBox.Show(this, "このメモを削除します。元に戻せません。\r\n\r\n" + (n.Text.Length > 80 ? n.Text.Substring(0, 80) + "…" : n.Text), "メモの削除",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (ok == DialogResult.Yes)
            {
                _services.Db.DeleteNote(n.Id);
                Reload();
            }
        }
    }
}
