using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Streetwriters.Common.Enums;
using Streetwriters.Common.Interfaces;

namespace Streetwriters.Common.Models
{
    public class Client : IClient
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string[] ProductIds { get; set; }
        public ApplicationType Type { get; set; }
        public ApplicationType AppId { get; set; }
        public string SenderEmail { get; set; }
        public string SenderName { get; set; }
        public string WelcomeEmailTemplateId { get; set; }
    }
}
