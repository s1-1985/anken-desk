using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using AnkenDesk.Core;

namespace AnkenDesk.App
{
    /// <summary>調達先マスター。正式名、略称（見積書PDFのファイル名に使う）、メールアドレス、住所。</summary>
    internal sealed class SuppliersForm : Form
    {
        private readonly AppServices _services;
        private readonly ListBox _list = new ListBox();
        private readonly TextBox _name = new TextBox();
        private readonly TextBox _short = new TextBox();
        private readonly TextBox _email = new TextBox();
        private readonly TextBox _address = new TextBox();
        private readonly TextBox _specialty = new TextBox();

        public SuppliersForm(AppServices services)
        {
            _services = services;
            UiStyle.Apply(this);
            Text = "調達先マスター";
            ClientSize = new Size(860, 560);

            _list.SetBounds(16, 16, 300, 480);
            _list.IntegralHeight = false;
            _list.DisplayMember = "Name";
            _list.SelectedIndexChanged += (s, e) => ShowSelected();

            AddField("正式名", _name, 340, 16);
            AddField("略称（見積書PDFのファイル名に使います。空なら正式名）", _short, 340, 96);
            AddField("メールアドレス", _email, 340, 176);
            AddField("住所", _address, 340, 256);
            AddField("得意分野（工法・材質など。空白で区切って複数。調達先を選ぶときの検索に使います）", _specialty, 340, 336);

            var add = UiStyle.CreateButton("追加", true, 120);
            add.Left = 340; add.Top = 420;
            add.Click += (s, e) => Run(() =>
            {
                var created = _services.Db.AddSupplier(_name.Text, _short.Text, _email.Text, _address.Text);
                _services.Db.SetSupplierSpecialty(created.Id, _specialty.Text);
            });

            var update = UiStyle.CreateButton("選んだ調達先を更新", false, 200);
            update.Left = 470; update.Top = 420;
            update.Click += (s, e) => Run(() =>
            {
                var sel = Selected();
                sel.Name = _name.Text;
                sel.ShortName = _short.Text;
                sel.Email = _email.Text;
                sel.Address = _address.Text;
                _services.Db.UpdateSupplier(sel);
                _services.Db.SetSupplierSpecialty(sel.Id, _specialty.Text);
            });

            var delete = UiStyle.CreateButton("削除", false, 120);
            delete.Left = 680; delete.Top = 420;
            delete.Click += (s, e) => Run(() =>
            {
                var sel = Selected();
                var ok = MessageBox.Show(this, "「" + sel.Name + "」を削除します。", "削除の確認",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
                if (ok == DialogResult.Yes)
                {
                    _services.Db.DeleteSupplier(sel.Id);
                }
            });

            var close = UiStyle.CreateButton("閉じる", false, 120);
            close.Left = 724; close.Top = 504;
            close.Click += (s, e) => Close();

            var stats = UiStyle.CreateButton("調達先の実績を見る", false, 240);
            stats.Left = 340; stats.Top = 470;
            stats.Click += (s, e) => new SupplierStatsForm(_services).ShowDialog(this);

            Controls.AddRange(new Control[] { _list, add, update, delete, stats, close });
            Reload();
        }

        private void AddField(string caption, TextBox box, int x, int y)
        {
            Controls.Add(new Label { Text = caption, Left = x, Top = y, Width = 500, Height = 24 });
            box.SetBounds(x, y + 28, 500, 32);
            Controls.Add(box);
        }

        private Supplier Selected()
        {
            var s = _list.SelectedItem as Supplier;
            if (s == null)
            {
                throw new InvalidOperationException("一覧から選んでください。");
            }

            return s;
        }

        private void ShowSelected()
        {
            var s = _list.SelectedItem as Supplier;
            if (s == null)
            {
                return;
            }

            _name.Text = s.Name;
            _short.Text = s.ShortName;
            _email.Text = s.Email;
            _address.Text = s.Address;
            _specialty.Text = s.Specialty;
        }

        private void Run(Action action)
        {
            try
            {
                action();
                Reload();
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException)
            {
                MessageBox.Show(this, ex.Message, "調達先マスター", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void Reload()
        {
            _list.DataSource = null;
            _list.DisplayMember = "Name";
            _list.DataSource = new List<Supplier>(_services.Db.ListSuppliers());
            _list.ClearSelected();
            _name.Text = "";
            _short.Text = "";
            _email.Text = "";
            _address.Text = "";
            _specialty.Text = "";
        }
    }
}
