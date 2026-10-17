using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;

namespace MyCodex.WinForms.Skills
{
    internal static class SkillSourcePluginLoader
    {
        public static async Task<List<AppServerPluginSummary>> TryLoadAsync(IAppServerClient appServerClient)
        {
            return await TryLoadAsync(
                appServerClient,
                appServerClient != null ? appServerClient.ProtocolCapabilities : null).ConfigureAwait(true);
        }

        public static async Task<List<AppServerPluginSummary>> TryLoadAsync(
            IAppServerPluginClient appServerClient,
            AppServerProtocolCapabilities protocolCapabilities)
        {
            if (appServerClient == null ||
                !AppServerProtocolFeatureSupport.IsSupported(
                    protocolCapabilities,
                    AppServerProtocolFeature.PluginManagement))
            {
                return new List<AppServerPluginSummary>();
            }

            try
            {
                var snapshot = await AppServerInstalledPluginObservationService.LoadAsync(
                    appServerClient,
                    protocolCapabilities,
                    System.Threading.CancellationToken.None).ConfigureAwait(true);
                return (snapshot.Plugins ?? new List<AppServerPluginSummary>())
                    .Where(item => item != null && !string.IsNullOrWhiteSpace(item.SourcePath))
                    .ToList();
            }
            catch (Exception)
            {
                return new List<AppServerPluginSummary>();
            }
        }
    }
}
