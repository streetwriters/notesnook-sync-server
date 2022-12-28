namespace Notesnook.API.Interfaces
{
    public interface IEncrypted
    {
        string Cipher { get; set; }
        string IV { get; set; }
        long Length { get; set; }
        string Salt { get; set; }
    }
}
