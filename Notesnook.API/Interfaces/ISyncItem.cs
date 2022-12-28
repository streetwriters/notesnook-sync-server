using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson.Serialization.Serializers;
using Notesnook.API.Models;
using Streetwriters.Common.Attributes;
using Streetwriters.Common.Converters;
using Streetwriters.Common.Interfaces;

namespace Notesnook.API.Interfaces
{
    [BsonSerializer(typeof(ImpliedImplementationInterfaceSerializer<ISyncItem, SyncItem>))]
    [JsonInterfaceConverter(typeof(InterfaceConverter<ISyncItem, SyncItem>))]
    public interface ISyncItem
    {
        long DateSynced
        {
            get; set;
        }

        string UserId { get; set; }
        string Algorithm { get; set; }
        string IV { get; set; }
    }
}
