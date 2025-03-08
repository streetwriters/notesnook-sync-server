namespace Notesnook.API.Models;

public class PartETagWrapper
{
    public int PartNumber { get; set; }
    public string ETag { get; set; }

    /// <summary>
    /// Default constructor.
    /// </summary>
    public PartETagWrapper()
    {
    }
}