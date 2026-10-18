using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Tests.Fakes;
using MyCodex.Application.Tests.TestSupport;
using MyCodex.WinForms.Forms;
using MyCodex.WinForms.Localization;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AsyncInputRequestTests
    {
        [Test]
        public void Routing_RecognizesAsyncInputRequest()
        {
            Assert.That(
                AppServerEventRouting.Resolve(BuildRequest("item-1", "Question?")),
                Is.EqualTo(AppServerEventRoute.AsyncInputRequested));
        }

        [Test]
        public void Queue_DeduplicatesAndKeepsIndependentPartialDrafts()
        {
            var queue = new AsyncInputRequestQueue();
            var first = queue.Enqueue(BuildRequest("item-1", "Question one?"), "Project / One");
            var duplicate = queue.Enqueue(BuildRequest("item-1", "Question one?"), "Project / One");
            var second = queue.Enqueue(BuildRequest("item-2", "Question two?"), "Project / Two");

            AsyncInputRequestQueue.SaveAnswers(first, new Dictionary<string, string>
            {
                { "async-1", "  answered  " },
                { "empty", " " }
            });
            AsyncInputRequestQueue.SaveAnswers(second, new Dictionary<string, string>
            {
                { "async-1", "second draft" }
            });

            Assert.Multiple(() =>
            {
                Assert.That(queue.Count, Is.EqualTo(2));
                Assert.That(duplicate, Is.Null);
                Assert.That(first.Answers, Is.EqualTo(new Dictionary<string, string> { { "async-1", "answered" } }));
                Assert.That(second.Answers["async-1"], Is.EqualTo("second draft"));
                Assert.That(AsyncInputRequestQueue.BuildResponseText(first), Does.Contain("Question one?"));
                Assert.That(AsyncInputRequestQueue.BuildResponseText(first), Does.Contain(UiLocalization.Text("AsyncInput.Response.QuestionPrefix", "Question: ")));
                Assert.That(AsyncInputRequestQueue.BuildResponseText(first), Does.Contain("answered"));
            });

            Assert.That(queue.Remove(first.Key), Is.True);
            Assert.That(queue.Items.Single(), Is.SameAs(second));
            queue.Remove(second.Key);
            Assert.That(queue.Enqueue(BuildRequest("item-1", "Question one?"), "Project / One"), Is.Null);
            Assert.That(queue.Count, Is.Zero, "a completed notification should not reopen in the same run");
        }

        [Test]
        public void Arrival_DeduplicatesAndAnnouncesOnlyBackgroundOrLaterRequests()
        {
            var queue = new AsyncInputRequestQueue();
            var first = queue.Enqueue(BuildRequest("item-1", "First?"), "Project / One");
            var duplicate = queue.Enqueue(BuildRequest("item-1", "First?"), "Project / One");
            var later = queue.Enqueue(BuildRequest("item-2", "Later?"), "Project / Two");

            Assert.Multiple(() =>
            {
                Assert.That(first, Is.Not.Null);
                Assert.That(duplicate, Is.Null, "duplicate delivery must not trigger another announcement");
                Assert.That(later, Is.Not.Null);
                Assert.That(AsyncInputArrivalPlanner.ShouldAnnounce(true, true), Is.False);
                Assert.That(AsyncInputArrivalPlanner.ShouldAnnounce(true, false), Is.True);
                Assert.That(AsyncInputArrivalPlanner.ShouldAnnounce(false, true), Is.True);
                Assert.That(queue.Count, Is.EqualTo(2));
            });
        }

        [Test]
        public async Task Dispatcher_SteersOriginatingTurnOrStartsWhenIdle()
        {
            var inputs = new[] { new AppServerUserInputItem { Type = "text", Text = "answer" } };
            var client = new FakeAsyncInputThreadClient
            {
                Result = AppServerTurnSteerResult.AcceptedResult("turn-1", "turn-1")
            };
            var started = false;

            var steered = await AsyncInputResponseDispatcher.DispatchAsync(
                client,
                "thread-1",
                "turn-1",
                inputs,
                () => true,
                () => { started = true; return Task.CompletedTask; });

            Assert.That(steered, Is.EqualTo(AsyncInputDispatchOutcome.Steered));
            Assert.That(client.ExpectedTurnId, Is.EqualTo("turn-1"));
            Assert.That(started, Is.False);

            client.Result = AppServerTurnSteerResult.NotAttempted("turn-1", "no-active-turn");
            var startedNewTurn = await AsyncInputResponseDispatcher.DispatchAsync(
                client,
                "thread-1",
                "turn-1",
                inputs,
                () => false,
                () => { started = true; return Task.CompletedTask; });

            Assert.That(startedNewTurn, Is.EqualTo(AsyncInputDispatchOutcome.StartedNewTurn));
            Assert.That(started, Is.True);
        }

        [Test]
        public void Dispatcher_DoesNotFallbackAfterIndeterminateFailure()
        {
            var client = new FakeAsyncInputThreadClient { Exception = new InvalidOperationException("connection lost") };
            var started = false;

            Assert.ThrowsAsync<InvalidOperationException>(() => AsyncInputResponseDispatcher.DispatchAsync(
                client,
                "thread-1",
                "turn-1",
                new[] { new AppServerUserInputItem { Type = "text", Text = "answer" } },
                () => false,
                () => { started = true; return Task.CompletedTask; }));

            Assert.That(started, Is.False);
        }

        [Test]
        public async Task Dispatcher_KeepsResponsePendingWhileNewerTurnRuns()
        {
            var client = new FakeAsyncInputThreadClient
            {
                Result = AppServerTurnSteerResult.NotAttempted("turn-1", "active-turn-changed")
            };
            var started = false;

            var result = await AsyncInputResponseDispatcher.DispatchAsync(
                client,
                "thread-1",
                "turn-1",
                new[] { new AppServerUserInputItem { Type = "text", Text = "answer" } },
                () => true,
                () => { started = true; return Task.CompletedTask; });

            Assert.That(result, Is.EqualTo(AsyncInputDispatchOutcome.NewerTurnRunning));
            Assert.That(started, Is.False);
        }

        [Test]
        public async Task Dispatcher_ServerRejectionStartsNewTurnDespiteStaleBusyState()
        {
            var client = new FakeAsyncInputThreadClient
            {
                Result = AppServerTurnSteerResult.Fallback("turn-1", "cannot accept steering")
            };
            var started = false;

            var result = await AsyncInputResponseDispatcher.DispatchAsync(
                client,
                "thread-1",
                "turn-1",
                new[] { new AppServerUserInputItem { Type = "text", Text = "answer" } },
                () => true,
                () => { started = true; return Task.CompletedTask; });

            Assert.That(result, Is.EqualTo(AsyncInputDispatchOutcome.StartedNewTurn));
            Assert.That(started, Is.True);
        }

        [Test]
        public async Task Dispatcher_WithoutOriginatingTurnSkipsSteer()
        {
            var client = new FakeAsyncInputThreadClient();
            var started = false;

            var result = await AsyncInputResponseDispatcher.DispatchAsync(
                client,
                "thread-1",
                null,
                new[] { new AppServerUserInputItem { Type = "text", Text = "answer" } },
                () => false,
                () => { started = true; return Task.CompletedTask; });

            Assert.That(result, Is.EqualTo(AsyncInputDispatchOutcome.StartedNewTurn));
            Assert.That(client.CallCount, Is.Zero);
            Assert.That(started, Is.True);
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void Form_UsesStaticQuestionTextAndNativeAnswerControls()
        {
            var queue = new AsyncInputRequestQueue();
            var request = BuildRequest("item-1", "Describe the issue.");
            var questions = (List<AppServerToolUserInputQuestion>)request.ToolQuestions;
            questions.Add(CreateChoiceQuestion("async-2", "Choose one.", 3));
            questions.Add(CreateChoiceQuestion("async-3", "Choose from many.", 6));
            queue.Enqueue(request, "Project / Session");

            using (var form = new AsyncInputRequestForm(queue))
            {
                form.RefreshRequests();

                Assert.That(form.FormBorderStyle, Is.EqualTo(FormBorderStyle.Sizable));
                Assert.That(form.MinimumSize.Width, Is.GreaterThanOrEqualTo(640));
                var question = FormTestDriver.FindControlOrDefault<Control>(form, "1. Describe the issue.");
                Assert.That(question, Is.Not.InstanceOf<TextBox>());
                Assert.That(question.AccessibleRole, Is.EqualTo(AccessibleRole.StaticText));
                Assert.That(question.TabStop, Is.True);
                var secondQuestion = FormTestDriver.FindControlOrDefault<Control>(form, "2. Choose one.");
                Assert.That(secondQuestion, Is.Not.Null);
                Assert.That(FormTestDriver.FindControlOrDefault<Control>(form, "3. Choose from many."), Is.Not.Null);
                var answerName = UiLocalization.Text("AsyncInputForm.Accessible.Answer", "Answer");
                Assert.That(FormTestDriver.FindControlOrDefault<TextBox>(form, answerName), Is.Not.Null);
                Assert.That(FormTestDriver.FindControlOrDefault<ComboBox>(form, answerName), Is.Not.Null);
                Assert.That(FindDescendants<RadioButton>(form), Has.Count.EqualTo(4));
                Assert.That(FindDescendants<ComboBox>(form), Has.Count.EqualTo(2));

                var send = FormTestDriver.FindControlOrDefault<Button>(form, UiLocalization.Text("AsyncInputForm.Accessible.Send", "Send answers"));
                Assert.That(send.Enabled, Is.False);
                var answer = FormTestDriver.FindControlOrDefault<TextBox>(form, answerName);
                Assert.That(answer.AcceptsReturn, Is.True);
                answer.Text = "Only this answer";
                Assert.That(send.Enabled, Is.True, "partial answers should be accepted");

                form.SetBusy(true, "Sending");
                Assert.That(answer.Enabled, Is.False);
                Assert.That(FindDescendants<RadioButton>(form).All(control => !control.Enabled), Is.True);
                form.SetBusy(false, string.Empty);
                Assert.That(answer.Enabled, Is.True);

                form.Show();
                form.Activate();
                form.FocusQuestion();
                System.Windows.Forms.Application.DoEvents();
                Assert.That(question.Focused, Is.True, "the first foreground request should focus its static question text");

                secondQuestion.Focus();
                Assert.That(form.SelectNextControl(secondQuestion, true, true, true, true), Is.True);
                Assert.That(FindDescendants<RadioButton>(form).Any(control => control.Focused), Is.True,
                    "Tab from an option question should enter its radio-button group");

                answer.Focus();
                var later = queue.Enqueue(BuildRequest("item-2", "A later question?"), "Project / Other Session");
                form.AddRequest(later);
                System.Windows.Forms.Application.DoEvents();
                Assert.That(answer.Focused, Is.True, "later requests should not move focus or rebuild the current draft");

                var picker = FormTestDriver.FindControlOrDefault<ComboBox>(form, UiLocalization.Text("AsyncInputForm.Accessible.RequestPicker", "Pending request"));
                picker.SelectedIndex = 1;
                picker.SelectedIndex = 0;
                Assert.That(FormTestDriver.FindControlOrDefault<TextBox>(form, answerName).Text, Is.EqualTo("Only this answer"));
                form.Hide();
                form.RefreshRequests();
                form.Show();
                Assert.That(FormTestDriver.FindControlOrDefault<TextBox>(form, answerName).Text, Is.EqualTo("Only this answer"));
                form.Hide();
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void Form_HideReopenAndIgnorePreserveThenUpdatePendingState()
        {
            var queue = new AsyncInputRequestQueue();
            var first = queue.Enqueue(BuildRequest("item-1", "First?"), "Project / One");
            queue.Enqueue(BuildRequest("item-2", "Second?"), "Project / Two");

            using (var form = new AsyncInputRequestForm(queue))
            {
                form.RefreshRequests(first.Key);
                form.IgnoreRequested += (sender, args) =>
                {
                    queue.Remove(args.RequestKey);
                    form.RefreshRequests();
                };
                form.Show();
                var answerName = UiLocalization.Text("AsyncInputForm.Accessible.Answer", "Answer");
                FormTestDriver.FindControlOrDefault<TextBox>(form, answerName).Text = "draft";

                FormTestDriver.FindControlOrDefault<Button>(form, UiLocalization.Text("AsyncInputForm.Accessible.Later", "Answer later")).PerformClick();
                Assert.That(form.Visible, Is.False);
                Assert.That(queue.Count, Is.EqualTo(2));

                form.Show();
                Assert.That(FormTestDriver.FindControlOrDefault<TextBox>(form, answerName).Text, Is.EqualTo("draft"));
                FormTestDriver.FindControlOrDefault<Button>(form, UiLocalization.Text("AsyncInputForm.Accessible.Ignore", "Ignore request")).PerformClick();
                Assert.That(queue.Count, Is.EqualTo(1));
                Assert.That(queue.Find(first.Key), Is.Null);
                form.Hide();
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void Form_NarrowWindowExpandsLongQuestionHeight()
        {
            var longQuestion = string.Join(" ", Enumerable.Repeat("Long question content for larger text and narrow windows.", 20));
            var queue = new AsyncInputRequestQueue();
            queue.Enqueue(BuildRequest("item-1", longQuestion), "Project / One");

            using (var form = new AsyncInputRequestForm(queue))
            {
                form.Width = form.MinimumSize.Width;
                form.RefreshRequests();
                form.Show();
                System.Windows.Forms.Application.DoEvents();
                var question = FormTestDriver.FindControlOrDefault<Control>(form, "1. " + longQuestion);
                var preferred = question.GetPreferredSize(new Size(question.Width, 0));

                Assert.That(question.Height, Is.GreaterThanOrEqualTo(preferred.Height));
                Assert.That(question.Height, Is.GreaterThan(52));
                form.Hide();
            }
        }

        [TestCase(3, "My own answer\r\nwith details")]
        [TestCase(6, "My own answer\r\nwith details")]
        [TestCase(3, "Option 1")]
        [TestCase(6, "Option 1")]
        [Apartment(ApartmentState.STA)]
        public void Form_OtherAnswerValidatesSendsTextAndRetainsIndependentDraft(int optionCount, string draft)
        {
            var queue = new AsyncInputRequestQueue();
            var source = BuildRequest("item-1", "Choose?");
            var questions = (List<AppServerToolUserInputQuestion>)source.ToolQuestions;
            questions.Clear();
            questions.Add(CreateChoiceQuestion("async-1", "Choose?", optionCount));
            questions.Add(new AppServerToolUserInputQuestion { Id = "async-2", Question = "Any details?" });
            var request = queue.Enqueue(source, "Project / One");
            queue.Enqueue(BuildRequest("item-2", "Second request?"), "Project / Two");
            var otherName = UiLocalization.Text("AsyncInputForm.Accessible.OtherAnswer", "Other answer");
            var required = UiLocalization.Text("AsyncInputForm.Error.OtherRequired", "Please enter your answer for Other.");

            using (var form = new AsyncInputRequestForm(queue))
            {
                form.RefreshRequests(request.Key);
                var input = FormTestDriver.FindControlOrDefault<TextBox>(form, otherName);
                Assert.That(input, Is.Not.Null, "Every choice question must provide a custom answer field.");
                form.Show();
                var send = FormTestDriver.FindControlOrDefault<Button>(form, UiLocalization.Text("AsyncInputForm.Accessible.Send", "Send answers"));
                var status = FormTestDriver.FindControlOrDefault<Label>(form, UiLocalization.Text("AsyncInputForm.Accessible.Status", "Status"));
                var submitted = 0;
                form.SubmitRequested += (sender, args) => submitted++;
                Assert.That(input.Visible, Is.False);
                Assert.That(input.Enabled, Is.False);
                Assert.That(input.Multiline && input.AcceptsReturn, Is.True);
                FormTestDriver.FindControlOrDefault<TextBox>(form, UiLocalization.Text("AsyncInputForm.Accessible.Answer", "Answer")).Text = "Details";
                Assert.That(send.Enabled, Is.True, "Unanswered questions must still allow partial responses.");

                SelectChoice(form, optionCount, true);
                Assert.That(input.Visible && input.Enabled, Is.True);
                input.Text = " ";
                Assert.That(send.Enabled, Is.False, "An explicitly chosen Other cannot be silently omitted.");
                Assert.That(status.Text, Is.EqualTo(required));
                input.Text = draft;
                Assert.That(send.Enabled, Is.True);
                Assert.That(status.Text, Is.Empty);
                Assert.That(request.Answers["async-1"], Is.EqualTo(draft));
                Assert.That(AsyncInputRequestQueue.BuildResponseText(request), Does.Contain(draft));
                Assert.That(submitted, Is.Zero, "Editing and selecting must not submit answers.");

                form.SetBusy(true, "Sending");
                Assert.That(input.Enabled, Is.False);
                form.SetBusy(false, string.Empty);
                Assert.That(input.Enabled, Is.True);
                var choice = optionCount <= 4
                    ? (Control)FindDescendants<RadioButton>(form).Last()
                    : FormTestDriver.FindControlOrDefault<ComboBox>(form, UiLocalization.Text("AsyncInputForm.Accessible.Answer", "Answer"));
                choice.Focus();
                Assert.That(form.SelectNextControl(choice, true, true, true, true), Is.True);
                Assert.That(input.Focused, Is.True, "Tab from Other should enter the custom answer field.");

                SelectChoice(form, optionCount, false);
                Assert.That(input.Text, Is.EqualTo(draft));
                Assert.That(input.Visible || input.Enabled, Is.False);
                Assert.That(request.Answers["async-1"], Is.EqualTo("Option 1"));
                var picker = FormTestDriver.FindControlOrDefault<ComboBox>(form, UiLocalization.Text("AsyncInputForm.Accessible.RequestPicker", "Pending request"));
                picker.SelectedIndex = 1;
                picker.SelectedIndex = 0;
                input = FormTestDriver.FindControlOrDefault<TextBox>(form, otherName);
                Assert.That(input.Text, Is.EqualTo(draft));
                Assert.That(input.Visible || input.Enabled, Is.False);
                SelectChoice(form, optionCount, true);
                Assert.That(input.Text, Is.EqualTo(draft));
                form.Hide();
                form.RefreshRequests(request.Key);
                form.Show();
                input = FormTestDriver.FindControlOrDefault<TextBox>(form, otherName);
                Assert.That(input.Visible && input.Enabled, Is.True, "Restore Other even when its text matches a preset.");
                Assert.That(input.Text, Is.EqualTo(draft));
                send.PerformClick();
                Assert.That(submitted, Is.EqualTo(1));
                Assert.That(request.Answers["async-1"], Is.EqualTo(draft));
                form.Hide();
            }
        }

        [TestCase(3)]
        [TestCase(6)]
        [Apartment(ApartmentState.STA)]
        public void Form_EmptyOtherSelectionSurvivesRebuildWithoutBecomingAnAnswer(int optionCount)
        {
            var queue = new AsyncInputRequestQueue();
            var source = BuildRequest("item-1", "Choose?");
            var questions = (List<AppServerToolUserInputQuestion>)source.ToolQuestions;
            questions.Clear();
            questions.Add(CreateChoiceQuestion("async-1", "Choose?", optionCount));
            var request = queue.Enqueue(source, "Project / One");
            using (var form = new AsyncInputRequestForm(queue))
            {
                form.RefreshRequests();
                SelectChoice(form, optionCount, true);
                var otherName = UiLocalization.Text("AsyncInputForm.Accessible.OtherAnswer", "Other answer");
                FormTestDriver.FindControlOrDefault<TextBox>(form, otherName).Text = "  ";
                form.RefreshRequests();
                form.Show();
                var input = FormTestDriver.FindControlOrDefault<TextBox>(form, otherName);
                Assert.That(input.Visible && input.Enabled, Is.True);
                Assert.That(input.Text, Is.EqualTo("  "));
                Assert.That(request.Answers, Is.Empty);
                Assert.That(AsyncInputRequestQueue.BuildResponseText(request), Is.Empty);
                Assert.That(FormTestDriver.FindControlOrDefault<Button>(form, UiLocalization.Text("AsyncInputForm.Accessible.Send", "Send answers")).Enabled, Is.False);
                Assert.That(FormTestDriver.FindControlOrDefault<Label>(form, UiLocalization.Text("AsyncInputForm.Accessible.Status", "Status")).Text,
                    Is.EqualTo(UiLocalization.Text("AsyncInputForm.Error.OtherRequired", "Please enter your answer for Other.")));
                SelectChoice(form, optionCount, false);
                form.SetBusy(true, "Sending");
                form.SetBusy(false, string.Empty);
                Assert.That(input.Visible || input.Enabled, Is.False);
                form.Hide();
            }
        }

        private static void SelectChoice(Form form, int optionCount, bool other)
        {
            if (optionCount <= 4)
            {
                var radios = FindDescendants<RadioButton>(form);
                (other ? radios.Last() : radios.First()).Checked = true;
            }
            else
            {
                var choices = FormTestDriver.FindControlOrDefault<ComboBox>(form, UiLocalization.Text("AsyncInputForm.Accessible.Answer", "Answer"));
                choices.SelectedIndex = other ? choices.Items.Count - 1 : 0;
            }
        }

        private static AppServerEventArgs BuildRequest(string itemId, string question)
        {
            return new AppServerEventArgs(
                "async_input_requested",
                question,
                "thread-1",
                "turn-1",
                itemId,
                toolQuestions: new List<AppServerToolUserInputQuestion>
                {
                    new AppServerToolUserInputQuestion { Id = "async-1", Question = question }
                });
        }

        private static AppServerToolUserInputQuestion CreateChoiceQuestion(string id, string question, int count)
        {
            var result = new AppServerToolUserInputQuestion { Id = id, Question = question };
            for (var i = 1; i <= count; i++)
            {
                result.Options.Add(new AppServerToolUserInputOption
                {
                    Label = "Option " + i,
                    Value = "Option " + i
                });
            }

            return result;
        }

        private static List<T> FindDescendants<T>(Control root)
            where T : Control
        {
            var result = new List<T>();
            foreach (Control child in root.Controls)
            {
                var typed = child as T;
                if (typed != null)
                {
                    result.Add(typed);
                }

                result.AddRange(FindDescendants<T>(child));
            }

            return result;
        }

        private sealed class FakeAsyncInputThreadClient : UnsupportedAppServerThreadClient
        {
            internal AppServerTurnSteerResult Result { get; set; }

            internal Exception Exception { get; set; }

            internal string ExpectedTurnId { get; private set; }

            internal int CallCount { get; private set; }

            public override Task<AppServerTurnSteerResult> TrySteerTurnAsync(
                string threadId,
                IReadOnlyCollection<AppServerUserInputItem> inputItems,
                string clientUserMessageId,
                string expectedTurnId)
            {
                CallCount++;
                ExpectedTurnId = expectedTurnId;
                if (Exception != null)
                {
                    throw Exception;
                }

                return Task.FromResult(Result);
            }
        }
    }
}
