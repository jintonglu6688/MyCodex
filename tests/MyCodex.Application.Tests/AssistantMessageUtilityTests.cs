using MyCodex.Shared.Helpers;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AssistantMessageUtilityTests
    {
        [TestCase(null, false)]
        [TestCase("", false)]
        [TestCase("   ", false)]
        [TestCase("...", false)]
        [TestCase(" … ", false)]
        [TestCase(". . .", false)]
        [TestCase("．．．", false)]
        [TestCase("我来分析当前项目。", true)]
        [TestCase("...继续分析如下", true)]
        public void HasDisplayableContent_ReturnsExpectedResult(string text, bool expected)
        {
            Assert.That(AssistantMessageUtility.HasDisplayableContent(text), Is.EqualTo(expected));
        }
    }
}
