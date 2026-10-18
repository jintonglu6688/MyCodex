using System;
using System.Collections.Generic;
using System.IO;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Shared.Helpers;
using MyCodex.Shared.Localization;

namespace MyCodex.Application.Services
{
    public sealed class AutomationInputItemPlanner
    {
        private readonly Func<string, bool> _pathExists;

        public AutomationInputItemPlanner()
            : this(path => File.Exists(path) || Directory.Exists(path))
        {
        }

        public AutomationInputItemPlanner(Func<string, bool> pathExists)
        {
            _pathExists = pathExists ?? throw new ArgumentNullException(nameof(pathExists));
        }

        public AutomationInputItemPlan Build(AutomationDefinitionSnapshot definition)
        {
            var inputItems = new List<AppServerUserInputItem>();
            if (definition == null)
            {
                return AutomationInputItemPlan.Ok(inputItems);
            }

            foreach (var item in definition.InputItems ?? new List<AutomationInputItemSnapshot>())
            {
                if (item == null)
                {
                    continue;
                }

                var planned = BuildStructuredInput(item);
                if (!planned.Succeeded)
                {
                    return planned;
                }

                inputItems.AddRange(planned.InputItems);
            }

            if (!string.IsNullOrWhiteSpace(definition.Prompt))
            {
                inputItems.Add(new AppServerUserInputItem
                {
                    Type = "text",
                    Text = definition.Prompt.Trim()
                });
            }

            return AutomationInputItemPlan.Ok(inputItems);
        }

        private AutomationInputItemPlan BuildStructuredInput(AutomationInputItemSnapshot item)
        {
            var displayName = TextValueHelper.FirstNonEmptyTrimmed(item.Name, item.DisplayName, item.Path, "(unnamed)");
            var path = item.Path == null ? null : item.Path.Trim();
            if (string.IsNullOrWhiteSpace(path))
            {
                return AutomationInputItemPlan.Fail(BuildPathRequiredMessage(item.Type, displayName));
            }

            if (!_pathExists(path))
            {
                return AutomationInputItemPlan.Fail(BuildUnavailableMessage(item.Type, displayName));
            }

            return AutomationInputItemPlan.Ok(new List<AppServerUserInputItem>
            {
                new AppServerUserInputItem
                {
                    Type = item.Type == AutomationInputItemKind.Plugin ? "mention" : "skill",
                    Name = TextValueHelper.FirstNonEmptyTrimmed(item.Name, item.DisplayName, path),
                    Path = path
                }
            });
        }

        private static string BuildPathRequiredMessage(AutomationInputItemKind kind, string displayName)
        {
            if (kind == AutomationInputItemKind.Plugin)
            {
                return string.Format(
                    SharedLocalization.Text("AutomationInputItemPlanner.Error.PluginPathRequired", "Automation references a plugin without a path: {0}"),
                    displayName);
            }

            return string.Format(
                SharedLocalization.Text("AutomationInputItemPlanner.Error.SkillPathRequired", "Automation references a skill without a path: {0}"),
                displayName);
        }

        private static string BuildUnavailableMessage(AutomationInputItemKind kind, string displayName)
        {
            if (kind == AutomationInputItemKind.Plugin)
            {
                return string.Format(
                    SharedLocalization.Text("AutomationInputItemPlanner.Error.PluginUnavailable", "Automation referenced plugin is unavailable: {0}"),
                    displayName);
            }

            return string.Format(
                SharedLocalization.Text("AutomationInputItemPlanner.Error.SkillUnavailable", "Automation referenced skill is unavailable: {0}"),
                displayName);
        }
    }
}
