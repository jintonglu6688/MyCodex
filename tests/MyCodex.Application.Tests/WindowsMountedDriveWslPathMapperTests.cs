using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public class WindowsMountedDriveWslPathMapperTests
    {
        [Test]
        public void HostPlatform_IsWsl()
        {
            var mapper = new WindowsMountedDriveWslPathMapper();

            Assert.That(mapper.HostPlatform, Is.EqualTo(AppServerHostPlatform.Wsl));
        }

        [Test]
        public void TryMapToServerPath_MapsWindowsDrivePathToMntPath()
        {
            var mapper = new WindowsMountedDriveWslPathMapper();

            var mapped = mapper.TryMapToServerPath(@"E:\Projects\demo folder", out var serverPath);

            Assert.That(mapped, Is.True);
            Assert.That(serverPath, Is.EqualTo("/mnt/e/Projects/demo folder"));
        }

        [Test]
        public void TryMapToServerPath_MapsForwardSlashWindowsPathToMntPath()
        {
            var mapper = new WindowsMountedDriveWslPathMapper();

            var mapped = mapper.TryMapToServerPath("D:/repo/src", out var serverPath);

            Assert.That(mapped, Is.True);
            Assert.That(serverPath, Is.EqualTo("/mnt/d/repo/src"));
        }

        [Test]
        public void TryMapToServerPath_RemovesLongPathPrefix()
        {
            var mapper = new WindowsMountedDriveWslPathMapper();

            var mapped = mapper.TryMapToServerPath(@"\\?\C:\Temp\Repo", out var serverPath);

            Assert.That(mapped, Is.True);
            Assert.That(serverPath, Is.EqualTo("/mnt/c/Temp/Repo"));
        }

        [Test]
        public void TryMapToServerPath_RejectsRelativePath()
        {
            var mapper = new WindowsMountedDriveWslPathMapper();

            var mapped = mapper.TryMapToServerPath(@"repo\src", out var serverPath);

            Assert.That(mapped, Is.False);
            Assert.That(serverPath, Is.Null);
        }

        [Test]
        public void TryMapToClientPath_MapsMntPathToWindowsDrivePath()
        {
            var mapper = new WindowsMountedDriveWslPathMapper();

            var mapped = mapper.TryMapToClientPath("/mnt/e/Projects/demo folder", out var clientPath);

            Assert.That(mapped, Is.True);
            Assert.That(clientPath, Is.EqualTo(@"E:\Projects\demo folder"));
        }

        [Test]
        public void TryMapToClientPath_MapsDriveRootToWindowsDriveRoot()
        {
            var mapper = new WindowsMountedDriveWslPathMapper();

            var mapped = mapper.TryMapToClientPath("/mnt/c", out var clientPath);

            Assert.That(mapped, Is.True);
            Assert.That(clientPath, Is.EqualTo(@"C:\"));
        }

        [Test]
        public void TryMapToClientPath_RejectsNonMountedPath()
        {
            var mapper = new WindowsMountedDriveWslPathMapper();

            var mapped = mapper.TryMapToClientPath("/home/user/repo", out var clientPath);

            Assert.That(mapped, Is.False);
            Assert.That(clientPath, Is.Null);
        }
    }
}
