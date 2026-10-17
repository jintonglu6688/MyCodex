using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Infrastructure.AuthenticationCenter;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class ModelsDevCatalogSourceTests
    {
        private string _directory;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "models-dev-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }

        [Test]
        public async Task DownloadIsCachedAndReusedWhileFresh()
        {
            var cache = Path.Combine(_directory, "catalog.json");
            var now = new DateTime(2026, 10, 10, 8, 0, 0, DateTimeKind.Utc);
            var downloads = 0;
            var source = new ModelsDevCatalogSource(cache, token => { downloads++; return Task.FromResult(ModelMetadataResolverTests.CatalogJson); }, () => now);

            Assert.That(source.Cached, Is.Null);
            var catalog = await source.LoadAsync(CancellationToken.None);
            Assert.That(catalog.Providers, Has.Count.EqualTo(4));
            Assert.That(source.Cached, Is.SameAs(catalog));
            Assert.That(await source.LoadAsync(CancellationToken.None), Is.SameAs(catalog));
            Assert.That(downloads, Is.EqualTo(1));
            Assert.That(File.ReadAllText(cache), Does.Not.Contain("\"toggle\""), "The cache keeps only fields used for suggestions.");

            File.SetLastWriteTimeUtc(cache, now.AddHours(-1));
            var restarted = new ModelsDevCatalogSource(cache, token => { downloads++; return Task.FromResult("{}"); }, () => now);
            var cached = await restarted.LoadAsync(CancellationToken.None);
            Assert.That(downloads, Is.EqualTo(1), "A fresh disk cache avoids another download.");
            Assert.That(new MyCodex.Application.Services.ModelMetadataResolver("relay.example", null, cached).Resolve("gpt-5.5").ReasoningEfforts,
                Is.EqualTo(new[] { "none", "low", "medium", "high", "xhigh" }));
        }

        [Test]
        public async Task FailedRefreshKeepsStaleCacheAndWaitsBeforeRetrying()
        {
            var cache = Path.Combine(_directory, "catalog.json");
            var now = new DateTime(2026, 10, 10, 8, 0, 0, DateTimeKind.Utc);
            await new ModelsDevCatalogSource(cache, token => Task.FromResult(ModelMetadataResolverTests.CatalogJson), () => now).LoadAsync(CancellationToken.None);
            File.SetLastWriteTimeUtc(cache, now.AddDays(-3));
            var attempts = 0;
            var source = new ModelsDevCatalogSource(cache, token => { attempts++; throw new IOException("offline"); }, () => now);

            Assert.That((await source.LoadAsync(CancellationToken.None)).Providers, Has.Count.EqualTo(4));
            Assert.That(await source.LoadAsync(CancellationToken.None), Is.Not.Null);
            Assert.That(attempts, Is.EqualTo(1));
            now = now.Add(ModelsDevCatalogSource.RetryDelay);
            await source.LoadAsync(CancellationToken.None);
            Assert.That(attempts, Is.EqualTo(2));
        }

        [Test]
        public async Task UnavailableOrInvalidDataReturnsNull()
        {
            var offline = new ModelsDevCatalogSource(Path.Combine(_directory, "missing.json"), token => throw new IOException("offline"), () => DateTime.UtcNow);
            Assert.That(await offline.LoadAsync(CancellationToken.None), Is.Null);

            var invalidCache = Path.Combine(_directory, "invalid.json");
            File.WriteAllText(invalidCache, "not json");
            var invalid = new ModelsDevCatalogSource(invalidCache, token => Task.FromResult("[]"), () => DateTime.UtcNow);
            Assert.That(await invalid.LoadAsync(CancellationToken.None), Is.Null);
            Assert.That(File.ReadAllText(invalidCache), Is.EqualTo("not json"), "Invalid downloads do not replace the cache.");
        }

        [Test]
        public void CancellationIsNotTreatedAsUnavailableData()
        {
            var source = new ModelsDevCatalogSource(Path.Combine(_directory, "catalog.json"),
                token => { token.ThrowIfCancellationRequested(); return Task.FromResult(ModelMetadataResolverTests.CatalogJson); }, () => DateTime.UtcNow);
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                Assert.That(async () => await source.LoadAsync(cancellation.Token), Throws.InstanceOf<OperationCanceledException>());
            }
        }
    }
}
