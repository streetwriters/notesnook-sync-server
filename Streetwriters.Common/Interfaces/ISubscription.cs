using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using Streetwriters.Common.Attributes;
using Streetwriters.Common.Converters;
using Streetwriters.Common.Enums;
using Streetwriters.Common.Models;

namespace Streetwriters.Common.Interfaces
{
    [JsonInterfaceConverter(typeof(InterfaceConverter<ISubscription, Subscription>))]
    public interface ISubscription : IDocument
    {
        string UserId { get; set; }
        ApplicationType AppId { get; set; }
        SubscriptionProvider Provider { get; set; }
        long StartDate { get; set; }
        long ExpiryDate { get; set; }
        SubscriptionType Type { get; set; }
        string OrderId { get; set; }
        string SubscriptionId { get; set; }
    }
}
