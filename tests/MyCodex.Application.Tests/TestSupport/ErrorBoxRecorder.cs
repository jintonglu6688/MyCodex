using System.Collections.Generic;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests.TestSupport
{
    // Records authentication error boxes instead of showing modal message boxes during tests.
    internal static class ErrorBoxRecorder
    {
        private static readonly object Sync = new object();
        private static readonly List<string> Shown = new List<string>();

        internal static IReadOnlyList<string> Messages
        {
            get { lock (Sync) { return Shown.ToArray(); } }
        }

        internal static void Clear()
        {
            lock (Sync) { Shown.Clear(); }
        }

        internal static void Install()
        {
            AuthenticationCenterUiText.ErrorPresenter = (owner, text, caption) =>
            {
                Assert.That(caption, Is.Not.Empty, "Error boxes need a title.");
                lock (Sync) { Shown.Add(text); }
            };
        }
    }
}

// Outside any namespace so it applies to every test in the assembly.
[SetUpFixture]
public sealed class ErrorBoxRecorderSetUp
{
    [OneTimeSetUp]
    public void InstallRecorder()
    {
        MyCodex.Application.Tests.TestSupport.ErrorBoxRecorder.Install();
    }
}
