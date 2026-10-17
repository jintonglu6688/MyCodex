using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using MyCodex.Infrastructure.ByCtrl;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class BaoYiWebApiScreenReaderServiceTests
    {
        [Test]
        public void Speak_SendsSingleMessageWithCompleteLongText()
        {
            var transport = new FakeBaoYiWebApiTransport();
            transport.EnqueueResponse(@"{""action"":""speech.speak"",""error"":""OK""}");
            var text = new string('\u6D4B', 6000) + "\r\nnext";

            using (var service = new BaoYiWebApiScreenReaderService(transport))
            {
                service.ApplyIndependentChannelName(" My Channel ");

                var result = service.Speak(text, true, false, true);

                Assert.That(result, Is.True);
                Assert.That(transport.Requests, Has.Count.EqualTo(1));
                var payload = Parse(transport.Requests[0]);
                Assert.That(payload["action"], Is.EqualTo("speech.speak"));
                Assert.That(payload["text"], Is.EqualTo(new string('\u6D4B', 6000) + "\nnext"));
                Assert.That(payload["use_slave"], Is.EqualTo(true));
                Assert.That(payload["slave_name"], Is.EqualTo("My Channel"));
                Assert.That(payload["append"], Is.EqualTo(false));
                Assert.That(payload.ContainsKey("allowBreak"), Is.False);
            }
        }

        [Test]
        public void Speak_SendsSingleMessageWithVeryLongTextBeyondDefaultJsonLimit()
        {
            var transport = new FakeBaoYiWebApiTransport();
            transport.EnqueueResponse(@"{""action"":""speech.speak"",""error"":""OK""}");
            var text = new string('\u6D4B', 800000);

            using (var service = new BaoYiWebApiScreenReaderService(transport))
            {
                var result = service.Speak(text, false, false, true);

                Assert.That(result, Is.True);
                Assert.That(transport.Requests, Has.Count.EqualTo(1));
                var payload = Parse(transport.Requests[0]);
                Assert.That(payload["action"], Is.EqualTo("speech.speak"));
                Assert.That(payload["text"], Is.EqualTo(text));
            }
        }

        [Test]
        public void Speak_WithEmptyTextFailsWithoutSending()
        {
            var transport = new FakeBaoYiWebApiTransport();

            using (var service = new BaoYiWebApiScreenReaderService(transport))
            {
                var result = service.Speak(" \r\n ", true, false, false);

                Assert.That(result, Is.False);
                Assert.That(transport.Requests, Is.Empty);
                Assert.That(service.LastError, Is.Not.Null);
            }
        }

        [Test]
        public void Speak_WithNonOkResponseFails()
        {
            var transport = new FakeBaoYiWebApiTransport();
            transport.EnqueueResponse(@"{""action"":""speech.speak"",""error"":""BUSY""}");

            using (var service = new BaoYiWebApiScreenReaderService(transport))
            {
                var result = service.Speak("hello", false, true, false);

                Assert.That(result, Is.False);
                Assert.That(service.LastError, Does.Contain("BUSY"));
            }
        }

        [Test]
        public void Stop_SendsStopRequest()
        {
            var transport = new FakeBaoYiWebApiTransport();
            transport.EnqueueResponse(@"{""action"":""speech.status"",""error"":""OK"",""status"":0}");
            transport.EnqueueResponse(@"{""action"":""speech.stop"",""error"":""OK""}");

            using (var service = new BaoYiWebApiScreenReaderService(transport))
            {
                service.ApplyIndependentChannelName("My Channel");

                Assert.That(service.EnsureInitialized(), Is.True);
                var result = service.Stop(true);

                Assert.That(result, Is.True);
                Assert.That(transport.Requests, Has.Count.EqualTo(2));
                var payload = Parse(transport.Requests[1]);
                Assert.That(payload["action"], Is.EqualTo("speech.stop"));
                Assert.That(payload["use_slave"], Is.EqualTo(true));
                Assert.That(payload["slave_name"], Is.EqualTo("My Channel"));
                Assert.That(payload.ContainsKey("text"), Is.False);
                Assert.That(payload.ContainsKey("append"), Is.False);
            }
        }

        [Test]
        public void GetReaderState_StatusOneMapsToSpeaking()
        {
            var transport = new FakeBaoYiWebApiTransport();
            transport.EnqueueResponse(@"{""action"":""speech.status"",""error"":""OK"",""status"":1}");
            transport.EnqueueResponse(@"{""action"":""speech.status"",""error"":""OK"",""status"":1}");

            using (var service = new BaoYiWebApiScreenReaderService(transport))
            {
                var state = service.GetReaderState();

                Assert.That(state, Is.EqualTo(1));
                Assert.That(service.IsReaderRunning(), Is.True);
            }
        }

        [Test]
        public void IsReaderRunning_WhenIdleButStatusRequestSucceeds_ReturnsTrue()
        {
            var transport = new FakeBaoYiWebApiTransport();
            transport.EnqueueResponse(@"{""action"":""speech.status"",""error"":""OK"",""status"":0}");
            using (var service = new BaoYiWebApiScreenReaderService(transport))
            {
                Assert.That(service.IsReaderRunning(), Is.True);
            }
        }

        [Test]
        public void IsReaderRunning_AfterExitAndRestart_ProbesCurrentConnection()
        {
            var transport = new FakeBaoYiWebApiTransport();
            transport.EnqueueResponse(@"{""action"":""speech.status"",""error"":""OK"",""status"":1}");
            using (var service = new BaoYiWebApiScreenReaderService(transport))
            {
                Assert.That(service.IsReaderRunning(), Is.True);
                transport.ExceptionToThrow = new TimeoutException("reader exited");
                Assert.That(service.IsReaderRunning(), Is.False);
                Assert.That(service.IsInitialized, Is.False);

                transport.ExceptionToThrow = null;
                transport.EnqueueResponse(@"{""action"":""speech.status"",""error"":""OK"",""status"":0}");
                Assert.That(service.IsReaderRunning(), Is.True);
            }
        }

        [Test]
        public void GetReaderState_WhenTransportFailsReturnsNotSpeaking()
        {
            var transport = new FakeBaoYiWebApiTransport();
            transport.ExceptionToThrow = new TimeoutException("timed out");

            using (var service = new BaoYiWebApiScreenReaderService(transport))
            {
                var state = service.GetReaderState();

                Assert.That(state, Is.EqualTo(0));
                Assert.That(service.LastError, Does.Contain("timed out"));
            }
        }

        [Test]
        public void Stop_WhenNotInitializedDoesNotOpenWebSocket()
        {
            var transport = new FakeBaoYiWebApiTransport();
            transport.ExceptionToThrow = new TimeoutException("timed out");

            using (var service = new BaoYiWebApiScreenReaderService(transport))
            {
                var result = service.Stop(true);

                Assert.That(result, Is.True);
                Assert.That(transport.Requests, Is.Empty);
            }
        }

        [Test]
        public void TransportExceptionUpdatesLastErrorWithoutFullText()
        {
            var transport = new FakeBaoYiWebApiTransport();
            transport.ExceptionToThrow = new TimeoutException("timed out");
            var text = "secret phrase that should not be copied into LastError";

            using (var service = new BaoYiWebApiScreenReaderService(transport))
            {
                var result = service.Speak(text, false, false, false);

                Assert.That(result, Is.False);
                Assert.That(service.LastError, Does.Contain("timed out"));
                Assert.That(service.LastError, Does.Not.Contain(text));
            }
        }

        private static Dictionary<string, object> Parse(string json)
        {
            return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<Dictionary<string, object>>(json);
        }

        private sealed class FakeBaoYiWebApiTransport : IBaoYiWebApiScreenReaderTransport
        {
            private readonly Queue<string> _responses = new Queue<string>();

            public List<string> Requests { get; } = new List<string>();

            public Exception ExceptionToThrow { get; set; }

            public void EnqueueResponse(string response)
            {
                _responses.Enqueue(response);
            }

            public string Send(string requestJson, TimeSpan timeout)
            {
                Requests.Add(requestJson);
                if (ExceptionToThrow != null)
                {
                    throw ExceptionToThrow;
                }

                return _responses.Count > 0
                    ? _responses.Dequeue()
                    : @"{""error"":""OK""}";
            }

            public void Dispose()
            {
            }
        }
    }
}
