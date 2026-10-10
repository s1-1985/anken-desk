using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using AnkenDesk.Core;

namespace AnkenDesk.App
{
    /// <summary>
    /// ホームの一覧で、行にマウスを置くと、その案件の見積書の1ページ目を小さく出す。
    /// 画像は、ファイル一覧と同じキャッシュを使う。クリックを邪魔しないよう、フォーカスは奪わない。
    /// </summary>
    internal sealed class HoverPreview : Form
    {
        private const int W = 300;
        private const int H = 400;
        private readonly PictureBox _picture = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White };
        private readonly Label _caption = new Label { Dock = DockStyle.Bottom, Height = 26, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("BIZ UDPGothic", 9.5F) };
        private int _generation;

        public HoverPreview()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Size = new Size(W + 4, H + 30);
            BackColor = UiStyle.Primary;
            Padding = new Padding(2);
            Controls.Add(_picture);
            Controls.Add(_caption);
            _caption.BackColor = Color.White;
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        /// <summary>案件の見積書（5.調達先見積もり直下のPDFを優先、無ければ最初のファイル）。無ければnull。</summary>
        public static string? FindQuoteFile(string ankenFullPath)
        {
            try
            {
                var dir = QuoteFiles.QuoteDir(ankenFullPath);
                if (!Directory.Exists(dir))
                {
                    return null;
                }

                var files = Directory.GetFiles(dir).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
                return files.FirstOrDefault(f => string.Equals(Path.GetExtension(f), ".pdf", StringComparison.OrdinalIgnoreCase)) ?? files.FirstOrDefault();
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        public void ShowFor(string filePath, Point screenPos)
        {
            var gen = ++_generation;
            _caption.Text = "読み込み中…　" + Path.GetFileName(filePath);
            _picture.Image = null;
            var area = Screen.FromPoint(screenPos).WorkingArea;
            Location = new Point(Math.Min(screenPos.X + 24, area.Right - Width - 8), Math.Max(area.Top + 8, Math.Min(screenPos.Y - Height / 2, area.Bottom - Height - 8)));
            if (!Visible)
            {
                Show();
            }

            var t = new Thread(() =>
            {
                var made = ThumbnailService.GetOrMake(filePath, W, H);
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        if (gen != _generation || IsDisposed)
                        {
                            made.Image.Dispose();
                            return;
                        }

                        _picture.Image = made.Image;
                        _caption.Text = Path.GetFileName(filePath) + (made.Note.Length > 0 ? "　[" + made.Note + "]" : "");
                    }));
                }
                catch (InvalidOperationException)
                {
                    made.Image.Dispose();
                }
            }) { IsBackground = true };
            t.Start();
        }

        public void HidePreview()
        {
            _generation++;
            if (Visible)
            {
                Hide();
            }
        }
    }
}
