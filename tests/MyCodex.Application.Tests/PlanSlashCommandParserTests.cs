using MyCodex.Application.Services;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class PlanSlashCommandParserTests
    {
        [TestCase("/plan", "")]
        [TestCase("  /PLAN", "")]
        [TestCase("/plan-mode", "")]
        [TestCase("/plan  write the tests", "write the tests")]
        [TestCase("/plan-mode\twrite the tests", "write the tests")]
        public void Parse_RecognizesPlanCommands(string text, string expectedPrompt)
        {
            var command = PlanSlashCommandParser.Parse(text);

            Assert.That(command.IsPlanCommand, Is.True);
            Assert.That(command.PromptText, Is.EqualTo(expectedPrompt));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("plan")]
        [TestCase("/planning")]
        [TestCase("/plan-modes now")]
        [TestCase("/review")]
        public void Parse_IgnoresOtherText(string text)
        {
            Assert.That(PlanSlashCommandParser.Parse(text), Is.SameAs(PlanSlashCommand.None));
        }
    }
}
