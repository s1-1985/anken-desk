using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace AnkenDesk.App
{
    /// <summary>見積書のプレビュー。PDFはページを送って見られる。PDF以外（Excelなど）は、Windowsの標準アプリで開く。</summary>
    internal sealed class PdfPreviewForm : Form
    {
        private readonly List<KeyValuePair<string, string>> _items;
        private readonly ComboBox _files = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly Panel _scroll = new Panel { AutoScroll = true, BackColor = Color.FromArgb(225, 226, 222) };
        private readonly PictureBox _picture = new PictureBox { SizeMode = PictureBoxSizeMode.AutoSize, Left = 12, Top = 12 };
        private readonly Label _message = new Label { AutoSize = false, Left = 12, Top = 12, Width = 700, Height = 120, Visible = false };
        private readonly Label _pageLabel = new Label { AutoSize = false, Width = 140, Height = 40, TextAlign = ContentAlignment.MiddleCenter };

        private PdfRenderer? _renderer;
        private int _page;

        /// <param name="items">表示名とフルパスの組。</param>
        public PdfPreviewForm(IEnumerable<KeyValuePair<string, string>> items)
        {
            _items = new List<KeyValuePair<string, string>>(items);
            UiStyle.Apply(this);
            Text = "見積書のプレビュー";
            var h = Math.Min(980, Screen.PrimaryScreen.WorkingArea.Height - 60);
            ClientSize = new Size(900, h);

            _files.SetBounds(12, 12, 460, 32);
            foreach (var it in _items)
            {
                _files.Items.Add(it.Key);
            }

            _files.SelectedIndexChanged += (s, e) => OpenSelected();

            var prev = UiStyle.CreateButton("前のページ", false, 120);
            prev.Left = 484; prev.Top = 8;
            prev.Click += (s, e) => Go(-1);
            _pageLabel.Left = 610; _pageLabel.Top = 8;
            var next = UiStyle.CreateButton("次のページ", false, 120);
            next.Left = 756; next.Top = 8;
            next.Click += (s, e) => Go(1);

            var openExternal = UiStyle.CreateButton("Windowsの標準アプリで開く", false, 280);
            openExternal.Left = 12; openExternal.Top = 56;
            openExternal.Click += (s, e) => OpenExternal();
            var close = UiStyle.CreateButton("閉じる", false, 120);
            close.Left = 768; close.Top = 56;
            close.Click += (s, e) => Close();

            _scroll.SetBounds(12, 104, 876, h - 116);
            _scroll.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            _scroll.Controls.Add(_picture);
            _scroll.Controls.Add(_message);

            Controls.AddRange(new Control[] { _files, prev, _pageLabel, next, openExternal, close, _scroll });
            _scroll.Resize += (s, e) => Render();
            Shown += (s, e) =>
            {
                if (_files.Items.Count > 0)
                {
                    _files.SelectedIndex = 0;
                }
            };
        }

        private string? CurrentPath()
        {
            return _files.SelectedIndex < 0 ? null : _items[_files.SelectedIndex].Value;
        }

        private void OpenSelected()
        {
            if (_renderer != null)
            {
                _renderer.Dispose();
                _renderer = null;
            }

            _page = 0;
            var path = CurrentPath();
            if (path == null)
            {
                return;
            }

            if (!string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase))
            {
                Show("PDF以外のファイルです。プレビューできません。\r\n「Windowsの標準アプリで開く」を押してください。");
                return;
            }

            var r = new PdfRenderer(path);
            if (!r.Load())
            {
                Show("PDFを読み込めませんでした（パスワード付き、または壊れている可能性があります）。\r\n" + r.LoadError
                    + "\r\n「Windowsの標準アプリで開く」を試してください。");
                return;
            }

            _renderer = r;
            Render();
        }

        private void Show(string text)
        {
            _picture.Image = null;
            _picture.Visible = false;
            _message.Text = text;
            _message.Visible = true;
            _pageLabel.Text = "";
        }

        private void Render()
        {
            if (_renderer == null || _renderer.PageCount == 0)
            {
                return;
            }

            _message.Visible = false;
            var width = Math.Max(400, _scroll.ClientSize.Width - 40);
            var img = _renderer.RenderPage(_page, width);
            if (img == null)
            {
                Show("このページを描画できませんでした。「Windowsの標準アプリで開く」を試してください。");
                return;
            }

            _picture.Image = img;
            _picture.Visible = true;
            _pageLabel.Text = (_page + 1) + " / " + _renderer.PageCount + " ページ";
        }

        private void Go(int delta)
        {
            if (_renderer == null)
            {
                return;
            }

            var next = _page + delta;
            if (next < 0 || next >= _renderer.PageCount)
            {
                return;
            }

            _page = next;
            _scroll.AutoScrollPosition = new Point(0, 0);
            Render();
        }

        private void OpenExternal()
        {
            var path = CurrentPath();
            if (path == null || !File.Exists(path))
            {
                MessageBox.Show(this, "ファイルが見つかりません。", "見積書のプレビュー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _renderer != null)
            {
                _renderer.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
