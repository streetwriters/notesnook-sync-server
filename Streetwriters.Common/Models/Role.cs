/*
This file is part of the Notesnook Sync Server project (https://notesnook.com/)

Copyright (C) 2023 Streetwriters (Private) Limited

This program is free software: you can redistribute it and/or modify
it under the terms of the Affero GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
Affero GNU General Public License for more details.

You should have received a copy of the Affero GNU General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/



using AspNetCore.Identity.Mongo.Model;

namespace Streetwriters.Common.Models
{
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
