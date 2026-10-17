using MyCodex.Application.Services;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class PluginMarketAppServerSourcePolicyTests
    {
        [TestCase("https://market.example.com/git/official.git", true)]
        [TestCase("https://synthetic-token@market.example.com/git/official.git", false)]
        [TestCase("https://market.example.com/git/official.git?token=synthetic", false)]
        [TestCase("https://market.example.com/git/official.git#main", false)]
        [TestCase("http://market.example.com/git/official.git", false)]
        [TestCase("file:///C:/market.git", false)]
        [TestCase("../market.git", false)]
        [TestCase("", false)]
        public void IsAllowed_OnlyAcceptsAbsoluteHttpsSources(string source, bool expected)
        {
            Assert.That(PluginMarketAppServerSourcePolicy.IsAllowed(source), Is.EqualTo(expected));
        }
    }
}
