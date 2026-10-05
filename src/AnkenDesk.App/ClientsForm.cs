using System;
using System.Drawing;
using System.Windows.Forms;
using AnkenDesk.Core;

namespace AnkenDesk.App
{
    /// <summary>得意先・種別を、任意の名前で登録・変更・削除する。名前は Work space 直下のフォルダ名になる。</summary>
    internal sealed class ClientsForm : Form
    {
        private readonly AppServices _services;
        private readonly ListBox _list;
        private readonly TextBox _name;

        public ClientsForm(AppServices services)
        {
            _services = services;
            UiStyle.Apply(this);
            Text = "得意先・種別の管理";
            ClientSize = new Size(560, 520);

            var help = new Label
            {
                Text = "ここで登録した名前が、Work space直下のフォルダ名になります。\r\n例: 得意先名と種別を全角スペースでつなぐ。",
                Left = 16, Top = 12, Width = 528, Height = 50,
            };

            _name = new TextBox { Left = 16, Top = 364, Width = 528 };

            _list = new ListBox { Left = 16, Top = 70, Width = 528, Height = 280, IntegralHeight = false };
            _list.SelectedIndexChanged += (s, e) =>
            {
                var c = _list.SelectedItem as Client;
                if (c != null)
                {
                    _name.Text = c.Name;
                }
            };

            var add = UiStyle.CreateButton("追加", true, 120);
            add.Left = 16; add.Top = 404;
            add.Click += (s, e) => Run(() => _services.Db.AddClient(_name.Text));

            var rename = UiStyle.CreateButton("名前を変更", false, 140);
            rename.Left = 144; rename.Top = 404;
            rename.Click += (s, e) => Run(() =>
            {
                var c = Selected();
                _services.Db.RenameClient(c.Id, _name.Text);
            });

            var delete = UiStyle.CreateButton("削除", false, 120);
            delete.Left = 292; delete.Top = 404;
            delete.Click += (s, e) => Run(() =>
            {
                var c = Selected();
                var ok = MessageBox.Show(this, "「" + c.Name + "」を一覧から削除します。\r\n（フォルダは削除しません）", "削除の確認",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
                if (ok == DialogResult.Yes)
                {
                    _services.Db.DeleteClient(c.Id);
                }
            });

            var close = UiStyle.CreateButton("閉じる", false, 120);
            close.Left = 424; close.Top = 464;
            close.Click += (s, e) => Close();

            Controls.AddRange(new Control[] { help, _list, _name, add, rename, delete, close });
            Reload();
        }

        private Client Selected()
        {
            var c = _list.SelectedItem as Client;
            if (c == null)
            {
                throw new InvalidOperationException("一覧から選んでください。");
            }

            return c;
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
                MessageBox.Show(this, ex.Message, "得意先・種別", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void Reload()
        {
            _list.DisplayMember = "Name";
            _list.DataSource = null;
            _list.DataSource = new System.Collections.Generic.List<Client>(_services.Db.ListClients());
            _name.Text = "";
        }
    }
}
