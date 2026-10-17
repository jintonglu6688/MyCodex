namespace MyCodex.Infrastructure.Office
{
    public interface IOfficeCliEnvironmentStore
    {
        string GetUserPath();

        void SetUserPath(string path);

        string GetProcessPath();

        void SetProcessPath(string path);
    }
}
