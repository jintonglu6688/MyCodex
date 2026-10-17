using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MyCodex.Application.DTOs;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    internal static class AsyncInputArrivalPlanner
    {
        internal static bool ShouldAnnounce(bool wasEmpty, bool isForeground)
        {
            return !wasEmpty || !isForeground;
        }
    }

    internal sealed class AsyncInputOtherAnswerDraft
    {
        internal string Text { get; set; }
        internal bool IsSelected { get; set; }
    }

    internal sealed class PendingAsyncInputRequest
    {
        internal PendingAsyncInputRequest(AppServerEventArgs source, string sourceName)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            SourceName = sourceName ?? string.Empty;
            Key = BuildKey(source);
            Answers = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        internal string Key { get; }

        internal AppServerEventArgs Source { get; }

        internal string SourceName { get; }

        internal Dictionary<string, string> Answers { get; }

        internal Dictionary<string, AsyncInputOtherAnswerDraft> OtherAnswerDrafts { get; } =
            new Dictionary<string, AsyncInputOtherAnswerDraft>(StringComparer.Ordinal);

        public override string ToString()
        {
            var question = Source.ToolQuestions != null && Source.ToolQuestions.Count > 0
                ? Source.ToolQuestions[0].Question
                : Source.Content;
            return string.IsNullOrWhiteSpace(question)
                ? SourceName
                : SourceName + " - " + question.Trim();
        }

        internal static string BuildKey(AppServerEventArgs source)
        {
            if (!string.IsNullOrWhiteSpace(source.ItemId))
            {
                return (source.ThreadId ?? string.Empty) + "|" + source.ItemId.Trim();
            }

            return string.Join("|", new[]
            {
                source.ThreadId ?? string.Empty,
                source.TurnId ?? string.Empty,
                source.Content ?? string.Empty
            });
        }
    }

    internal sealed class AsyncInputRequestQueue
    {
        private readonly List<PendingAsyncInputRequest> _items = new List<PendingAsyncInputRequest>();
        private readonly HashSet<string> _seenKeys = new HashSet<string>(StringComparer.Ordinal);

        internal IReadOnlyList<PendingAsyncInputRequest> Items => _items;

        internal int Count => _items.Count;

        internal PendingAsyncInputRequest Enqueue(AppServerEventArgs source, string sourceName)
        {
            var candidate = new PendingAsyncInputRequest(source, sourceName);
            var existing = _items.FirstOrDefault(item => string.Equals(item.Key, candidate.Key, StringComparison.Ordinal));
            if (existing != null)
            {
                return null;
            }

            if (!_seenKeys.Add(candidate.Key))
            {
                return null;
            }

            _items.Add(candidate);
            return candidate;
        }

        internal bool Remove(string key)
        {
            var item = Find(key);
            return item != null && _items.Remove(item);
        }

        internal PendingAsyncInputRequest Find(string key)
        {
            return _items.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.Ordinal));
        }

        internal bool ContainsSource(AppServerEventArgs source)
        {
            return source != null && _seenKeys.Contains(PendingAsyncInputRequest.BuildKey(source));
        }

        internal static void SaveAnswers(PendingAsyncInputRequest request, IDictionary<string, string> answers)
        {
            if (request == null)
            {
                return;
            }

            request.Answers.Clear();
            foreach (var pair in answers ?? new Dictionary<string, string>())
            {
                if (!string.IsNullOrWhiteSpace(pair.Value))
                {
                    request.Answers[pair.Key] = pair.Value.Trim();
                }
            }
        }

        internal static string BuildResponseText(PendingAsyncInputRequest request)
        {
            if (request == null || request.Answers.Count == 0)
            {
                return string.Empty;
            }

            var questions = request.Source.ToolQuestions ?? Array.Empty<AppServerToolUserInputQuestion>();
            var text = new StringBuilder(UiLocalization.Text("AsyncInput.Response.Header", "Additional information:"));
            foreach (var question in questions)
            {
                string answer;
                if (question == null || !request.Answers.TryGetValue(question.Id, out answer))
                {
                    continue;
                }

                text.AppendLine();
                text.AppendLine();
                text.Append(UiLocalization.Text("AsyncInput.Response.QuestionPrefix", "Question: "));
                text.AppendLine(question.Question ?? question.Header ?? question.Id);
                text.Append(UiLocalization.Text("AsyncInput.Response.AnswerPrefix", "Answer: "));
                text.Append(answer);
            }

            return text.ToString();
        }
    }
}
