using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using AnkenDesk.Core;

namespace AnkenDesk.App
{
    /// <summary>
    /// 案件一覧。ホームと同じ本体の中に、タブのように切り替えて表示する（別の窓は開かない）。
    /// 全案件（終わった案件も）を、状態で絞り込み、検索できる。ダブルクリックで案件画面を開く。
    /// </summary>
    internal sealed class AnkenListView : UserControl
    {
        private readonly AppServices _services;
        private readonly DataGridView _grid = new DataGridView();
        private readonly TextBox _search = new TextBox();
        private readonly ComboBox _status = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly Label _count = new Label();
        private IReadOnlyList<HomeRow> _rows = new List<HomeRow>();

        public AnkenListView(AppServices services)
        {
            _services = services;
            Dock = DockStyle.Fill;
            BackColor = UiStyle.Background;
            Font = new Font("BIZ UDPGothic", 11F);
            Padding = new Padding(24, 16, 24, 16);

            var title = new Label { Text = "案件一覧", Font = new Font("BIZ UDPGothic", 22F, FontStyle.Bold), Left = 0, Top = 0, Width = 400, Height = 44 };
            var sub = new Label { Text = "登録した全案件（終わった案件も）。ダブルクリックで案件画面を開きます。", Left = 0, Top = 48, Width = 800, Height = 28, ForeColor = Color.FromArgb(90, 96, 100) };

            var captionSearch = new Label { Text = "検索（品番・日付・調達先）", Left = 0, Top = 90, Width = 260, Height = 22, ForeColor = Color.FromArgb(90, 96, 100) };
            _search.SetBounds(0, 114, 320, 30);
            _search.TextChanged += (s, e) => Apply();

            var captionStatus = new Label { Text = "状態", Left = 340, Top = 90, Width = 100, Height = 22, ForeColor = Color.FromArgb(90, 96, 100) };
            _status.SetBounds(340, 114, 190, 30);
            _status.Items.Add("すべて");
            _status.Items.AddRange(AnkenStatus.All);
            _status.SelectedIndex = 0;
            _status.SelectedIndexChanged += (s, e) => Apply();

            _count.SetBounds(550, 116, 200, 26);
            _count.TextAlign = ContentAlignment.MiddleLeft;

            var open = UiStyle.CreateButton("案件を開く", true, 160);
            open.Top = 104;
            open.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            open.Click += (s, e) => OpenDetail();
            var explorer = UiStyle.CreateButton("エクスプローラーで開く", false, 230);
            explorer.Top = 104;
            explorer.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            explorer.Click += (s, e) => OpenFolder();

            _grid.Left = 0;
            _grid.Top = 160;
            _grid.ReadOnly = true;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.AllowUserToResizeRows = false;
            _grid.RowHeadersVisible = false;
            _grid.BackgroundColor = Color.White;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.MultiSelect = false;
            _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            _grid.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            AddColumn("依頼日", 110);
            AddColumn("得意先・種別", 190);
            AddColumn("品番", 170);
            AddColumn("品名", 170);
            AddColumn("備考", 130);
            AddColumn("状態", 150);
            AddColumn("回答期限", 110);
            var progress = AddColumn("回答状況", 240);
            progress.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            _grid.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex >= 0)
                {
                    OpenDetail();
                }
            };

            Controls.AddRange(new Control[] { title, sub, captionSearch, _search, captionStatus, _status, _count, open, explorer, _grid });
            Resize += (s, e) => Layout2(open, explorer);
            Layout2(open, explorer);
            Reload();
        }

        private void Layout2(Button open, Button explorer)
        {
            var w = ClientSize.Width - Padding.Left - Padding.Right;
            open.Left = w - open.Width;
            explorer.Left = open.Left - 12 - explorer.Width;
            _grid.Width = w;
            _grid.Height = Math.Max(100, ClientSize.Height - Padding.Top - Padding.Bottom - 160);
        }

        private DataGridViewColumn AddColumn(string header, int width)
        {
            var col = new DataGridViewTextBoxColumn { HeaderText = header, Width = width, SortMode = DataGridViewColumnSortMode.NotSortable };
            _grid.Columns.Add(col);
            return col;
        }

        /// <summary>DBから読み直して表示する（本体に戻ったとき、案件画面から戻ったときに呼ぶ）。</summary>
        public void Reload()
        {
            var today = DateTime.Today;
            _rows = Home.Build(_services.Db.ListAnkens(), _services.Db.ListAllAnkenSuppliers(), today)
                .OrderByDescending(r => r.Anken.RequestDate).ThenByDescending(r => r.Anken.Id).ToList();
            Apply();
        }

        private void Apply()
        {
            var keep = _grid.CurrentRow == null ? -1 : (_grid.CurrentRow.Tag is HomeRow r0 ? r0.Anken.Id : -1);
            var status = _status.SelectedIndex <= 0 ? null : (string)_status.SelectedItem!;
            var rows = Home.Filter(_rows, HomeFilter.All, _search.Text).Where(r => status == null || r.Anken.Status == status).ToList();

            _grid.Rows.Clear();
            foreach (var r in rows)
            {
                var a = r.Anken;
                var idx = _grid.Rows.Add(
                    a.RequestDate.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture),
                    a.ClientName,
                    a.PartNumber,
                    a.PartName,
                    a.Note,
                    Home.StatusMark(r) + r.StatusText,
                    a.ReplyDueDate.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture),
                    r.ProgressText());
                _grid.Rows[idx].Tag = r;
                if (a.Id == keep)
                {
                    _grid.CurrentCell = _grid.Rows[idx].Cells[0];
                }
            }

            _count.Text = rows.Count + " 件（全 " + _rows.Count + " 件）";
        }

        private HomeRow? Selected()
        {
            return _grid.CurrentRow == null ? null : _grid.CurrentRow.Tag as HomeRow;
        }

        private void OpenDetail()
        {
            var r = Selected();
            if (r == null)
            {
                MessageBox.Show(this, "一覧から案件を選んでください。", "案件一覧", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var dlg = new AnkenDetailForm(_services, r.Anken.Id))
            {
                dlg.ShowDialog(this);
            }

            Reload();
        }

        private void OpenFolder()
        {
            var r = Selected();
            if (r == null)
            {
                MessageBox.Show(this, "一覧から案件を選んでください。", "案件一覧", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var full = Path.Combine(_services.WorkspaceRoot, r.Anken.FolderPath);
            if (!Directory.Exists(full))
            {
                MessageBox.Show(this, "フォルダが見つかりません。\r\n" + full + "\r\n\r\n（Work spaceの場所を変えた、またはフォルダを移動・削除した可能性があります）",
                    "案件一覧", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + full + "\"") { UseShellExecute = true });
        }
    }
}
