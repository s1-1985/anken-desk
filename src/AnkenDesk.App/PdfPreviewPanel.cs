using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace AnkenDesk.App
{
    /// <summary>
    /// 案件画面の右に置く、PDFの小さなプレビュー欄。ページ送りつき。
    /// 描画は別スレッドで行い、画面は止めない（連続して切り替えたときは、最後に選んだものだけ出す）。
    /// </summary>
    internal sealed class PdfPreviewPanel : Panel
    {
        private readonly PictureBox _picture = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, Dock = DockStyle.Fill, BackColor = Color.White };
        private readonly Label _caption = new Label { Dock = DockStyle.Top, Height = 54, Padding = new Padding(6, 4, 6, 0), Font = new Font("BIZ UDPGothic", 10.5F) };
        private readonly Label _page = new Label { TextAlign = ContentAlignment.MiddleCenter, Width = 100, Height = 40 };
        private readonly Button _prev = UiStyle.CreateButton("◀", false, 56);
        private readonly Button _next = UiStyle.CreateButton("▶", false, 56);
        private string? _path;
        private int _index;
        private int _count;
        private int _generation;

        public PdfPreviewPanel()
        {
            BackColor = Color.White;
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 48 };
            _prev.Left = 6; _prev.Top = 4;
            _page.Left = 66; _page.Top = 4;
            _next.Left = 172; _next.Top = 4;
            _prev.Click += (s, e) => Go(-1);
            _next.Click += (s, e) => Go(1);
            bottom.Controls.AddRange(new Control[] { _prev, _page, _next });
            Controls.Add(_picture);
            Controls.Add(bottom);
            Controls.Add(_caption);
            Clear("調達先の列を選ぶと、見積書（PDF）の1ページ目がここに出ます。");
        }

        public void Clear(string message)
        {
            _generation++;
            _path = null;
            _count = 0;
            _picture.Image = null;
            _caption.Text = message;
            _page.Text = "";
            _prev.Enabled = _next.Enabled = false;
        }

        /// <summary>PDFを表示する（1ページ目から）。nullなら、案内の文を出す。</summary>
        public void ShowPdf(string? path, string noneMessage)
        {
            if (path == null)
            {
                Clear(noneMessage);
                return;
            }

            if (string.Equals(path, _path, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _path = path;
            _index = 0;
            _count = 0;
            _caption.Text = Path.GetFileName(path);
            Render();
        }

        private void Go(int delta)
        {
            var next = _index + delta;
            if (_path == null || next < 0 || next >= _count)
            {
                return;
            }

            _index = next;
            Render();
        }

        private void Render()
        {
            var gen = ++_generation;
            var path = _path!;
            var index = _index;
            var width = Math.Max(200, _picture.Width * 2);
            _page.Text = "読み込み中…";
            _prev.Enabled = _next.Enabled = false;

            var t = new Thread(() =>
            {
                Image? img = null;
                var count = 0;
                string? error = null;
                try
                {
                    using (var r = new PdfRenderer(path))
                    {
                        if (r.Load())
                        {
                            count = r.PageCount;
                            img = r.RenderPage(Math.Min(index, Math.Max(0, count - 1)), width);
                        }
                        else
                        {
                            error = r.LoadError;
                        }
                    }
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                try
                {
                    BeginInvoke((Action)(() => Done(gen, img, count, error)));
                }
                catch (InvalidOperationException)
                {
                    img?.Dispose(); // 画面が閉じられた
                }
            }) { IsBackground = true };
            t.Start();
        }

        private void Done(int gen, Image? img, int count, string? error)
        {
            if (gen != _generation)
            {
                img?.Dispose();
                return;
            }

            _count = count;
            _picture.Image = img;
            if (img == null)
            {
                _page.Text = "";
                _caption.Text = Path.GetFileName(_path ?? "") + "\r\n開けませんでした" + (string.IsNullOrEmpty(error) ? "" : "（" + error + "）");
                return;
            }

            _page.Text = (_index + 1) + " / " + count;
            _prev.Enabled = _index > 0;
            _next.Enabled = _index + 1 < count;
        }
    }
}
