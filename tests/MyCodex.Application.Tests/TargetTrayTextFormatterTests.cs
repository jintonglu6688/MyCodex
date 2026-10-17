using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class TargetTrayTextFormatterTests
    {
        [Test]
        public void TrayTooltip_PreservesReadableStateAndFitsNativeLimitWithLongNames()
        {
            var tooltip = TargetTrayTextFormatter.BuildTrayTooltip("MyCodex", new string('x', 100), "Waiting for interaction");
            Assert.That(tooltip.Length, Is.LessThanOrEqualTo(63));
            Assert.That(tooltip, Does.Contain("Waiting for interaction"));
            Assert.That(tooltip, Does.StartWith("x"));
            Assert.That(tooltip, Does.EndWith("MyCodex"));
        }
    }
}
