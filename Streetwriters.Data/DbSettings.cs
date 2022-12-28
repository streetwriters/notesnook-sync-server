using Streetwriters.Data.Interfaces;

namespace Streetwriters.Data
{
    public class DbSettings : IDbSettings
    {
        public string DatabaseName { get; set; }
        public string ConnectionString { get; set; }
    }
}