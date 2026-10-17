using System;
using System.Collections.Generic;
using System.Linq;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.Services
{
    public sealed class AppServerConfigWriteGuardResult
    {
        internal AppServerConfigWriteGuardResult(bool canWrite, string expectedVersion, string reasonCode)
        {
            CanWrite = canWrite;
            ExpectedVersion = expectedVersion;
            ReasonCode = reasonCode;
        }

        public bool CanWrite { get; private set; }

        public string ExpectedVersion { get; private set; }

        public string ReasonCode { get; private set; }
    }

    public static class AppServerConfigWriteGuard
    {
        public static AppServerConfigWriteGuardResult Evaluate(
            AppServerConfigSnapshot snapshot,
            IEnumerable<AppServerConfigWriteEdit> edits)
        {
            var editList = (edits ?? Enumerable.Empty<AppServerConfigWriteEdit>())
                .Where(edit => edit != null && !string.IsNullOrWhiteSpace(edit.KeyPath))
                .ToList();
            if (snapshot == null || editList.Count == 0)
            {
                return Blocked("missing-observation");
            }

            string expectedVersion = null;
            foreach (var edit in editList)
            {
                var origin = FindOrigin(snapshot.Origins, edit.KeyPath);
                if (origin == null || string.IsNullOrWhiteSpace(origin.Version))
                {
                    return Blocked("missing-version");
                }

                if (expectedVersion == null)
                {
                    expectedVersion = origin.Version;
                }
                else if (!string.Equals(expectedVersion, origin.Version, StringComparison.Ordinal))
                {
                    return Blocked("multiple-versions");
                }
            }

            return new AppServerConfigWriteGuardResult(true, expectedVersion, null);
        }

        public static AppServerConfigWriteGuardResult EvaluateUserConfig(
            AppServerConfigSnapshot snapshot,
            IEnumerable<AppServerConfigWriteEdit> edits)
        {
            var editList = (edits ?? Enumerable.Empty<AppServerConfigWriteEdit>())
                .Where(edit => edit != null && !string.IsNullOrWhiteSpace(edit.KeyPath))
                .ToList();
            if (snapshot == null || editList.Count == 0)
            {
                return Blocked("missing-observation");
            }

            var userLayers = (snapshot.Layers ?? new List<AppServerConfigLayer>())
                .Where(layer => layer != null &&
                    string.Equals(layer.SourceType, "user", StringComparison.OrdinalIgnoreCase) &&
                    string.IsNullOrWhiteSpace(layer.DisabledReason))
                .ToList();
            if (userLayers.Count == 1 && !string.IsNullOrWhiteSpace(userLayers[0].Version))
            {
                return new AppServerConfigWriteGuardResult(true, userLayers[0].Version, null);
            }

            if (userLayers.Count > 1)
            {
                return Blocked("multiple-user-layers");
            }

            foreach (var edit in editList)
            {
                var origin = FindOrigin(snapshot.Origins, edit.KeyPath);
                if (origin == null ||
                    !string.Equals(origin.SourceType, "user", StringComparison.OrdinalIgnoreCase))
                {
                    return Blocked("missing-user-layer");
                }
            }

            return Evaluate(snapshot, editList);
        }

        private static AppServerConfigOrigin FindOrigin(
            IDictionary<string, AppServerConfigOrigin> origins,
            string keyPath)
        {
            if (origins == null || string.IsNullOrWhiteSpace(keyPath))
            {
                return null;
            }

            return origins
                .Where(pair => pair.Value != null && IsPathPrefix(pair.Key, keyPath))
                .OrderByDescending(pair => pair.Key.Length)
                .Select(pair => pair.Value)
                .FirstOrDefault();
        }

        private static bool IsPathPrefix(string candidate, string keyPath)
        {
            return !string.IsNullOrWhiteSpace(candidate) &&
                   (string.Equals(candidate, keyPath, StringComparison.OrdinalIgnoreCase) ||
                    keyPath.StartsWith(candidate + ".", StringComparison.OrdinalIgnoreCase));
        }

        private static AppServerConfigWriteGuardResult Blocked(string reasonCode)
        {
            return new AppServerConfigWriteGuardResult(false, null, reasonCode);
        }
    }
}
