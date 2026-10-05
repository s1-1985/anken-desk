using System;
using System.IO;
using AnkenDesk.Core;
using Xunit;

namespace AnkenDesk.Core.Tests
{
    public class DbSmokeTestTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "AnkenDeskTest-" + Guid.NewGuid().ToString("N"));

        [Fact]
        public void Run_は新しいフォルダにDBを作って書き込める()
        {
            var path = Path.Combine(_dir, "sub", "smoke.db");

            var result = DbSmokeTest.Run(path);

            Assert.True(File.Exists(path));
            Assert.Contains("結果: OK", result);
            Assert.Contains("書き込み回数: 1", result);
        }

        [Fact]
        public void Run_は繰り返すと書き込み回数が増える()
        {
            var path = Path.Combine(_dir, "smoke.db");

            DbSmokeTest.Run(path);
            var result = DbSmokeTest.Run(path);

            Assert.Contains("書き込み回数: 2", result);
        }

        public void Dispose()
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, true);
            }
        }
    }
}
