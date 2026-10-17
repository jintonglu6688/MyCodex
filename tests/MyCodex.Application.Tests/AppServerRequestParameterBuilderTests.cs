using System;
using System.Collections.Generic;
using MyCodex.Application.DTOs;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerRequestParameterBuilderTests
    {
        [Test]
        public void BuildCommandInputWriteParameters_IncludesProcessIdAndInput()
        {
            var parameters = AppServerRequestParameterBuilder.BuildCommandInputWriteParameters("process-1", "hello");

            Assert.That(parameters["processId"], Is.EqualTo("process-1"));
            Assert.That(parameters["input"], Is.EqualTo("hello"));
        }

        [Test]
        public void BuildFileSystemWriteFileParameters_EncodesContentAsBase64()
        {
            var parameters = AppServerRequestParameterBuilder.BuildFileSystemWriteFileParameters(
                @"E:\Repo\AGENTS.md",
                new byte[] { 1, 2, 3 },
                path => "server:" + path);

            Assert.That(parameters["path"], Is.EqualTo(@"server:E:\Repo\AGENTS.md"));
            Assert.That(parameters["dataBase64"], Is.EqualTo("AQID"));
        }

        [Test]
        public void BuildFileSystemWatchParameters_IncludesPathAndWatchId()
        {
            var parameters = AppServerRequestParameterBuilder.BuildFileSystemWatchParameters(
                @"E:\Repo",
                "watch-1",
                path => "server:" + path);

            Assert.That(parameters["path"], Is.EqualTo(@"server:E:\Repo"));
            Assert.That(parameters["watchId"], Is.EqualTo("watch-1"));
        }

        [Test]
        public void BuildFuzzyFileSearchParameters_IncludesQueryRootsAndCancellationToken()
        {
            var request = new AppServerFuzzyFileSearchRequest
            {
                Query = "agent",
                Roots = new List<string> { @"E:\Repo", @"E:\Repo", " " },
                CancellationToken = "search-1"
            };

            var parameters = AppServerRequestParameterBuilder.BuildFuzzyFileSearchParameters(
                request,
                path => "server:" + path);

            Assert.That(parameters["query"], Is.EqualTo("agent"));
            Assert.That((string[])parameters["roots"], Is.EqualTo(new[] { @"server:E:\Repo" }));
            Assert.That(parameters["cancellationToken"], Is.EqualTo("search-1"));
        }
    }
}
