using System;
using System.IO;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using AnkenDesk.Core;

namespace AnkenDesk.App
{
    /// <summary>
    /// ホーム（要対応）。起動時に開く画面（HANDOFF.md §5.1）。
    /// 回答待ちの案件を回答期限の近い順に並べ、期限の件数を出す。常駐はしない。
    /// </summary>
    internal sealed class MainForm : Form
    {
        private static readonly Color NavSelected = ColorTranslator.FromHtml("#DDE9EE");

        private readonly AppServices _services;
        private readonly TextBox _search = new TextBox();
        private readonly Label _subtitle = new Label();
        private readonly Label _tileOverdue = new Label();
        private readonly Label _tileToday = new Label();
        private readonly Label _tileSoon = new Label();
        private readonly Label _tileWaiting = new Label();
        private readonly Button _tabNeeds = new Button();
        private readonly Button _tabAll = new Button();
        private readonly Button _tabAnswered = new Button();
        private readonly Button _tabWeek = new Button();
        private readonly Button _tabClosed = new Button();
        private Button? _navRemind;
        private readonly HoverPreview _hover = new HoverPreview();
        private readonly System.Windows.Forms.Timer _hoverTimer = new System.Windows.Forms.Timer { Interval = 600 };
        private int _hoverRow = -1;
        private readonly DataGridView _grid = new DataGridView();
        private readonly Label _empty = new Label();
        private readonly Label _workspace = new Label();

        private HomeFilter _filter = HomeFilter.NeedsAction;
        private IReadOnlyList<HomeRow> _allRows = new List<HomeRow>();

        public MainForm(AppServices services)
        {
            _services = services;
            UiStyle.Apply(this);
            Text = AppInfo.Title;
            var screen = Screen.PrimaryScreen.WorkingArea;
            ClientSize = new Size(Math.Min(1320, screen.Width - 40), Math.Min(860, screen.Height - 60));
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1000, 640);

            Controls.Add(BuildMain());
            Controls.Add(BuildNav());

            KeyPreview = true;
            _hoverTimer.Tick += (s, e) => ShowHover();
            _grid.CellMouseEnter += (s, e) =>
            {
                _hoverTimer.Stop();
                _hover.HidePreview();
                _hoverRow = e.RowIndex;
                if (e.RowIndex >= 0)
                {
                    _hoverTimer.Start();
                }
            };
            _grid.MouseLeave += (s, e) =>
            {
                _hoverTimer.Stop();
                _hover.HidePreview();
                _hoverRow = -1;
            };
            _grid.MouseDown += (s, e) =>
            {
                _hoverTimer.Stop();
                _hover.HidePreview();
            };
            Deactivate += (s, e) => _hover.HidePreview();
            FormClosed += (s, e) =>
            {
                _hoverTimer.Stop();
                _hover.Dispose();
            };

            _search.TextChanged += (s, e) => Reload();
            Activated += (s, e) => Reload();
            Reload();
        }

        // ---- 左のメニュー ----

        private Control BuildNav()
        {
            var nav = new Panel { Dock = DockStyle.Left, Width = 250, BackColor = Color.White, Padding = new Padding(12) };

            var brand = new Label
            {
                Text = AppInfo.Name,
                Font = new Font("BIZ UDPGothic", 16F, FontStyle.Bold),
                Dock = DockStyle.Top,
                Height = 44,
                Padding = new Padding(4, 8, 0, 0),
            };
            var sub = new Label { Text = "見積・案件管理　" + AppInfo.Version, Dock = DockStyle.Top, Height = 30, Padding = new Padding(6, 0, 0, 0), ForeColor = Color.FromArgb(90, 96, 100) };

            _workspace.Dock = DockStyle.Bottom;
            _workspace.Height = 110;
            _workspace.Padding = new Padding(4);
            _workspace.ForeColor = Color.FromArgb(90, 96, 100);

            // Dockの積み順: あとから足したものが手前（上）に来るので、逆順に足す。
            var items = new List<Control>
            {
                NavButton("ホーム（要対応）", true, null),
                NavButton("案件一覧", false, () => new AnkenListForm(_services).ShowDialog(this)),
                NavButton("受信メールから取り込む", false, () => new ImportMailForm(_services, null).ShowDialog(this)),
                NavButton("既存フォルダを取り込む", false, () => new ImportFolderForm(_services).ShowDialog(this)),
                (_navRemind = NavButton("催促が必要な案件", false, () => OpenReminderBatch())),
                NavButton("一覧をExcelに書き出す", false, () => ExportList()),
                NavButton("調達先マスター", false, () => new SuppliersForm(_services).ShowDialog(this)),
                NavButton("得意先・種別", false, () => new ClientsForm(_services).ShowDialog(this)),
                NavButton("見積依頼書の既定値", false, () => new ItemDefaultsForm(_services).ShowDialog(this)),
                NavButton("設定・DBの控え", false, () => new SettingsForm(_services).ShowDialog(this)),
            };
            var spacer = new Panel { Dock = DockStyle.Top, Height = 16 };

            nav.Controls.Add(_workspace);
            for (var i = items.Count - 1; i >= 0; i--)
            {
                nav.Controls.Add(items[i]);
            }

            nav.Controls.Add(spacer);
            nav.Controls.Add(sub);
            nav.Controls.Add(brand);
            return nav;
        }

        private Button NavButton(string text, bool selected, Action? action)
        {
            var b = new Button
            {
                Text = text,
                Dock = DockStyle.Top,
                Height = 48,
                FlatStyle = FlatStyle.Flat,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 0, 0, 0),
                BackColor = selected ? NavSelected : Color.White,
                ForeColor = UiStyle.Text,
                Font = new Font("BIZ UDPGothic", 11F, selected ? FontStyle.Bold : FontStyle.Regular),
                UseVisualStyleBackColor = false,
                Margin = new Padding(0),
            };
            b.FlatAppearance.BorderSize = 0;
            if (action != null)
            {
                b.Click += (s, e) =>
                {
                    action();
                    Reload();
                };
            }

            return b;
        }

        // ---- 右の本体 ----

        private Control BuildMain()
        {
            var main = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 16, 24, 16) };

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 124));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(BuildHeader(), 0, 0);
            layout.Controls.Add(BuildTiles(), 0, 1);
            layout.Controls.Add(BuildTabs(), 0, 2);
            layout.Controls.Add(BuildGrid(), 0, 3);
            main.Controls.Add(layout);
            return main;
        }

        private Control BuildHeader()
        {
            var p = new Panel { Dock = DockStyle.Fill };
            var title = new Label { Text = "要対応", Font = new Font("BIZ UDPGothic", 22F, FontStyle.Bold), Left = 0, Top = 0, Width = 400, Height = 44 };
            _subtitle.SetBounds(0, 48, 700, 28);
            _subtitle.ForeColor = Color.FromArgb(90, 96, 100);

            var register = UiStyle.CreateButton("案件を登録", true, 160);
            register.Top = 4;
            register.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            register.Click += (s, e) =>
            {
                new RegisterForm(_services).ShowDialog(this);
                Reload();
            };

            var caption = new Label { Text = "検索（品番・日付・調達先）　Ctrl+F", Width = 320, Height = 22, Top = 0, ForeColor = Color.FromArgb(90, 96, 100) };
            caption.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _search.Width = 320;
            _search.Top = 26;
            _search.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            p.Controls.AddRange(new Control[] { title, _subtitle, register, caption, _search });
            p.Resize += (s, e) =>
            {
                register.Left = p.ClientSize.Width - register.Width;
                _search.Left = register.Left - 16 - _search.Width;
                caption.Left = _search.Left;
            };
            return p;
        }

        private Control BuildTiles()
        {
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Padding = new Padding(0, 4, 0, 4) };
            for (var i = 0; i < 4; i++)
            {
                t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            }

            t.Controls.Add(Tile("期限超過", _tileOverdue, "回答期限を過ぎています"), 0, 0);
            t.Controls.Add(Tile("本日期限", _tileToday, "今日中に回答が必要です"), 1, 0);
            t.Controls.Add(Tile("3日以内", _tileSoon, "あと1〜3日で期限です"), 2, 0);
            t.Controls.Add(Tile("回答待ち", _tileWaiting, "未回答の調達先がある案件"), 3, 0);
            return t;
        }

        private static Control Tile(string caption, Label number, string note)
        {
            var p = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Margin = new Padding(0, 0, 12, 0), Padding = new Padding(14, 8, 8, 8) };
            var tag = new Label { Text = caption, Dock = DockStyle.Top, Height = 26, Font = new Font("BIZ UDPGothic", 11F, FontStyle.Bold), ForeColor = UiStyle.Primary };
            number.Dock = DockStyle.Top;
            number.Height = 44;
            number.Font = new Font("BIZ UDPGothic", 22F, FontStyle.Bold);
            var n = new Label { Text = note, Dock = DockStyle.Top, Height = 24, ForeColor = Color.FromArgb(90, 96, 100) };
            p.Controls.Add(n);
            p.Controls.Add(number);
            p.Controls.Add(tag);
            return p;
        }

        private Control BuildTabs()
        {
            var p = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 8, 0, 0) };
            SetupTab(_tabNeeds, HomeFilter.NeedsAction);
            SetupTab(_tabAll, HomeFilter.All);
            SetupTab(_tabAnswered, HomeFilter.Answered);
            SetupTab(_tabWeek, HomeFilter.DueThisWeek);
            SetupTab(_tabClosed, HomeFilter.Closed);
            p.Controls.AddRange(new Control[] { _tabNeeds, _tabWeek, _tabAll, _tabAnswered, _tabClosed });
            return p;
        }

        private void SetupTab(Button b, HomeFilter filter)
        {
            b.Height = UiStyle.ButtonHeight;
            b.Width = 128;
            b.FlatStyle = FlatStyle.Flat;
            b.UseVisualStyleBackColor = false;
            b.FlatAppearance.BorderColor = UiStyle.Primary;
            b.Margin = new Padding(0, 0, 8, 0);
            b.Click += (s, e) =>
            {
                _filter = filter;
                Reload();
            };
        }

        private Control BuildGrid()
        {
            var host = new Panel { Dock = DockStyle.Fill };

            _grid.Dock = DockStyle.Fill;
            _grid.ReadOnly = true;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.AllowUserToResizeRows = false;
            _grid.RowHeadersVisible = false;
            _grid.BackgroundColor = Color.White;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.MultiSelect = false;
            _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            _grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            _grid.DefaultCellStyle.Padding = new Padding(4);
            _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;

            AddColumn("状態", 150);
            AddColumn("回答期限", 140);
            AddColumn("得意先・種別", 200);
            var wide = AddColumn("案件", 300);
            wide.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            AddColumn("回答状況", 240);
            var action = new DataGridViewButtonColumn
            {
                HeaderText = "次の操作",
                UseColumnTextForButtonValue = false,
                Width = 140,
                FlatStyle = FlatStyle.Flat,
                SortMode = DataGridViewColumnSortMode.NotSortable,
            };
            _grid.Columns.Add(action);

            _grid.CellContentClick += (s, e) =>
            {
                if (e.RowIndex >= 0 && e.ColumnIndex == _grid.Columns.Count - 1)
                {
                    var row = _grid.Rows[e.RowIndex].Tag as HomeRow;
                    if (row != null && row.CanRemind)
                    {
                        Remind(row);
                    }
                    else
                    {
                        OpenDetail(e.RowIndex);
                    }
                }
            };
            _grid.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex >= 0)
                {
                    OpenDetail(e.RowIndex);
                }
            };

            _empty.Dock = DockStyle.Top;
            _empty.Height = 60;
            _empty.TextAlign = ContentAlignment.MiddleCenter;
            _empty.Font = new Font("BIZ UDPGothic", 12F);
            _empty.BackColor = Color.White;
            _empty.Visible = false;

            host.Controls.Add(_grid);
            host.Controls.Add(_empty);
            return host;
        }

        private DataGridViewColumn AddColumn(string header, int width)
        {
            var col = new DataGridViewTextBoxColumn { HeaderText = header, Width = width, SortMode = DataGridViewColumnSortMode.NotSortable };
            _grid.Columns.Add(col);
            return col;
        }

        // ---- 表示 ----

        private void Reload()
        {
            var today = DateTime.Today;
            _allRows = Home.Build(_services.Db.ListAnkens(), _services.Db.ListAllAnkenSuppliers(), today);
            var summary = Home.Summarize(_allRows);

            _subtitle.Text = today.ToString("yyyy年M月d日", CultureInfo.GetCultureInfo("ja-JP")) + "（" + DayName(today) + "）　"
                + "回答待ちの案件を、回答期限の近い順に表示しています";
            _tileOverdue.Text = summary.Overdue + " 件";
            _tileToday.Text = summary.Today + " 件";
            _tileSoon.Text = summary.Within3Days + " 件";
            _tileWaiting.Text = summary.Waiting + " 件";

            Tab(_tabNeeds, "要対応 " + Home.Filter(_allRows, HomeFilter.NeedsAction, null).Count, _filter == HomeFilter.NeedsAction);
            Tab(_tabAll, "すべて " + _allRows.Count, _filter == HomeFilter.All);
            Tab(_tabAnswered, "回答済み " + Home.Filter(_allRows, HomeFilter.Answered, null).Count, _filter == HomeFilter.Answered);
            Tab(_tabWeek, "今週期限 " + Home.Filter(_allRows, HomeFilter.DueThisWeek, null).Count, _filter == HomeFilter.DueThisWeek);
            Tab(_tabClosed, "終了・保留 " + Home.Filter(_allRows, HomeFilter.Closed, null).Count, _filter == HomeFilter.Closed);
            if (_navRemind != null)
            {
                var n = Home.RemindCandidates(_allRows).Count;
                _navRemind.Text = n > 0 ? "催促が必要な案件（" + n + "件）" : "催促が必要な案件";
                _navRemind.Font = new Font("BIZ UDPGothic", 11F, n > 0 ? FontStyle.Bold : FontStyle.Regular);
            }

            var rows = Home.Filter(_allRows, _filter, _search.Text);
            _grid.Rows.Clear();
            foreach (var r in rows)
            {
                var a = r.Anken;
                var title = Home.Title(a) + (string.IsNullOrWhiteSpace(a.PartName) ? "" : "\r\n" + a.PartName);
                var progress = r.Total == 0
                    ? "調達先が未登録です"
                    : r.Answered + "/" + r.Total + "社" + (r.PendingNames.Count > 0 ? "\r\n未回答: " + string.Join("、", r.PendingNames) : "");
                var idx = _grid.Rows.Add(
                    Home.StatusMark(r) + r.StatusText,
                    a.ReplyDueDate.ToString("MM/dd", CultureInfo.InvariantCulture) + "（" + DayName(a.ReplyDueDate) + "）",
                    a.ClientName,
                    title,
                    progress,
                    r.CanRemind ? "催促メールを作成" : "案件を開く");
                var row = _grid.Rows[idx];
                row.Tag = r;
                row.Cells[0].Style.Font = new Font("BIZ UDPGothic", 11F, FontStyle.Bold);
                if (r.NeedsAction && r.DaysToDue < 0)
                {
                    row.Cells[0].Style.ForeColor = UiStyle.Danger;
                }
            }

            _empty.Visible = rows.Count == 0;
            _empty.Text = _allRows.Count == 0
                ? "案件がまだありません。「案件を登録」から登録してください。"
                : (_search.Text.Trim().Length > 0 ? "検索に合う案件がありません。" : "この一覧に出す案件はありません。");

            _workspace.Text = "案件フォルダを作る場所:\r\n" + _services.WorkspaceRoot + (_services.IsDefaultWorkspace ? "（検証用のフォルダ）" : "");
        }

        private static void Tab(Button b, string text, bool selected)
        {
            b.Text = text;
            b.BackColor = selected ? UiStyle.Primary : Color.White;
            b.ForeColor = selected ? Color.White : UiStyle.Text;
            b.Font = new Font("BIZ UDPGothic", 11F, selected ? FontStyle.Bold : FontStyle.Regular);
        }

        private static string DayName(DateTime d)
        {
            return "日月火水木金土"[(int)d.DayOfWeek].ToString();
        }

        // ホーム一覧の行にマウスを置いて少し待つと、その案件の見積書の1ページ目を出す。
        private void ShowHover()
        {
            _hoverTimer.Stop();
            if (_hoverRow < 0 || _hoverRow >= _grid.Rows.Count)
            {
                return;
            }

            var r = _grid.Rows[_hoverRow].Tag as HomeRow;
            if (r == null)
            {
                return;
            }

            var file = HoverPreview.FindQuoteFile(Path.Combine(_services.WorkspaceRoot, r.Anken.FolderPath));
            if (file != null)
            {
                _hover.ShowFor(file, Cursor.Position);
            }
        }

        private void OpenReminderBatch()
        {
            var rows = Home.RemindCandidates(_allRows);
            if (rows.Count == 0)
            {
                MessageBox.Show(this, "今、催促が必要な案件はありません。\r\n（見積依頼を送ったのに未回答の調達先があり、回答期限が明日以前の案件を出します）",
                    "催促が必要な案件", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var dlg = new ReminderBatchForm(_services, rows))
            {
                dlg.ShowDialog(this);
            }
        }

        // 今の絞り込み結果を、Excelに書き出す。保存先は、保存画面で選ぶ。
        private void ExportList()
        {
            var rows = Home.Filter(_allRows, _filter, _search.Text);
            if (rows.Count == 0)
            {
                MessageBox.Show(this, "書き出す案件がありません。", "一覧をExcelに書き出す", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var dlg = new SaveFileDialog
            {
                Title = "一覧をExcelに書き出す（今の絞り込み結果 " + rows.Count + " 件）",
                Filter = "Excelブック (*.xlsx)|*.xlsx",
                FileName = "案件一覧" + DateTime.Today.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".xlsx",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                OverwritePrompt = true,
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    ExcelExports.WriteAnkenList(dlg.FileName, rows);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    MessageBox.Show(this, "書き出せませんでした。\r\n（Excelで開いたままのときは、閉じてからやり直してください）\r\n\r\n" + ex.Message,
                        "一覧をExcelに書き出す", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (MessageBox.Show(this, rows.Count + " 件を書き出しました。\r\n\r\n" + dlg.FileName + "\r\n\r\nExcelで開きますか？", "一覧をExcelに書き出す",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
                }
            }
        }

        // Ctrl+F: 検索へ、Ctrl+N: 案件を登録、F5: 読み直し
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.F))
            {
                _search.Focus();
                _search.SelectAll();
                return true;
            }

            if (keyData == (Keys.Control | Keys.N))
            {
                new RegisterForm(_services).ShowDialog(this);
                Reload();
                return true;
            }

            if (keyData == Keys.F5)
            {
                Reload();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        // 見積依頼を送ったのに回答が無い調達先を選んだ状態で、催促メールの画面を開く。
        private void Remind(HomeRow r)
        {
            using (var dlg = new ComposeMailForm(_services, r.Anken, MailKind.Reminder, r.RemindSupplierIds.ToList()))
            {
                dlg.ShowDialog(this);
            }

            Reload();
        }

        private void OpenDetail(int rowIndex)
        {
            var r = _grid.Rows[rowIndex].Tag as HomeRow;
            if (r == null)
            {
                return;
            }

            using (var dlg = new AnkenDetailForm(_services, r.Anken.Id))
            {
                dlg.ShowDialog(this);
            }

            Reload();
        }
    }
}
