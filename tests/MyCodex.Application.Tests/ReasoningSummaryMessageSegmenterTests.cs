using System.Collections.Generic;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class ReasoningSummaryMessageSegmenterTests
    {
        [Test]
        public void Append_EmitsCompletedSentencesFromCumulativeSummary()
        {
            var state = new ReasoningSummaryMessageState();

            var first = ReasoningSummaryMessageSegmenter.Append(state, "我正在分析", false);
            var second = ReasoningSummaryMessageSegmenter.Append(state, "我正在分析代码。接下来检查", false);
            var third = ReasoningSummaryMessageSegmenter.Append(state, "我正在分析代码。接下来检查入口。", false);

            Assert.That(first, Is.Empty);
            Assert.That(second, Is.EqualTo(new List<string> { "我正在分析代码。" }));
            Assert.That(third, Is.EqualTo(new List<string> { "接下来检查入口。" }));
        }

        [Test]
        public void Append_ForceFlushEmitsPendingSummary()
        {
            var state = new ReasoningSummaryMessageState();

            var first = ReasoningSummaryMessageSegmenter.Append(state, "Now let me provide", false);
            var second = ReasoningSummaryMessageSegmenter.Append(state, "Now let me provide the summary", true);

            Assert.That(first, Is.Empty);
            Assert.That(second, Is.EqualTo(new List<string> { "Now let me provide the summary" }));
        }

        [Test]
        public void Append_DoesNotDuplicateSameSegment()
        {
            var state = new ReasoningSummaryMessageState();

            var first = ReasoningSummaryMessageSegmenter.Append(state, "正在分析代码。", false);
            var second = ReasoningSummaryMessageSegmenter.Append(state, "正在分析代码。", true);

            Assert.That(first, Is.EqualTo(new List<string> { "正在分析代码。" }));
            Assert.That(second, Is.Empty);
        }
    }
}
