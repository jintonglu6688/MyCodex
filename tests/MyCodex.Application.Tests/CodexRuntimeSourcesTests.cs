using MyCodex.Shared.Constants;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class CodexRuntimeSourcesTests
    {
        [TestCase(null, CodexRuntimeSources.Bundled)]
        [TestCase("", CodexRuntimeSources.Bundled)]
        [TestCase("Unknown", CodexRuntimeSources.Bundled)]
        [TestCase(CodexRuntimeSources.Bundled, CodexRuntimeSources.Bundled)]
        [TestCase("bUnDlEd", CodexRuntimeSources.Bundled)]
        [TestCase(CodexRuntimeSources.System, CodexRuntimeSources.System)]
        [TestCase("sYsTeM", CodexRuntimeSources.System)]
        [TestCase(CodexRuntimeSources.Custom, CodexRuntimeSources.Custom)]
        [TestCase("cUsToM", CodexRuntimeSources.Custom)]
        [TestCase(CodexRuntimeSources.Wsl, CodexRuntimeSources.Wsl)]
        [TestCase("wSl", CodexRuntimeSources.Wsl)]
        [TestCase(CodexRuntimeSources.RemoteMacSsh, CodexRuntimeSources.RemoteUnixSsh)]
        [TestCase("rEmOtEmAcSsH", CodexRuntimeSources.RemoteUnixSsh)]
        [TestCase(CodexRuntimeSources.RemoteUnixSsh, CodexRuntimeSources.RemoteUnixSsh)]
        [TestCase("rEmOtEuNiXsSh", CodexRuntimeSources.RemoteUnixSsh)]
        public void CanonicalizeForExecution_ReturnsExpectedSource(string source, string expected)
        {
            Assert.That(CodexRuntimeSources.CanonicalizeForExecution(source), Is.EqualTo(expected));
        }

        [TestCase(null, CodexRuntimeSources.Bundled)]
        [TestCase("", CodexRuntimeSources.Bundled)]
        [TestCase("Unknown", CodexRuntimeSources.Bundled)]
        [TestCase(CodexRuntimeSources.Bundled, CodexRuntimeSources.Bundled)]
        [TestCase("bUnDlEd", CodexRuntimeSources.Bundled)]
        [TestCase(CodexRuntimeSources.System, CodexRuntimeSources.System)]
        [TestCase("sYsTeM", CodexRuntimeSources.System)]
        [TestCase(CodexRuntimeSources.Custom, CodexRuntimeSources.Custom)]
        [TestCase("cUsToM", CodexRuntimeSources.Custom)]
        [TestCase(CodexRuntimeSources.Wsl, CodexRuntimeSources.Wsl)]
        [TestCase("wSl", CodexRuntimeSources.Wsl)]
        [TestCase(CodexRuntimeSources.RemoteMacSsh, CodexRuntimeSources.RemoteMacSsh)]
        [TestCase("rEmOtEmAcSsH", CodexRuntimeSources.RemoteMacSsh)]
        [TestCase(CodexRuntimeSources.RemoteUnixSsh, CodexRuntimeSources.RemoteUnixSsh)]
        [TestCase("rEmOtEuNiXsSh", CodexRuntimeSources.RemoteUnixSsh)]
        public void CanonicalizeForPersistence_ReturnsExpectedSource(string source, string expected)
        {
            Assert.That(CodexRuntimeSources.CanonicalizeForPersistence(source), Is.EqualTo(expected));
        }
    }
}
