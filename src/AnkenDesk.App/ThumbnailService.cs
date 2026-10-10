using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using AnkenDesk.Core;

namespace AnkenDesk.App
{
    /// <summary>
    /// サムネイルの作成とキャッシュ（Work spaceの外）をまとめて扱う。ファイル一覧、ホームのマウスオーバー、
    /// ドロップ直後の先作りが、同じキャッシュを使う。例外は外に出さない（作れなければ枠の画像）。
    /// </summary>
    internal static class ThumbnailService
    {
        public const int TileW = 190;
        public const int TileH = 256; // ImageListの上限は256px

        public static readonly ThumbnailCache Cache = new ThumbnailCache(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AnkenDesk", "thumbs"));

        /// <summary>キャッシュがあればそれを、無ければ作って保存して返す。画像は呼び出し側が破棄する。</summary>
        public static ThumbnailResult GetOrMake(string path, int width, int height)
        {
            try
            {
                string cached;
                if (Cache.TryGet(path, width, out cached))
                {
                    using (var ms = new MemoryStream(File.ReadAllBytes(cached)))
                    using (var img = new Bitmap(ms))
                    {
                        return new ThumbnailResult { Image = new Bitmap(img), Note = "" };
                    }
                }

                var made = ThumbnailMaker.Make(path, width, height);
                using (var ms = new MemoryStream())
                {
                    made.Image.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    Cache.Store(path, width, ms.ToArray());
                }

                return made;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is System.Runtime.InteropServices.ExternalException)
            {
                return ThumbnailMaker.Placeholder(Path.GetExtension(path), width, height, "読めません");
            }
        }

        /// <summary>保存・ドロップした直後に、画像を先に作っておく（一覧を開いたときの待ちを減らす）。別スレッドで、画面は止めない。</summary>
        public static void WarmAsync(IEnumerable<string> paths)
        {
            var list = new List<string>(paths);
            if (list.Count == 0)
            {
                return;
            }

            var t = new Thread(() =>
            {
                foreach (var p in list)
                {
                    try
                    {
                        using (GetOrMake(p, TileW, TileH).Image)
                        {
                        }
                    }
                    catch (Exception)
                    {
                        // 先作りの失敗は、一覧を開いたときに作り直すだけ。
                    }
                }
            }) { IsBackground = true };
            t.Start();
        }
    }
}
