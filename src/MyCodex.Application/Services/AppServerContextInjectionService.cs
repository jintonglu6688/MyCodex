using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Shared.Localization;

namespace MyCodex.Application.Services
{
    public sealed class AppServerContextInjectionService
    {
        private static readonly HashSet<string> AllowedSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "office-summary",
            "session-import-summary",
            "migration-summary",
            "tool-context"
        };

        private readonly IAppServerThreadClient _threadClient;

        public AppServerContextInjectionService(IAppServerThreadClient threadClient)
        {
            _threadClient = threadClient ?? throw new ArgumentNullException(nameof(threadClient));
        }

        public async Task<AppServerContextInjectionResult> TryInjectAsync(
            AppServerContextInjectionRequest request,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (string.IsNullOrWhiteSpace(request.ThreadId))
            {
                throw new ArgumentException(
                    SharedLocalization.Text(
                        "AppServerClient.Error.ThreadIdRequired",
                        "Thread ID cannot be empty."),
                    nameof(request));
            }

            if (string.IsNullOrWhiteSpace(request.Source) || !AllowedSources.Contains(request.Source.Trim()))
            {
                throw new ArgumentException(
                    SharedLocalization.Text(
                        "AppServerContextInjectionService.Error.SourceNotAllowed",
                        "Context injection source is not allowed."),
                    nameof(request));
            }

            var text = request.Text ?? string.Empty;
            try
            {
                var result = await _threadClient.InjectThreadItemsAsync(
                    request.ThreadId,
                    new[] { BuildSystemMessageItem(text) },
                    cancellationToken)
                    .ConfigureAwait(false);

                var injected = result != null && result.Succeeded;
                return new AppServerContextInjectionResult
                {
                    Injected = injected,
                    ShouldFallbackToUserMessage = !injected && request.Required
                };
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                return new AppServerContextInjectionResult
                {
                    Injected = false,
                    ShouldFallbackToUserMessage = request.Required,
                    ErrorMessage = ex.Message
                };
            }
        }

        private static IDictionary<string, object> BuildSystemMessageItem(string text)
        {
            return new Dictionary<string, object>
            {
                { "type", "message" },
                { "role", "system" },
                {
                    "content",
                    new object[]
                    {
                        new Dictionary<string, object>
                        {
                            { "type", "input_text" },
                            { "text", text ?? string.Empty }
                        }
                    }
                }
            };
        }
    }
}
