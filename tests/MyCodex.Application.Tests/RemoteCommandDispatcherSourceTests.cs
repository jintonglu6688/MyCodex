using System.IO;
using MyCodex.Application.Tests.SourceGuards;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [Category(SourceGuardCategory.SourceBehavior)]
    [TestFixture]
    public sealed class RemoteCommandDispatcherSourceTests
    {
        [Test]
        public void HostSettingsBehavior_LivesInFocusedPartial()
        {
            var main = RepositorySource.ReadSourceFile("RemoteCommandDispatcher.cs");
            var focusedPath = RepositorySource.GetPath("src", "MyCodex.Application", "CodingRemote", "RemoteCommandDispatcher.HostSettings.cs");

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
