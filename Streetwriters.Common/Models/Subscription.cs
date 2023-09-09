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

using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Streetwriters.Common.Enums;
using Streetwriters.Common.Interfaces;

namespace Streetwriters.Common.Models
{
    public class Subscription : ISubscription
    {
        public Subscription()
        {
            Id = ObjectId.GenerateNewId().ToString();
        }

        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("userId")]
        public string UserId { get; set; }

        [JsonIgnore]
        public string OrderId { get; set; }
        [JsonIgnore]
        public string SubscriptionId { get; set; }

        [BsonRepresentation(BsonType.Int32)]
        [JsonPropertyName("appId")]
        public ApplicationType AppId { get; set; }

        [JsonPropertyName("start")]
        public long StartDate { get; set; }

        [JsonPropertyName("expiry")]
        public long ExpiryDate { get; set; }

        [BsonRepresentation(BsonType.Int32)]
        [JsonPropertyName("provider")]
        public SubscriptionProvider Provider { get; set; }

        [BsonRepresentation(BsonType.Int32)]
        [JsonPropertyName("type")]
        public SubscriptionType Type { get; set; }

        [JsonPropertyName("cancelURL")]
        public string CancelURL { get; set; }

        [JsonPropertyName("updateURL")]
        public string UpdateURL { get; set; }

        [JsonPropertyName("productId")]
        public string ProductId { get; set; }

        [JsonIgnore]
        public int TrialExtensionCount { get; set; }
    }
}
