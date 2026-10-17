using System;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class PluginMarketPagingTests
    {
        [Test]
        public void PageSize_MatchesMarketBrowseList()
        {
            Assert.That(PluginMarketPaging.PageSize, Is.EqualTo(100));
        }

        [TestCase(0, 0)]
        [TestCase(100, 1)]
        [TestCase(101, 2)]
        [TestCase(2008, 21)]
        public void GetTotalPages_ReturnsExpectedCount(int totalCount, int expectedPages)
        {
            Assert.That(PluginMarketPaging.GetTotalPages(totalCount), Is.EqualTo(expectedPages));
        }

        [TestCase(1, 100, new string[0])]
        [TestCase(1, 101, new[] { "下一页" })]
        [TestCase(2, 201, new[] { "上一页", "下一页" })]
        [TestCase(3, 201, new[] { "上一页" })]
        public void GetNavigationItems_ReturnsAdjacentNavigationRows(int page, int totalCount, string[] expectedLabels)
        {
            var items = PluginMarketPaging.GetNavigationItems(page, totalCount, "上一页", "下一页");

            Assert.That(Array.ConvertAll(items, item => item.Label), Is.EqualTo(expectedLabels));
        }
    }
}
