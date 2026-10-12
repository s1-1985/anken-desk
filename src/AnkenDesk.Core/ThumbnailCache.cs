using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace AnkenDesk.Core
{
    /// <summary>
    /// サムネイル画像のキャッシュの置き場と名前（画像そのものの生成は画面側）。
    /// 名前は「パス・サイズ・更新日時」から決める。ファイルが差し替わると名前が変わるので、古い画像は使われない。
    /// 置き場はDBと同じ側（Work spaceの外）。Work spaceには何も書かない。
    /// </summary>
    public sealed class ThumbnailCache
    {
        private readonly string _dir;

        public ThumbnailCache(string dir)
        {
            _dir = dir;
        }

        public string Directory
        {
            get { return _dir; }
        }

        /// <summary>この版のファイルの、キャッシュ画像のパス（まだ無いこともある）。</summary>
        public string PathFor(string filePath, int width)
        {
            var info = new FileInfo(filePath);
            var key = filePath.ToLowerInvariant() + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks + "|" + width;
            byte[] hash;
            using (var sha = SHA1.Create())
            {
                hash = sha.ComputeHash(Encoding.UTF8.GetBytes(key));
            }

            return System.IO.Path.Combine(_dir, BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant() + ".png");
        }

        public bool TryGet(string filePath, int width, out string cachedPath)
        {
            cachedPath = PathFor(filePath, width);
            return File.Exists(cachedPath);
        }

        /// <summary>画像を保存する（途中で止まっても壊れた画像が残らないよう、一時ファイル経由）。</summary>
        public void Store(string filePath, int width, byte[] png)
        {
            System.IO.Directory.CreateDirectory(_dir);
            var target = PathFor(filePath, width);
            var temp = target + ".tmp";
            File.WriteAllBytes(temp, png);
            if (File.Exists(target))
            {
                File.Delete(target);
            }

            File.Move(temp, target);
        }

        /// <summary>古い順に消して、<paramref name="maxFiles"/> 個以内にする。</summary>
        public int Prune(int maxFiles)
        {
            if (!System.IO.Directory.Exists(_dir))
            {
                return 0;
            }

            var files = new DirectoryInfo(_dir).GetFiles("*.png").OrderByDescending(f => f.LastWriteTimeUtc).ToList();
            var removed = 0;
            for (var i = maxFiles; i < files.Count; i++)
            {
                try
                {
                    files[i].Delete();
                    removed++;
                }
                catch (IOException)
                {
                }
            }

            return removed;
        }
    }
}
