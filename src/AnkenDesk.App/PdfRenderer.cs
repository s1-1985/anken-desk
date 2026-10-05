using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace AnkenDesk.App
{
    /// <summary>
    /// PDFのページを画像として取り出す。Windows標準のPDF描画エンジン(Windows.Data.Pdf)を使う。
    /// OSに含まれているため、ネイティブDLLを同梱しない。PDFsorterで会社PCで動いた方式をそのまま使う。
    /// </summary>
    internal sealed class PdfRenderer : IDisposable
    {
        private readonly string _path;
        private PdfDocument? _document;

        public PdfRenderer(string path)
        {
            _path = path;
        }

        /// <summary>ページ数。読み込みに失敗した場合は0。</summary>
        public int PageCount
        {
            get { return _document == null ? 0 : (int)_document.PageCount; }
        }

        /// <summary>読み込めなかった理由（パスワード付き・破損など）。成功時は null。</summary>
        public string? LoadError { get; private set; }

        /// <summary>
        /// PDFを開く。WinRTのAPIは非同期なので、UIスレッドから直接待つとデッドロックしうる。
        /// Task.Run でUIスレッドの外へ出してから待つ。
        /// </summary>
        public bool Load()
        {
            try
            {
                _document = Task.Run(async () =>
                {
                    var file = await StorageFile.GetFileFromPathAsync(_path);
                    return await PdfDocument.LoadFromFileAsync(file);
                }).GetAwaiter().GetResult();
                LoadError = null;
                return true;
            }
            catch (Exception ex)
            {
                _document = null;
                LoadError = ex.Message;
                return false;
            }
        }

        /// <summary>指定ページを、幅 targetWidth ピクセルの画像として描画する。失敗したら null。</summary>
        public Image? RenderPage(int pageIndex, int targetWidth)
        {
            var doc = _document;
            if (doc == null || pageIndex < 0 || pageIndex >= PageCount || targetWidth <= 0)
            {
                return null;
            }

            try
            {
                var bytes = Task.Run(async () =>
                {
                    using (var page = doc.GetPage((uint)pageIndex))
                    using (var ras = new InMemoryRandomAccessStream())
                    {
                        var options = new PdfPageRenderOptions { DestinationWidth = (uint)targetWidth };
                        await page.RenderToStreamAsync(ras, options);

                        using (var ms = new MemoryStream())
                        {
                            await ras.AsStreamForRead().CopyToAsync(ms);
                            return ms.ToArray();
                        }
                    }
                }).GetAwaiter().GetResult();

                // Image.FromStream はストリームを開いたまま参照し続けるので、閉じないストリームを渡す。
                return Image.FromStream(new MemoryStream(bytes));
            }
            catch (Exception)
            {
                return null;
            }
        }

        public void Dispose()
        {
            _document = null;
        }
    }
}
