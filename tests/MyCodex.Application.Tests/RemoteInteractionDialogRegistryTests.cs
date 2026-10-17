using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class RemoteInteractionDialogRegistryTests
    {
        [Test]
        public void Complete_RegisteredRequest_InvokesCloseOnce()
        {
            var registry = new RemoteInteractionDialogRegistry();
            var closeCount = 0;

            using (registry.Register("approval-1", delegate { closeCount++; }))
            {
                Assert.That(registry.Complete("approval-1"), Is.True);
                Assert.That(registry.Complete("approval-1"), Is.False);
            }

            Assert.That(closeCount, Is.EqualTo(1));
        }
    }
}
