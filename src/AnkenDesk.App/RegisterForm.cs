using System;
using System.Linq;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using AnkenDesk.Core;

namespace AnkenDesk.App
{
    /// <summary>案件を登録する（HANDOFF.md §5.4）。フォルダ名と15個のサブフォルダを確認してから、フォルダを作ってDBに保存する。</summary>
    internal sealed class RegisterForm : Form
    {
        private static readonly string[] BasicItemNames = QuoteRequestItems.BasicNames;
        private static readonly string[] ExtraItemNames = QuoteRequestItems.ExtraNames;
        private static readonly string[] MultilineItems = { "検査内容", "備考" };
        private static readonly string[] QuantityKinds = { "試作", "量産", "年間見込数" };
        private static readonly string[] QuantityUnits = { "個/Lot", "個", "個/年" };

        private sealed class QtyRow
        {
            public Panel Panel = null!;
            public ComboBox Kind = null!;
            public NumericUpDown Qty = null!;
            public ComboBox Unit = null!;
        }

        private readonly AppServices _services;
        private readonly AnkenRegistrar _registrar;

        private readonly ComboBox _client = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly DateTimePicker _requestDate = new DateTimePicker { Format = DateTimePickerFormat.Short };
        private readonly TextBox _part = new TextBox();
        private readonly TextBox _partName = new TextBox();
        private readonly TextBox _note = new TextBox();
        private readonly DateTimePicker _dueDate = new DateTimePicker { Format = DateTimePickerFormat.Short };
        private readonly FlowLayoutPanel _qtyRows = new FlowLayoutPanel();
        private readonly List<QtyRow> _qtyList = new List<QtyRow>();
        private readonly Dictionary<string, TextBox> _items = new Dictionary<string, TextBox>();
        private readonly Panel _extraPanel = new Panel();

        private readonly Label _pathLabel = new Label();
        private readonly Label _warn = new Label();
        private readonly Button _register;
        private readonly ListBox _dropList = new ListBox();
        private readonly List<string> _dropFiles = new List<string>();

        public RegisterForm(AppServices services)
        {
            _services = services;
            _registrar = services.CreateRegistrar();
            UiStyle.Apply(this);
            Text = "案件を登録";
            ClientSize = new Size(1180, 780);

            var title = new Label
            {
                Text = "案件を登録",
                Font = new Font("BIZ UDPGothic", 18F, FontStyle.Bold),
                Left = 16, Top = 12, Width = 400, Height = 40,
            };

            var left = new FlowLayoutPanel
            {
                Left = 16, Top = 60, Width = 720, Height = 640,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                BackColor = Color.White,
                Padding = new Padding(12),
            };
            left.Controls.Add(BuildBasicBlock());
            left.Controls.Add(BuildQuantityBlock());
            left.Controls.Add(BuildItemsBlock());

            var right = BuildPreviewBlock();

            var cancel = UiStyle.CreateButton("キャンセル", false, 140);
            cancel.Left = 760; cancel.Top = 724;
            cancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            cancel.Click += (s, e) => DialogResult = DialogResult.Cancel;

            _register = UiStyle.CreateButton("登録してフォルダを作成", true, 260);
            _register.Left = 908; _register.Top = 724;
            _register.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _register.Click += (s, e) => DoRegister();

            Controls.AddRange(new Control[] { title, left, right, cancel, _register });

            _requestDate.Value = DateTime.Today;
            _dueDate.Value = DateTime.Today.AddDays(7);
            LoadClients();
            AddQuantityRow("試作", 1, "個/Lot");
            foreach (var kv in _services.Db.GetItemDefaults())
            {
                TextBox box;
                if (_items.TryGetValue(kv.Key, out box))
                {
                    box.Text = kv.Value.Replace("\r\n", "\n").Replace("\n", "\r\n");
                }
            }

            _client.SelectedIndexChanged += (s, e) => UpdatePreview();
            _requestDate.ValueChanged += (s, e) => UpdatePreview();
            _part.TextChanged += (s, e) => UpdatePreview();
            _note.TextChanged += (s, e) => UpdatePreview();
            UpdatePreview();
        }

        // ---- 画面の組み立て ----

        private Panel BuildBasicBlock()
        {
            var p = new Panel { Width = 680, Height = 250 };
            p.Controls.Add(Header("基本情報", 0, 0));

            p.Controls.Add(Caption("得意先・種別", 0, 34));
            _client.SetBounds(0, 58, 280, 32);
            p.Controls.Add(_client);
            var manage = UiStyle.CreateButton("管理...", false, 90);
            manage.Left = 286; manage.Top = 54;
            manage.Click += (s, e) =>
            {
                new ClientsForm(_services).ShowDialog(this);
                LoadClients();
            };
            p.Controls.Add(manage);

            p.Controls.Add(Caption("依頼日", 400, 34));
            _requestDate.SetBounds(400, 58, 200, 32);
            p.Controls.Add(_requestDate);

            p.Controls.Add(Caption("品番", 0, 104));
            _part.SetBounds(0, 128, 380, 32);
            p.Controls.Add(_part);

            p.Controls.Add(Caption("品名", 400, 104));
            _partName.SetBounds(400, 128, 270, 32);
            p.Controls.Add(_partName);

            p.Controls.Add(Caption("備考（フォルダ名の末尾に付きます。例: 増面型、可動、Lot5）", 0, 174));
            _note.SetBounds(0, 198, 380, 32);
            p.Controls.Add(_note);

            p.Controls.Add(Caption("回答希望期日", 400, 174));
            _dueDate.SetBounds(400, 198, 200, 32);
            p.Controls.Add(_dueDate);
            return p;
        }

        private Panel BuildQuantityBlock()
        {
            var p = new Panel { Width = 680, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            var stack = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
            };
            stack.Controls.Add(Header("見積依頼数量", 0, 0, true));

            _qtyRows.FlowDirection = FlowDirection.TopDown;
            _qtyRows.WrapContents = false;
            _qtyRows.AutoSize = true;
            _qtyRows.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            stack.Controls.Add(_qtyRows);

            var add = UiStyle.CreateButton("数量パターンを追加", false, 240);
            add.Click += (s, e) => AddQuantityRow("試作", 1, "個/Lot");
            stack.Controls.Add(add);

            p.Controls.Add(stack);
            return p;
        }

        private Panel BuildItemsBlock()
        {
            var p = new Panel { Width = 680, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            var stack = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
            };
            stack.Controls.Add(Header("見積依頼書に載せる項目", 0, 0, true));
            stack.Controls.Add(FieldTable(BasicItemNames));

            var toggle = UiStyle.CreateButton("ほか" + ExtraItemNames.Length + "項目を入力（" + string.Join("・", ExtraItemNames) + "）", false, 640);
            toggle.Click += (s, e) => _extraPanel.Visible = !_extraPanel.Visible;
            stack.Controls.Add(toggle);

            _extraPanel.AutoSize = true;
            _extraPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _extraPanel.Visible = false;
            _extraPanel.Controls.Add(FieldTable(ExtraItemNames));
            stack.Controls.Add(_extraPanel);

            p.Controls.Add(stack);
            return p;
        }

        private TableLayoutPanel FieldTable(string[] names)
        {
            var t = new TableLayoutPanel
            {
                ColumnCount = 2,
                RowCount = names.Length,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Width = 650,
            };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 490));
            foreach (var name in names)
            {
                var tb = new TextBox { Width = 480, Margin = new Padding(0, 4, 0, 4) };
                if (Array.IndexOf(MultilineItems, name) >= 0)
                {
                    tb.Multiline = true;
                    tb.AcceptsReturn = true;
                    tb.Height = 64;
                    tb.ScrollBars = ScrollBars.Vertical;
                }

                _items[name] = tb;
                t.Controls.Add(new Label { Text = name, AutoSize = false, Width = 146, Height = 32, TextAlign = ContentAlignment.MiddleLeft });
                t.Controls.Add(tb);
            }

            return t;
        }

        private Panel BuildPreviewBlock()
        {
            var p = new Panel
            {
                Left = 752, Top = 60, Width = 412, Height = 640,
                BackColor = Color.White,
                Padding = new Padding(12),
            };
            p.Controls.Add(Header("作成されるフォルダ", 0, 0));

            _pathLabel.SetBounds(0, 36, 388, 92);
            _pathLabel.Font = new Font("BIZ UDPGothic", 12F, FontStyle.Bold);
            _pathLabel.ForeColor = UiStyle.Primary;
            p.Controls.Add(_pathLabel);

            _warn.SetBounds(0, 132, 388, 66);
            _warn.ForeColor = UiStyle.Danger;
            _warn.Font = new Font("BIZ UDPGothic", 11F, FontStyle.Bold);
            p.Controls.Add(_warn);

            var list = new ListBox { Left = 0, Top = 204, Width = 388, Height = 150, IntegralHeight = false, SelectionMode = SelectionMode.None, TabStop = false };
            list.Items.AddRange(FolderNames.Subfolders);
            p.Controls.Add(list);

            p.Controls.Add(new Label { Text = "客先の依頼ファイル（ここへドロップ）", Left = 0, Top = 360, Width = 388, Height = 24 });
            _dropList.SetBounds(0, 386, 388, 108);
            _dropList.IntegralHeight = false;
            _dropList.AllowDrop = true;
            _dropList.DragEnter += (s, e) => e.Effect = e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            _dropList.DragDrop += (s, e) =>
            {
                var dropped = e.Data == null ? null : e.Data.GetData(DataFormats.FileDrop) as string[];
                if (dropped != null)
                {
                    AddDropped(dropped);
                }
            };
            p.Controls.Add(_dropList);

            var addFile = UiStyle.CreateButton("ファイルを追加...", false, 190);
            addFile.Left = 0; addFile.Top = 498;
            addFile.Click += (s, e) =>
            {
                using (var dlg = new OpenFileDialog { Multiselect = true, Title = "客先の依頼ファイルを選ぶ" })
                {
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        AddDropped(dlg.FileNames);
                    }
                }
            };
            var removeFile = UiStyle.CreateButton("選んだものを外す", false, 190);
            removeFile.Left = 198; removeFile.Top = 498;
            removeFile.Click += (s, e) =>
            {
                if (_dropList.SelectedIndex >= 0)
                {
                    _dropFiles.RemoveAt(_dropList.SelectedIndex);
                    ShowDropped();
                }
            };
            p.Controls.AddRange(new Control[] { addFile, removeFile });

            var root = new Label
            {
                Left = 0, Top = 546, Width = 388, Height = 80,
                Text = "作成する場所:\r\n" + _services.WorkspaceRoot + (_services.IsDefaultWorkspace ? "（検証用のフォルダ）" : ""),
            };
            p.Controls.Add(root);
            return p;
        }

        private static Label Header(string text, int x, int y, bool withTopMargin = false)
        {
            return new Label
            {
                Text = text,
                Left = x, Top = y, Width = 640, Height = 30,
                Font = new Font("BIZ UDPGothic", 13F, FontStyle.Bold),
                Margin = new Padding(0, withTopMargin ? 12 : 0, 0, 4),
            };
        }

        private static Label Caption(string text, int x, int y)
        {
            return new Label { Text = text, Left = x, Top = y, Width = 380, Height = 22 };
        }

        // ---- 数量の行 ----

        private void AddQuantityRow(string kind, decimal qty, string unit)
        {
            var row = new QtyRow();
            row.Panel = new Panel { Width = 650, Height = 48 };
            row.Kind = new ComboBox { Left = 0, Top = 6, Width = 170, DropDownStyle = ComboBoxStyle.DropDown, Text = kind };
            row.Kind.Items.AddRange(QuantityKinds);
            row.Qty = new NumericUpDown
            {
                Left = 184, Top = 6, Width = 160,
                Minimum = 0, Maximum = 99999999, DecimalPlaces = 0, ThousandsSeparator = true, Value = qty,
            };
            row.Unit = new ComboBox { Left = 358, Top = 6, Width = 130, DropDownStyle = ComboBoxStyle.DropDown, Text = unit };
            row.Unit.Items.AddRange(QuantityUnits);
            var del = UiStyle.CreateButton("削除", false, 100);
            del.Left = 502; del.Top = 2;
            del.Click += (s, e) =>
            {
                _qtyList.Remove(row);
                _qtyRows.Controls.Remove(row.Panel);
                row.Panel.Dispose();
            };
            row.Panel.Controls.AddRange(new Control[] { row.Kind, row.Qty, row.Unit, del });
            _qtyList.Add(row);
            _qtyRows.Controls.Add(row.Panel);
        }

        // ---- 状態の更新 ----

        private void LoadClients()
        {
            var selected = _client.SelectedItem as Client;
            _client.DisplayMember = "Name";
            _client.DataSource = new List<Client>(_services.Db.ListClients());
            if (selected != null)
            {
                foreach (Client c in _client.Items)
                {
                    if (c.Id == selected.Id)
                    {
                        _client.SelectedItem = c;
                    }
                }
            }
        }

        // 登録できない理由があれば文字で返す（登録ボタンも使えなくする）。
        private string UpdatePreview()
        {
            var client = _client.SelectedItem as Client;
            var part = _part.Text.Trim();
            string problem = "";

            if (client == null)
            {
                _pathLabel.Text = "（得意先・種別が未登録です）";
                problem = "得意先・種別がありません。「管理...」から登録してください。";
            }
            else if (part.Length == 0)
            {
                _pathLabel.Text = client.Name + "\r\n（品番を入力してください）";
                problem = "品番を入力してください。";
            }
            else
            {
                var rel = AnkenRegistrar.RelativePath(client, _requestDate.Value, part, _note.Text);
                _pathLabel.Text = rel.Replace(Path.DirectorySeparatorChar.ToString(), "\r\n  └ ");
                if (!Directory.Exists(_services.WorkspaceRoot))
                {
                    problem = "Work spaceのフォルダが見つかりません。設定で場所を確認してください。";
                }
                else if (_registrar.FolderExists(rel))
                {
                    problem = "同じ名前のフォルダが既にあります。備考などを変えてください。";
                }
            }

            _warn.Text = problem.Length == 0 ? "" : "警告: " + problem;
            _register.Enabled = problem.Length == 0;
            return problem;
        }

        // ---- 客先の依頼ファイル ----

        private void AddDropped(IEnumerable<string> paths)
        {
            foreach (var path in paths)
            {
                if (File.Exists(path) && !_dropFiles.Contains(path, StringComparer.OrdinalIgnoreCase))
                {
                    _dropFiles.Add(path);
                }
            }

            ShowDropped();
        }

        private void ShowDropped()
        {
            _dropList.Items.Clear();
            foreach (var f in _dropFiles)
            {
                var name = Path.GetFileName(f);
                _dropList.Items.Add(name + "　→　" + RequestFiles.TargetSubfolder(name));
            }
        }

        // ---- 登録 ----

        private void DoRegister()
        {
            if (UpdatePreview().Length != 0)
            {
                return;
            }

            if (_dueDate.Value.Date < _requestDate.Value.Date)
            {
                MessageBox.Show(this, "回答希望期日が、依頼日より前になっています。", "案件を登録", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var client = (Client)_client.SelectedItem;
            var input = new AnkenInput
            {
                ClientId = client.Id,
                RequestDate = _requestDate.Value.Date,
                PartNumber = _part.Text,
                PartName = _partName.Text,
                Note = _note.Text,
                ReplyDueDate = _dueDate.Value.Date,
            };
            foreach (var q in _qtyList)
            {
                input.Quantities.Add(new QuantityPattern { Kind = q.Kind.Text.Trim(), Quantity = q.Qty.Value, Unit = q.Unit.Text.Trim() });
            }

            foreach (var kv in _items)
            {
                input.Items[kv.Key] = kv.Value.Text;
            }

            try
            {
                var rec = _registrar.Register(input);
                var msg = "登録しました。\r\n\r\n" + _registrar.FullPath(rec.FolderPath);
                var icon = MessageBoxIcon.Information;
                if (_dropFiles.Count > 0)
                {
                    var copied = RequestFiles.CopyInto(_registrar.FullPath(rec.FolderPath), _dropFiles);
                    var failed = copied.Where(c => c.Destination == null).ToList();
                    msg += "\r\n\r\n依頼ファイルを " + (copied.Count - failed.Count) + " 件コピーしました。";
                    if (failed.Count > 0)
                    {
                        icon = MessageBoxIcon.Warning;
                        msg += "\r\nコピーできなかったもの:\r\n" + string.Join("\r\n", failed.Select(f => Path.GetFileName(f.Source) + "（" + f.Error + "）"));
                    }
                }

                MessageBox.Show(this, msg, "案件を登録", MessageBoxButtons.OK, icon);
                DialogResult = DialogResult.OK;
            }
            catch (DuplicateFolderException)
            {
                UpdatePreview();
                MessageBox.Show(this, "同じ名前のフォルダが既にあります。備考などを変えてください。", "案件を登録", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is ArgumentException)
            {
                MessageBox.Show(this, "登録できませんでした。\r\n\r\n" + ex.Message, "案件を登録", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
