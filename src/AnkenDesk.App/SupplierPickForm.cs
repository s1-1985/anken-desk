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

        private readonly IReadOnlyList<long> _recent;

        public List<Supplier> Chosen { get; } = new List<Supplier>();

        // 一覧の1行。同じ得意先の直近の案件で依頼した調達先には「★前回」を付けて、先に並べる。
        private sealed class Item
        {
            public Supplier Supplier = new Supplier();
            public bool Recent;

            public override string ToString()
            {
                return (Recent ? "★前回　" : "") + Supplier.Name;
            }
        }

        /// <param name="recentSupplierIds">同じ得意先・種別の直近の案件の調達先（印を付けて先頭に出す）。</param>
        public SupplierPickForm(AppServices services, IEnumerable<long> alreadyAdded, IReadOnlyList<long>? recentSupplierIds = null)
        {
            _services = services;
            _already = new HashSet<long>(alreadyAdded);
            _recent = recentSupplierIds ?? new List<long>();
            UiStyle.Apply(this);
            Text = "調達先を加える";
            ClientSize = new Size(560, 520);

            Controls.Add(new Label { Text = "この案件で見積を依頼する調達先を選んでください。", Left = 16, Top = 12, Width = 528, Height = 28 });

            _list.SetBounds(16, 44, 528, 360);
            _list.CheckOnClick = true;
            Controls.Add(_list);

            _hint.SetBounds(250, 414, 294, 40);
            Controls.Add(_hint);

            var manage = UiStyle.CreateButton("調達先マスター...", false, 200);
            manage.Left = 16; manage.Top = 464;

            var sameAsLast = UiStyle.CreateButton("★前回と同じを選ぶ", false, 220);
            sameAsLast.Left = 16; sameAsLast.Top = 410 - 4;
            sameAsLast.Click += (s, e) =>
            {
                for (var i = 0; i < _list.Items.Count; i++)
                {
                    if (((Item)_list.Items[i]).Recent)
                    {
                        _list.SetItemChecked(i, true);
                    }
                }
            };
            sameAsLast.Visible = _recent.Count > 0;
            Controls.Add(sameAsLast);
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
                    Chosen.Add(((Item)item).Supplier);
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
            var items = _services.Db.ListSuppliers().Where(x => !_already.Contains(x.Id))
                .Select(x => new Item { Supplier = x, Recent = _recent.Contains(x.Id) })
                .OrderBy(i => i.Recent ? _recent.ToList().IndexOf(i.Supplier.Id) : int.MaxValue)
                .ToList();
            foreach (var i in items)
            {
                _list.Items.Add(i);
            }

            _hint.Text = _list.Items.Count == 0
                ? "加えられる調達先がありません。「調達先マスター...」から登録してください。"
                : (_recent.Count > 0 ? "★は、同じ得意先・種別の直近の案件で依頼した調達先です。" : "");
        }
    }
}
