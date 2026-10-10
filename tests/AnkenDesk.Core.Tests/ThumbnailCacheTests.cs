using System;
using System.IO;
using Xunit;

namespace AnkenDesk.Core.Tests
{
    public class ThumbnailCacheTests
    {
        [Fact]
        public void Name_changes_when_file_changes_and_store_roundtrips()
        {
            using (var t = new TempDir())
            {
                var f = Path.Combine(t.Path, "a.pdf");
                File.WriteAllText(f, "1");
                var cache = new ThumbnailCache(Path.Combine(t.Path, "cache"));

                string p;
                Assert.False(cache.TryGet(f, 240, out p));
                cache.Store(f, 240, new byte[] { 1, 2, 3 });
                Assert.True(cache.TryGet(f, 240, out p));
                Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(p));
                Assert.False(cache.TryGet(f, 360, out p)); // 幅が違えば別

                var before = cache.PathFor(f, 240);
                File.WriteAllText(f, "22");
                File.SetLastWriteTimeUtc(f, DateTime.UtcNow.AddMinutes(5));
                Assert.NotEqual(before, cache.PathFor(f, 240));
                Assert.False(cache.TryGet(f, 240, out p)); // 差し替えたら使わない
                Assert.Empty(Directory.GetFiles(cache.Directory, "*.tmp"));
            }
        }

        [Fact]
        public void Prune_keeps_newest()
        {
            using (var t = new TempDir())
            {
                var cache = new ThumbnailCache(Path.Combine(t.Path, "c"));
                var files = new string[4];
                for (var i = 0; i < 4; i++)
                {
                    files[i] = Path.Combine(t.Path, "f" + i + ".pdf");
                    File.WriteAllText(files[i], "x" + i);
                    cache.Store(files[i], 100, new byte[] { 1 });
                    File.SetLastWriteTimeUtc(cache.PathFor(files[i], 100), new DateTime(2026, 1, 1 + i, 0, 0, 0, DateTimeKind.Utc));
                }

                Assert.Equal(2, cache.Prune(2));
                string p;
                Assert.False(cache.TryGet(files[0], 100, out p));
                Assert.False(cache.TryGet(files[1], 100, out p));
                Assert.True(cache.TryGet(files[2], 100, out p));
                Assert.True(cache.TryGet(files[3], 100, out p));
            }
        }
    }
}
