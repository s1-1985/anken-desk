using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using AnkenDesk.Core;

namespace AnkenDesk.App
{
    /// <summary>
    /// 案件フォルダの中のファイルを、サムネイル（画像）で並べて見る。1つずつ開かずに、中身を見分けるための画面。
    /// 画像は、作ったらキャッシュ（Work spaceの外）に残すので、2回目からはすぐ出る。読み取りだけで、ファイルには触らない。
    /// </summary>
    internal sealed class FileGalleryForm : Form
    {
        private const int TileW = ThumbnailService.TileW;
        private const int TileH = ThumbnailService.TileH;
        private const int MaxFiles = 600;

        private readonly string _ankenFull;
        private readonly ListView _list = new ListView();
        private readonly ImageList _images = new ImageList { ImageSize = new Size(TileW, TileH), ColorDepth = ColorDepth.Depth24Bit };
        private readonly ComboBox _folder = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly Label _status = new Label();
        private readonly List<string> _paths = new List<string>();
        private volatile bool _closing;
        private int _generation;

        public FileGalleryForm(AppServices services, AnkenRecord anken)
        {
            _ankenFull = Path.Combine(services.WorkspaceRoot, anken.FolderPath);
            UiStyle.Apply(this);
            Text = "ファイルを画像で見る　" + Home.Title(anken);
            var h = Math.Min(900, Screen.PrimaryScreen.WorkingArea.Height - 60);
            ClientSize = new Size(1240, h);

            Controls.Add(new Label
            {
                Text = "ファイルを画像で見る",
                Font = new Font("BIZ UDPGothic", 18F, FontStyle.Bold),
                Left = 16, Top = 10, Width = 500, Height = 40,
            });

            Controls.Add(new Label { Text = "フォルダ", Left = 16, Top = 62, Width = 70, Height = 28 });
            _folder.SetBounds(90, 58, 330, 32);
            _folder.Items.Add("すべて");
            _folder.Items.AddRange(FolderNames.Subfolders);
            _folder.SelectedIndex = 0;
            _folder.SelectedIndexChanged += (s, e) => Load();
            Controls.Add(_folder);

            var open = UiStyle.CreateButton("開く", true, 130);
            open.Left = 440; open.Top = 54;
            open.Click += (s, e) => OpenSelected();
            var enlarge = UiStyle.CreateButton("PDFを大きく見る", false, 200);
            enlarge.Left = 580; enlarge.Top = 54;
            enlarge.Click += (s, e) => Enlarge();
            var reload = UiStyle.CreateButton("読み直す", false, 130);
            reload.Left = 790; reload.Top = 54;
            reload.Click += (s, e) => Load();
            Controls.AddRange(new Control[] { open, enlarge, reload });

            _status.SetBounds(16, 102, 1208, 26);
            _status.ForeColor = Color.FromArgb(90, 96, 100);
            Controls.Add(_status);

            _list.SetBounds(16, 132, 1208, h - 190);
            _list.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            _list.View = View.LargeIcon;
            _list.LargeImageList = _images;
            _list.MultiSelect = false;
            _list.HideSelection = false;
            _list.ShowGroups = true;
            _list.DoubleClick += (s, e) => OpenSelected();
            Controls.Add(_list);

            var close = UiStyle.CreateButton("閉じる", false, 140);
            close.Left = 1084; close.Top = h - 52;
            close.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            close.Click += (s, e) => Close();
            Controls.Add(close);

            Shown += (s, e) => Load();
            FormClosing += (s, e) => _closing = true;
        }

        private void Load()
        {
            var gen = ++_generation;
            _list.BeginUpdate();
            _list.Items.Clear();
            _list.Groups.Clear();
            _images.Images.Clear();
            _paths.Clear();

            var files = new List<string>();
            var subs = _folder.SelectedIndex <= 0 ? FolderNames.Subfolders : new[] { FolderNames.Subfolders[_folder.SelectedIndex - 1] };
            var truncated = false;
            foreach (var sub in subs)
            {
                var dir = Path.Combine(_ankenFull, sub);
                if (!Directory.Exists(dir))
                {
                    continue;
                }

                var inSub = SafeFiles(dir).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
                var group = new ListViewGroup(sub + "（" + inSub.Count + "）");
                _list.Groups.Add(group);
                foreach (var f in inSub)
                {
                    if (files.Count >= MaxFiles)
                    {
                        truncated = true;
                        break;
                    }

                    var name = Path.GetFileName(f);
                    var rel = f.Substring(Path.Combine(_ankenFull, sub).Length).TrimStart(Path.DirectorySeparatorChar);
                    _images.Images.Add(ThumbnailMaker.Placeholder(Path.GetExtension(f), TileW, TileH, "読み込み中…").Image);
                    var item = new ListViewItem(rel, files.Count) { Group = group, Tag = f, ToolTipText = f };
                    _list.Items.Add(item);
                    files.Add(f);
                    _paths.Add(f);
                }
            }

            _list.EndUpdate();
            _status.Text = files.Count == 0
                ? "ファイルがありません。"
                : files.Count + " 個のファイル" + (truncated ? "（多いので、最初の " + MaxFiles + " 個だけ）" : "") + "　画像を作っています…";

            if (files.Count == 0)
            {
                return;
            }

            ThumbnailService.Cache.Prune(3000);
            var worker = new Thread(() => MakeAll(gen, files)) { IsBackground = true };
            worker.SetApartmentState(ApartmentState.MTA);
            worker.Start();
        }

        // 別スレッドで、1つずつ画像を作る（キャッシュがあればそれを使う）。画面への反映は、UIスレッドで行う。
        private void MakeAll(int gen, List<string> files)
        {
            for (var i = 0; i < files.Count; i++)
            {
                if (_closing || gen != _generation)
                {
                    return;
                }

                var made = ThumbnailService.GetOrMake(files[i], TileW, TileH);
                var bmp = made.Image;
                var note = made.Note;

                var index = i;
                var shown = bmp;
                var text = note;
                try
                {
                    BeginInvoke((Action)(() => Apply(gen, index, shown, text, files.Count)));
                }
                catch (InvalidOperationException)
                {
                    return; // 画面が閉じられた
                }
            }
        }

        private void Apply(int gen, int index, Bitmap bmp, string note, int total)
        {
            if (gen != _generation || index >= _images.Images.Count || index >= _list.Items.Count)
            {
                return;
            }

            _images.Images[index] = bmp;
            var item = _list.Items[index];
            if (note.Length > 0)
            {
                item.Text = item.Text + "　[" + note + "]";
            }

            _list.RedrawItems(index, index, false);
            _status.Text = total + " 個のファイル　" + (index + 1 < total ? (index + 1) + " 個目まで作成済み…" : "すべて表示しました。ダブルクリックで開きます。");
        }

        private string? SelectedPath()
        {
            return _list.SelectedItems.Count == 0 ? null : _list.SelectedItems[0].Tag as string;
        }

        private void OpenSelected()
        {
            var path = SelectedPath();
            if (path == null)
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                MessageBox.Show(this, "開けませんでした。\r\n\r\n" + ex.Message, "案件デスク", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void Enlarge()
        {
            var path = SelectedPath();
            if (path == null || !string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this, "PDFのファイルを選んでください。", "案件デスク", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var items = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>(Path.GetFileName(path), path) };
            using (var dlg = new PdfPreviewForm(items))
            {
                dlg.ShowDialog(this);
            }
        }

        private static IEnumerable<string> SafeFiles(string dir)
        {
            try
            {
                return Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
            }
            catch (IOException)
            {
                return new string[0];
            }
            catch (UnauthorizedAccessException)
            {
                return new string[0];
            }
        }
    }
}
