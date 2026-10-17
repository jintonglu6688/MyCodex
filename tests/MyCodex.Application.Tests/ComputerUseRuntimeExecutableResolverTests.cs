using System;
using System.Collections.Generic;
using MyCodex.Infrastructure.ComputerUse;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class ComputerUseRuntimeExecutableResolverTests
    {
        private static readonly DateTime Started = new DateTime(2026, 9, 29, 1, 0, 0, DateTimeKind.Utc);

        [Test]
        public void FindsTheNativeBinaryUnderTheSelectedWrapperOnly()
        {
            var parents = new Dictionary<int, int> { { 2, 1 }, { 3, 2 }, { 8, 7 } };
            var identities = new Dictionary<int, ComputerUseRuntimeExecutableResolver.RuntimeProcessIdentity>
            {
                { 2, Identity(@"D:\Selected\node.exe", 1) },
                { 3, Identity(@"D:\Selected\vendor\codex.exe", 2) },
                { 8, Identity(@"E:\Bundled\codex.exe", 1) }
            };
            Assert.That(Select(parents, identities), Is.EqualTo(@"D:\Selected\vendor\codex.exe"));
        }

        [Test]
        public void AmbiguousNativeProcessesAreRejectedEvenWhenTheirPathsMatch()
        {
            var parents = new Dictionary<int, int> { { 2, 1 }, { 3, 1 } };
            var identities = new Dictionary<int, ComputerUseRuntimeExecutableResolver.RuntimeProcessIdentity>
            {
                { 2, Identity(@"D:\Selected\codex.exe", 1) }, { 3, Identity(@"D:\Selected\codex.exe", 2) }
            };
            Assert.That(Select(parents, identities), Is.Null);
        }

        [Test]
        public void ReusedParentPidCannotAdoptOlderUnrelatedProcess()
        {
            var parents = new Dictionary<int, int> { { 2, 1 }, { 3, 2 }, { 4, 3 } };
            var identities = new Dictionary<int, ComputerUseRuntimeExecutableResolver.RuntimeProcessIdentity>
            {
                { 2, Identity(@"D:\Selected\node.exe", 3) },
                { 3, Identity(@"D:\Other\launcher.exe", 2) },
                { 4, Identity(@"D:\Other\codex.exe", 4) }
            };
            Assert.That(Select(parents, identities), Is.Null);
        }

        [Test]
        public void UnreadableOrExitedDescendantDoesNotGuessTheRuntime()
        {
            Assert.That(Select(new Dictionary<int, int> { { 2, 1 } },
                new Dictionary<int, ComputerUseRuntimeExecutableResolver.RuntimeProcessIdentity>()), Is.Null);
        }

        [Test]
        public void CycleIsRejectedInsteadOfWalkingForever()
        {
            var parents = new Dictionary<int, int> { { 2, 1 }, { 1, 2 } };
            Assert.That(Select(parents, new Dictionary<int, ComputerUseRuntimeExecutableResolver.RuntimeProcessIdentity>
                { { 2, Identity(@"D:\Selected\codex.exe", 1) } }), Is.Null);
        }

        [TestCase(@"D:\Selected\codex.cmd")]
        [TestCase("codex.exe")]
        [TestCase(@"D:\Selected\node.exe")]
        public void WrapperRelativeAndNonCodexDescendantsAreNotNativeCandidates(string path)
        {
            Assert.That(Select(new Dictionary<int, int> { { 2, 1 } },
                new Dictionary<int, ComputerUseRuntimeExecutableResolver.RuntimeProcessIdentity> { { 2, Identity(path, 1) } }), Is.Null);
        }

        private static string Select(IDictionary<int, int> parents,
            IDictionary<int, ComputerUseRuntimeExecutableResolver.RuntimeProcessIdentity> identities)
        {
            return ComputerUseRuntimeExecutableResolver.SelectUniqueCodexDescendant(1, Identity(@"C:\Windows\System32\cmd.exe", 0), parents,
                id => identities.TryGetValue(id, out var identity) ? identity : null);
        }

        private static ComputerUseRuntimeExecutableResolver.RuntimeProcessIdentity Identity(string path, int seconds)
        {
            return new ComputerUseRuntimeExecutableResolver.RuntimeProcessIdentity(path, Started.AddSeconds(seconds));
        }
    }
}
