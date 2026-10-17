using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class FormInteractionConventionsTests
    {
        [Test]
        public void ConfigureRefreshButton_RemovesTabStopByDefault()
        {
            var button = new System.Windows.Forms.Button
            {
                TabStop = true
            };

            FormInteractionConventions.ConfigureRefreshButton(button);

            Assert.That(button.TabStop, Is.False);
        }

        [Test]
        public void ConfigureRefreshButton_CanKeepTabStopForSpecialCases()
        {
            var button = new System.Windows.Forms.Button
            {
                TabStop = false
            };

            FormInteractionConventions.ConfigureRefreshButton(button, true);

            Assert.That(button.TabStop, Is.True);
        }

        [Test]
        public void ApplyMnemonic_AddsRequestedMnemonicWhenMissing()
        {
            var text = FormInteractionConventions.ApplyMnemonic("加载仓库", 'L');

            Assert.That(text, Is.EqualTo("加载仓库(&L)"));
        }

        [Test]
        public void ApplyMnemonic_ReplacesExistingMnemonicWhenDifferent()
        {
            var text = FormInteractionConventions.ApplyMnemonic("刷新仓库(&R)", 'L');

            Assert.That(text, Is.EqualTo("刷新仓库(&L)"));
        }

        [Test]
        public void ApplyMnemonic_LeavesTextUnchangedWhenMnemonicAlreadyMatches()
        {
            var text = FormInteractionConventions.ApplyMnemonic("刷新(&R)", 'R');

            Assert.That(text, Is.EqualTo("刷新(&R)"));
        }
    }
}
