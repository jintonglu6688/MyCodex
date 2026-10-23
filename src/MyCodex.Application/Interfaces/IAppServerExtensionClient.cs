namespace MyCodex.Application.Interfaces
{
    public interface IAppServerExtensionClient :
        IAppServerMcpClient,
        IAppServerSkillClient,
        IAppServerPluginClient
    {
    }
}
