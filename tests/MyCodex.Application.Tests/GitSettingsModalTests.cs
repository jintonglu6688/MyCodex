using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Infrastructure.GitHosting;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class GitSettingsModalTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void SettingsAreTargetOwnedResizableModalDialogsWithAccessibleClose(bool credentials)
        {
            using (var owner = new Form())
            using (var group = new TargetWindowGroup(owner, () => "Ubuntu"))
            using (Form form = credentials
                ? (Form)new GitRemoteCredentialsForm(new GitHubCliService(new OfflineGitHub()), new OfflineSsh(), null)
                : new GitEnvironmentForm(new OfflineEnvironment(), null))
            {
                owner.Show();
                Exception failure = null;
                form.Shown += (sender, args) => form.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        Assert.That(form.Modal, Is.True);
                        Assert.That(form.Owner, Is.SameAs(owner));
                        Assert.That(IsWindowEnabled(owner.Handle), Is.False);
                        Assert.That(group.FindModal(), Is.SameAs(form));
                        Assert.That(form.Text, Does.StartWith("Ubuntu - "));
                        Assert.That(form.FormBorderStyle, Is.EqualTo(FormBorderStyle.Sizable));
                        Assert.That(form.MinimumSize.Width, Is.GreaterThan(0));
                        Assert.That(form.MinimumSize.Height, Is.GreaterThan(0));
                        var close = form.CancelButton as Button;
                        Assert.That(close, Is.Not.Null);
                        Assert.That(close.AccessibleName, Is.Not.Empty);
                        Assert.That(close.TabStop, Is.True);
                        Assert.That(Controls(form).OfType<Button>().Where(button => button.TabStop)
                            .All(button => !string.IsNullOrWhiteSpace(button.AccessibleName)), Is.True);
                        close.PerformClick();
                    }
                    catch (Exception ex) { failure = ex; }
                    finally { if (form.Visible) form.Close(); }
                }));
                form.ShowDialog(owner);
                Assert.That(failure, Is.Null);
                Assert.That(IsWindowEnabled(owner.Handle), Is.True);
                Assert.That(owner.IsDisposed, Is.False);
            }
        }

        [DllImport("user32.dll")]
        private static extern bool IsWindowEnabled(IntPtr window);

        private static IEnumerable<Control> Controls(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                yield return child;
                foreach (var descendant in Controls(child)) yield return descendant;
            }
        }

        private sealed class OfflineEnvironment : IGitEnvironmentService
        {
            public Task<GitEnvironmentSnapshot> ReadStatusAsync(CancellationToken token) => Task.FromResult(new GitEnvironmentSnapshot());
            public Task SetGlobalUserNameAsync(string value, CancellationToken token) => Task.CompletedTask;
            public Task SetGlobalUserEmailAsync(string value, CancellationToken token) => Task.CompletedTask;
        }

        private sealed class OfflineSsh : IGitSshKeyService
        {
            public Task<GitSshKeySnapshot> ReadKeysAsync(CancellationToken token) => Task.FromResult(new GitSshKeySnapshot());
            public Task<GitSshTestResult> TestGitHubSshAsync(CancellationToken token) => Task.FromResult(new GitSshTestResult());
        }

        private sealed class OfflineGitHub : IGitHubCliCommandRunner
        {
            public Task<GitHubCliCommandResult> RunAsync(string arguments, int timeout, CancellationToken token) =>
                Task.FromResult(new GitHubCliCommandResult { ExitCode = GitHubCliCommandResult.NotFoundExitCode });
            public GitCloneResult CloneRepository(GitHubRepositoryEntry repository, string parent, string folder,
                bool submodules, bool ssh, IProgress<GitCloneProgress> progress, CancellationToken token) => throw new NotSupportedException();
            public void StartInteractiveLogin() => throw new NotSupportedException();
        }
    }
}
