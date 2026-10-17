using System;
using System.Reflection;
using System.Threading;
using MyCodex.Infrastructure.Git;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class GitLocalProcessOutputTests
    {
        // ~140 KB of stdout: far beyond the pipe buffer, which blocked the child when output was read only after exit.
        private const string LargeOutputArguments = "/d /c for /L %i in (1,1,3000) do @echo 0123456789012345678901234567890123456789";

        [TestCase(typeof(GitEnvironmentService))]
        [TestCase(typeof(GitSshKeyService))]
        public void RunProcess_ReadsOutputLargerThanPipeBufferWithoutTimingOut(Type serviceType)
        {
            var result = InvokeRunProcess(serviceType, "cmd.exe", LargeOutputArguments, 20000);

            Assert.That(GetProperty<int>(result, "ExitCode"), Is.EqualTo(0), GetProperty<string>(result, "StandardError"));
            Assert.That(GetProperty<string>(result, "StandardOutput").Length, Is.GreaterThan(100000));
        }

        [TestCase(typeof(GitEnvironmentService))]
        [TestCase(typeof(GitSshKeyService))]
        public void RunProcess_ReportsTimeoutAsFailedResult(Type serviceType)
        {
            var result = InvokeRunProcess(serviceType, "cmd.exe", "/d /c ping -n 30 127.0.0.1 >nul", 500);

            Assert.That(GetProperty<int>(result, "ExitCode"), Is.EqualTo(-1));
            Assert.That(GetProperty<string>(result, "StandardError"), Is.Not.Empty);
        }

        [TestCase(typeof(GitEnvironmentService))]
        [TestCase(typeof(GitSshKeyService))]
        public void RunProcess_RethrowsCancellation(Type serviceType)
        {
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();

                var exception = Assert.Throws<TargetInvocationException>(() =>
                    InvokeRunProcess(serviceType, "cmd.exe", "/d /c echo unused", 5000, cancellation.Token));
                Assert.That(exception.InnerException, Is.InstanceOf<OperationCanceledException>());
            }
        }

        private static object InvokeRunProcess(
            Type serviceType,
            string fileName,
            string arguments,
            int timeoutMilliseconds,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var method = serviceType.GetMethod("RunProcess", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            return method.Invoke(null, new object[] { fileName, arguments, timeoutMilliseconds, cancellationToken });
        }

        private static T GetProperty<T>(object instance, string name)
        {
            return (T)instance.GetType().GetProperty(name).GetValue(instance);
        }
    }
}
