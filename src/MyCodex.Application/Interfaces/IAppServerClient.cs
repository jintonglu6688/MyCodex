namespace MyCodex.Application.Interfaces
{
    public interface IAppServerClient :
        IAppServerConnectionClient,
        IAppServerThreadClient,
        IAppServerAuthenticationClient,
        IAppServerConfigClient,
        IAppServerExtensionClient,
        IAppServerInteractionClient,
        IAppServerCommandClient,
        IAppServerExternalAgentConfigClient,
        IAppServerFileSystemClient,
        IAppServerFuzzyFileSearchClient,
        IAppServerWindowsSandboxClient,
        IAppServerGitDiffClient
    {
    }
}
