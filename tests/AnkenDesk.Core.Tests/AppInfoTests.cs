using AnkenDesk.Core;
using Xunit;

namespace AnkenDesk.Core.Tests
{
    public class AppInfoTests
    {
        [Fact]
        public void Title_は名前とバージョンを含む()
        {
            Assert.Contains(AppInfo.Name, AppInfo.Title);
            Assert.Contains(AppInfo.Version, AppInfo.Title);
        }
    }
}
