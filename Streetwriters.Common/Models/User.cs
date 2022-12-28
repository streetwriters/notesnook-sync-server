

using AspNetCore.Identity.Mongo.Model;
using Streetwriters.Data.Attributes;

namespace Streetwriters.Common.Models
{
    [BsonCollection("identity", "users")]
    public class User : MongoUser
    {
    }
}
