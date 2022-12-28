

using AspNetCore.Identity.Mongo.Model;
using Streetwriters.Data.Attributes;

namespace Streetwriters.Common.Models
{
    [BsonCollection("identity", "roles")]
    public class Role : MongoRole
    {
        //     [DataMember(Name = "email")]
        //     [BsonElement("email")]
        //     public string Email
        //     {
        //         get; set;
        //     }

        //     [DataMember(Name = "isEmailConfirmed")]
        //     [BsonElement("isEmailConfirmed")]
        //     public bool IsEmailConfirmed { get; set; }

        //     [DataMember(Name = "username")]
        //     [BsonElement("username")]
        //     public string Username
        //     {
        //         get; set;
        //     }

        //     [BsonId]
        //     [BsonRepresentation(BsonType.ObjectId)]
        //     public string Id
        //     {
        //         get; set;
        //     }

        //     [IgnoreDataMember]
        //     [BsonElement("passwordHash")]
        //     public string PasswordHash
        //     {
        //         get; set;
        //     }

        // [DataMember(Name = "salt")]
        // public string Salt
        // {
        //     get; set;
        // }
    }
    /* 
        public class Picture
        {
            [DataMember(Name = "thumbnail")]
            public string Thumbnail
            {
                get; set;
            }
            [DataMember(Name = "full")]
            public string Full
            {
                get; set;
            }
        }

        public class Streetwriters
        {


        [DataMember(Name = "fullName")]
        public string FullName
        {
            get; set;
        }

            [DataMember(Name = "biography")]
            [StringLength(240)]
            public string Biography
            {
                get; set;
            }

            [DataMember(Name = "favoriteWords")]
            public string FavoriteWords
            {
                get; set;
            }

            [DataMember(Name = "profilePicture")]
            public Picture ProfilePicture
            {
                get; set;
            }

            [DataMember(Name = "followers")]
            public string[] Followers
            {
                get; set;
            }

            [DataMember(Name = "following")]
            public string[] Following
            {
                get; set;
            }

            [DataMember(Name = "website")]
            [Url]
            public string Website
            {
                get; set;
            }

            [DataMember(Name = "instagram")]
            public string Instagram
            {
                get; set;
            }

            [DataMember(Name = "twitter")]
            public string Twitter
            {
                get; set;
            }
        } */
}
