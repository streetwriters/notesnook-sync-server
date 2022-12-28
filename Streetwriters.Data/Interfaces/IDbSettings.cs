namespace Streetwriters.Data.Interfaces
{
    public interface IDbSettings
    {
        string DatabaseName { get; set; }
        string ConnectionString { get; set; }
    }
}