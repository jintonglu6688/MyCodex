using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Tests.SourceGuards;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class GitRemoteManagementFormTests
    {
        [Test]
        public void GitRemoteManagementForm_CloseCancelsBlockedRead()
        {
            var service = new BlockingGitRemoteService();
            using (var form = new GitRemoteManagementForm("C:\\workspace", service))
            {
                form.Show();
                System.Windows.Forms.Application.DoEvents();
                Assert.That(service.ReadStarted.Wait(1000), Is.True);

                form.Close();

                Assert.That(service.ReadCanceled.Wait(1000), Is.True);
                service.CompleteRead();
                System.Windows.Forms.Application.DoEvents();
            }
        }

        [Category(SourceGuardCategory.SourceBehavior)]
        [Test]
        public void GitRemoteManagementForm_CancelsLifetimeWorkBeforeClose()
        {
            var source = RepositorySource.ReadSourceFile("GitRemoteManagementForm.cs");

            Assert.That(source, Does.Contain("CancellationTokenSource _lifetimeCancellation"));
            Assert.That(source, Does.Contain("_lifetimeCancellation.Cancel();"));
            Assert.That(source, Does.Contain("OperationCanceledException"));
            Assert.That(source, Does.Contain("_lifetimeCancellation.Token"));
        }

        private sealed class BlockingGitRemoteService : IGitRemoteService
        {
            private readonly TaskCompletionSource<IReadOnlyList<GitRemoteEntry>> _readCompletion = new TaskCompletionSource<IReadOnlyList<GitRemoteEntry>>();

            public ManualResetEventSlim ReadStarted { get; } = new ManualResetEventSlim();

            public ManualResetEventSlim ReadCanceled { get; } = new ManualResetEventSlim();

            public Task<IReadOnlyList<GitRemoteEntry>> ReadRemotesAsync(string workspacePath, CancellationToken cancellationToken = default)
            {
                cancellationToken.Register(() => ReadCanceled.Set());
                ReadStarted.Set();
                return _readCompletion.Task;
            }

            public void CompleteRead()
            {
                _readCompletion.TrySetResult(new GitRemoteEntry[0]);
            }

            public Task<GitOperationResult> InitializeRepositoryAsync(string workspacePath, CancellationToken cancellationToken = default) { return Task.FromResult<GitOperationResult>(null); }
            public Task<GitOperationResult> AddRemoteAsync(string workspacePath, string remoteName, string remoteUrl, CancellationToken cancellationToken = default) { return Task.FromResult<GitOperationResult>(null); }
            public Task<GitOperationResult> SetRemoteUrlAsync(string workspacePath, string remoteName, string remoteUrl, CancellationToken cancellationToken = default) { return Task.FromResult<GitOperationResult>(null); }
            public Task<GitOperationResult> SetRemotePushUrlAsync(string workspacePath, string remoteName, string remoteUrl, CancellationToken cancellationToken = default) { return Task.FromResult<GitOperationResult>(null); }
            public Task<GitOperationResult> RemoveRemoteAsync(string workspacePath, string remoteName, CancellationToken cancellationToken = default) { return Task.FromResult<GitOperationResult>(null); }
            public Task<GitOperationResult> SetUpstreamAsync(string workspacePath, string branchName, string upstream, CancellationToken cancellationToken = default) { return Task.FromResult<GitOperationResult>(null); }
            public Task<GitOperationResult> UnsetUpstreamAsync(string workspacePath, string branchName, CancellationToken cancellationToken = default) { return Task.FromResult<GitOperationResult>(null); }
        }
    }
}
