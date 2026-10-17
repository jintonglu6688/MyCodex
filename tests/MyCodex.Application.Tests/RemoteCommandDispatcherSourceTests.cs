using System.IO;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class RemoteCommandDispatcherSourceTests
    {
        [Test]
        public void HostSettingsBehavior_LivesInFocusedPartial()
        {
            var directory = Path.GetFullPath(Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "..",
                "..",
                "..",
                "..",
                "src",
                "MyCodex.Application",
                "CodingRemote"));
            var main = File.ReadAllText(Path.Combine(directory, "RemoteCommandDispatcher.cs"));
            var focusedPath = Path.Combine(directory, "RemoteCommandDispatcher.HostSettings.cs");

            Assert.That(File.Exists(focusedPath), Is.True, "Focused host settings dispatcher partial should exist.");
            Assert.That(main, Does.Contain("public sealed partial class RemoteCommandDispatcher"));
            Assert.That(main, Does.Not.Contain("ParseHostSettingsUpdate"));
            Assert.That(main, Does.Not.Contain("MapHostSettings"));

            var focused = File.ReadAllText(focusedPath);
            Assert.That(focused, Does.Contain("partial class RemoteCommandDispatcher"));
            Assert.That(focused, Does.Contain("DispatchHostSettingsGetAsync"));
            Assert.That(focused, Does.Contain("DispatchHostSettingsUpdateAsync"));
            Assert.That(focused, Does.Contain("ParseHostSettingsUpdate"));
            Assert.That(focused, Does.Contain("MapHostSettings"));
        }
    }
}
