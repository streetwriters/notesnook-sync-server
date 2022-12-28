namespace Streetwriters.Common.Interfaces
{
    public interface IResponse
    {
        bool Success { get; set; }
        int StatusCode { get; set; }
    }
}