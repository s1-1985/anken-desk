using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using AnkenDesk.Core;

namespace AnkenDesk.App
{
    /// <summary>案件に加える調達先を、マスターから選ぶ（まだ加えていない調達先だけ出す）。</summary>
    internal sealed class SupplierPickForm : Form
    {
        private readonly AppServices _services;
        private readonly HashSet<long> _already;
        private readonly CheckedListBox _list = new CheckedListBox();
        private readonly Label _hint = new Label();

        public List<Supplier> Chosen { get; } = new List<Supplier>();

        public SupplierPickForm(AppServices services, IEnumerable<long> alreadyAdded)
        {
            _services = services;
            _already = new HashSet<long>(alreadyAdded);
            UiStyle.Apply(this);
            Text = "調達先を加える";
            ClientSize = new Size(560, 520);

            Controls.Add(new Label { Text = "この案件で見積を依頼する調達先を選んでください。", Left = 16, Top = 12, Width = 528, Height = 28 });

            _list.SetBounds(16, 44, 528, 360);
            _list.CheckOnClick = true;
            _list.DisplayMember = "Name";
            Controls.Add(_list);

            _hint.SetBounds(16, 410, 528, 40);
            Controls.Add(_hint);

            var manage = UiStyle.CreateButton("調達先マスター...", false, 200);
            manage.Left = 16; manage.Top = 464;
            manage.Click += (s, e) =>
            {
                new SuppliersForm(_services).ShowDialog(this);
                Reload();
            };

            var ok = UiStyle.CreateButton("加える", true, 120);
            ok.Left = 288; ok.Top = 464;
            ok.Click += (s, e) =>
            {
                foreach (var item in _list.CheckedItems)
                {
                    Chosen.Add((Supplier)item);
                }

                DialogResult = DialogResult.OK;
            };

            var cancel = UiStyle.CreateButton("キャンセル", false, 130);
            cancel.Left = 414; cancel.Top = 464;
            cancel.Click += (s, e) => DialogResult = DialogResult.Cancel;

            Controls.AddRange(new Control[] { manage, ok, cancel });
            Reload();
        }

        private void Reload()
        {
            _list.Items.Clear();
            foreach (var s in _services.Db.ListSuppliers().Where(x => !_already.Contains(x.Id)))
            {
                _list.Items.Add(s);
            }

            _hint.Text = _list.Items.Count == 0
                ? "加えられる調達先がありません。「調達先マスター...」から登録してください。"
                : "";
        }
    }
}
