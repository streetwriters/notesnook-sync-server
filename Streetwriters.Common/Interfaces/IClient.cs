using Streetwriters.Common.Enums;

namespace Streetwriters.Common.Interfaces
{
    public interface IClient
    {
        string Id { get; set; }
        string Name { get; set; }
        string[] ProductIds { get; set; }
        ApplicationType Type { get; set; }
        ApplicationType AppId { get; set; }
        string SenderEmail { get; set; }
        string SenderName { get; set; }
        string WelcomeEmailTemplateId { get; set; }
    }
}
