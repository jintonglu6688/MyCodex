using System.Collections.Generic;
using System.Web.Script.Serialization;
using MyCodex.Application.DTOs;
using MyCodex.Infrastructure.Instances;
using MyCodex.WinForms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class VoiceControlStartupTests
    {
        [Test]
        public void Status_UsesTheExistingTargetWithoutOpeningAnotherWindow()
        {
            TargetInstanceRequest observed = null;
            string output = null;
            int code;
            Assert.That(VoiceControlStartup.TryRun(new[] { "--voice-control", "{}", "--target", "wsl-one" }, "MyCodex", "unused", out code,
                (target, request) =>
                {
                    Assert.That(target, Is.EqualTo("wsl-one"));
                    observed = request;
                    return new TargetInstanceReply { Success = true, VoiceThreadId = "current", VoiceResult = "enabled=False" };
                }, text => output = text), Is.True);
            Assert.That(code, Is.Zero);
            Assert.That(observed.Operation, Is.EqualTo(TargetInstanceOperation.VoiceControl));
            Assert.That(VoiceControlCommand.Parse(observed.VoiceArguments).HasChanges, Is.False);
            Assert.That(new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(output)["thread_id"], Is.EqualTo("current"));
        }

        [Test]
        public void Mutation_IsBoundToAnExplicitConversation()
        {
            int code;
            Assert.That(VoiceControlStartup.TryRun(new[] { "--voice-control={\"enabled\":false}", "--thread=exact-thread" }, "MyCodex", "unused", out code,
                (target, request) =>
                {
                    Assert.That(target, Is.EqualTo("windows"));
                    Assert.That(request.VoiceThreadId, Is.EqualTo("exact-thread"));
                    Assert.That(VoiceControlCommand.Parse(request.VoiceArguments).Enabled, Is.False);
                    return new TargetInstanceReply { Success = true };
                }, text => { }), Is.True);
            Assert.That(code, Is.Zero);
        }

        [TestCase("{\"enabled\":true}")]
        [TestCase("{\"enabled\":\"true\"}")]
        [TestCase("[]")]
        [TestCase("not-json")]
        public void InvalidOrUnboundMutations_DoNotReachAnInstance(string arguments)
        {
            int code;
            VoiceControlStartup.TryRun(new[] { "--voice-control", arguments }, "MyCodex", "unused", out code,
                (target, request) => { Assert.Fail("Invalid request must not reach the client."); return null; }, text => { });
            Assert.That(code, Is.EqualTo(1));
        }
    }
}
