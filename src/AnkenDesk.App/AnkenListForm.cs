using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using AnkenDesk.Core;

namespace AnkenDesk.App
{
    /// <summary>登録した案件の一覧。段1では、確認用のシンプルな表。</summary>
    internal sealed class AnkenListForm : Form
    {
        private readonly AppServices _services;
        private readonly DataGridView _grid;

        public AnkenListForm(AppServices services)
        {
            _services = services;
            UiStyle.Apply(this);
            Text = "案件一覧";
            ClientSize = new Size(1000, 560);

            _grid = new DataGridView
            {
                Left = 16, Top = 16, Width = 968, Height = 440,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                RowHeadersVisible = false,
                AutoGenerateColumns = false,
                BackgroundColor = Color.White,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            };
            _grid.Columns.Add(Col("RequestDate", "依頼日", 110));
            _grid.Columns.Add(Col("ClientName", "得意先・種別", 200));
            _grid.Columns.Add(Col("PartNumber", "品番", 180));
            _grid.Columns.Add(Col("PartName", "品名", 180));
            _grid.Columns.Add(Col("Note", "備考", 140));
            _grid.Columns.Add(Col("ReplyDueDate", "回答希望期日", 130));
            _grid.Columns[0].DefaultCellStyle.Format = "yyyy/MM/dd";
            _grid.Columns[5].DefaultCellStyle.Format = "yyyy/MM/dd";

            var open = UiStyle.CreateButton("エクスプローラーで開く", true, 260);
            open.Left = 16; open.Top = 480;
            open.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            open.Click += (s, e) => OpenSelected();

            var close = UiStyle.CreateButton("閉じる", false, 140);
            close.Left = 844; close.Top = 480;
            close.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            close.Click += (s, e) => Close();

            Controls.AddRange(new Control[] { _grid, open, close });
            _grid.DataSource = new System.ComponentModel.BindingList<AnkenRecord>(new System.Collections.Generic.List<AnkenRecord>(_services.Db.ListAnkens()));
        }

        private static DataGridViewTextBoxColumn Col(string property, string header, int width)
        {
            return new DataGridViewTextBoxColumn { DataPropertyName = property, HeaderText = header, Width = width };
        }

        private void OpenSelected()
        {
            var rec = _grid.CurrentRow == null ? null : _grid.CurrentRow.DataBoundItem as AnkenRecord;
            if (rec == null)
            {
                MessageBox.Show(this, "一覧から案件を選んでください。", "案件一覧", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var full = Path.Combine(_services.WorkspaceRoot, rec.FolderPath);
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
