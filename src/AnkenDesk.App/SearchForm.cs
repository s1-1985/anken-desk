using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using AnkenDesk.Core;

namespace AnkenDesk.App
{
    /// <summary>全案件を横断して探す（案件の情報・メモ・メールの件名・回答のメモ・ファイル名）。読むだけ。</summary>
    internal sealed class SearchForm : Form
    {
        private readonly AppServices _services;
        private readonly TextBox _query = new TextBox();
        private readonly ListView _list = new ListView();
        private readonly Label _summary = new Label();
        private IReadOnlyList<SearchHit> _hits = new List<SearchHit>();

        public SearchForm(AppServices services)
        {
            _services = services;
            UiStyle.Apply(this);
            Text = "横断検索";
            ClientSize = new Size(1200, 740);

            Controls.Add(new Label { Text = "横断検索", Font = new Font("BIZ UDPGothic", 18F, FontStyle.Bold), Left = 16, Top = 10, Width = 400, Height = 40 });
            Controls.Add(new Label
            {
                Text = "全案件の、案件の情報・メモ・メールの件名・回答のメモ・ファイル名から探します。空白で区切った語が、すべて含まれるものが出ます。ダブルクリックで、案件（ファイルならそのファイル）を開きます。",
                Left = 16, Top = 52, Width = 1168, Height = 50, ForeColor = Color.FromArgb(90, 96, 100),
            });

            _query.SetBounds(16, 112, 760, 32);
            Controls.Add(_query);
            var go = UiStyle.CreateButton("検索", true, 140);
            go.Left = 790; go.Top = 108;
            go.Click += (s, e) => Search();
            Controls.Add(go);
            AcceptButton = go;

            _summary.SetBounds(950, 116, 234, 28);
            _summary.TextAlign = ContentAlignment.MiddleRight;
            Controls.Add(_summary);

            _list.SetBounds(16, 156, 1168, 520);
            _list.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.GridLines = true;
            _list.HideSelection = false;
            _list.MultiSelect = false;
            _list.Columns.Add("案件", 320);
            _list.Columns.Add("種類", 80);
            _list.Columns.Add("内容", 740);
            _list.DoubleClick += (s, e) => OpenSelected();
            Controls.Add(_list);

            var open = UiStyle.CreateButton("開く", true, 140);
            open.Left = 884; open.Top = 688;
            open.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            open.Click += (s, e) => OpenSelected();
            var close = UiStyle.CreateButton("閉じる", false, 140);
            close.Left = 1044; close.Top = 688;
            close.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            close.Click += (s, e) => Close();
            Controls.AddRange(new Control[] { open, close });
            CancelButton = close;
            Shown += (s, e) => _query.Focus();
        }

        private void Search()
        {
            var q = _query.Text;
            if (q.Trim().Length == 0)
            {
                return;
            }

            var db = _services.Db;
            var root = _services.WorkspaceRoot;
            try
            {
                _hits = Background.Run<IReadOnlyList<SearchHit>>(this, "探しています。\r\nしばらくお待ちください。", () => GlobalSearch.Run(db, root, q));
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(this, "検索できませんでした。\r\n\r\n" + ex.Message, "横断検索", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var h in _hits)
            {
                var item = new ListViewItem(Home.Title(h.Anken) + "　" + h.Anken.ClientName);
                item.SubItems.Add(h.Kind);
                item.SubItems.Add(h.Text);
                _list.Items.Add(item);
            }

            _list.EndUpdate();
            _summary.Text = _hits.Count == 0 ? "見つかりません" : _hits.Count + " 件" + (_hits.Count >= 500 ? "（多いので、先頭の500件）" : "");
        }

        private void OpenSelected()
        {
            if (_list.SelectedIndices.Count == 0)
            {
                return;
            }

            var h = _hits[_list.SelectedIndices[0]];
            if (h.Kind == "ファイル" && h.Path != null)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(h.Path) { UseShellExecute = true });
                }
                catch (System.ComponentModel.Win32Exception ex)
                {
                    MessageBox.Show(this, "開けませんでした。\r\n\r\n" + ex.Message, "横断検索", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

                return;
            }

            using (var dlg = new AnkenDetailForm(_services, h.Anken.Id))
            {
                dlg.ShowDialog(this);
            }
        }
    }
}
