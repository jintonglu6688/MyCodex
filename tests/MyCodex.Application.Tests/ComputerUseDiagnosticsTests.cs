using System.Collections.Generic;
using MyCodex.Infrastructure.ComputerUse;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class ComputerUseDiagnosticsTests
    {
        [Test]
        public void FailureLog_PreservesOnlySafeDiagnosticFields()
        {
            var diagnostic = new Dictionary<string, object>
            {
                { "stage", "executor_request" }, { "method", "launch_app" }, { "approval", "accept" },
                { "category", "helper_exit" }, { "errorType", "Error" }, { "errorCode", "EPIPE" },
                { "helperExitCode", 5 }, { "hresult", "0x80070005" }, { "elapsedMs", 15001 },
                { "message", "secret stderr" }, { "stack", "secret path" }
            };
            var message = new Dictionary<string, object>
            {
                { "event", "fault" }, { "code", "CUA_EXECUTION_FAILED" }, { "generation", 2 },
                { "slot", "00000000-0000-4000-8000-000000000001" }, { "diagnostic", diagnostic },
                { "thread", "secret thread" }, { "turn", "secret turn" }, { "params", "secret input" }
            };

            var line = ComputerUseHostProcess.FormatFailureDiagnostic(message);

            Assert.That(line, Does.Contain("Event=fault; Code=CUA_EXECUTION_FAILED"));
            Assert.That(line, Does.Contain("Slot=00000000-0000-4000-8000-000000000001; Generation=2"));
            Assert.That(line, Does.Contain("Stage=executor_request; Method=launch_app; Approval=accept"));
            Assert.That(line, Does.Contain("Category=helper_exit; ErrorType=Error; ErrorCode=EPIPE"));
            Assert.That(line, Does.Contain("HelperExitCode=5; HResult=0x80070005; ElapsedMs=15001"));
            Assert.That(line, Does.Not.Contain("secret"));
        }

        [Test]
        public void FailureLog_RejectsUntrustedValuesAndAcceptsOlderEventsWithoutDiagnostics()
        {
            var diagnostic = new Dictionary<string, object>();
            foreach (var key in new[] { "stage", "method", "approval", "category", "errorType", "errorCode", "helperExitCode", "hresult", "elapsedMs" })
                diagnostic[key] = "secret\r\nforged log";
            var message = new Dictionary<string, object>
            {
                { "event", "secret-event" }, { "code", "CUA_SECRET_TOKEN" }, { "slot", "secret-slot" },
                { "generation", -1 }, { "diagnostic", diagnostic }
            };
            Assert.That(ComputerUseHostProcess.FormatFailureDiagnostic(message), Does.Not.Contain("secret").IgnoreCase);
            Assert.That(ComputerUseHostProcess.FormatFailureDiagnostic(message), Does.Not.Contain("\n"));
            Assert.That(ComputerUseHostProcess.FormatFailureDiagnostic(message), Does.Contain("Generation=unknown"));
            Assert.That(ComputerUseHostProcess.FormatFailureDiagnostic(new Dictionary<string, object>
            {
                { "event", "fatal" }, { "code", "CUA_COMPONENT_CONTRACT" }
            }), Does.Contain("Event=fatal; Code=CUA_COMPONENT_CONTRACT"));
        }
    }
}
