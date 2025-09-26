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

using System;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using Streetwriters.Common.Enums;
using Streetwriters.Common.Interfaces;

namespace Streetwriters.Common.Messages
{
    public class CreateSubscriptionMessageV2
    {
        [JsonPropertyName("userId")]
        public string UserId { get; set; }

        [JsonPropertyName("provider")]
        public SubscriptionProvider Provider { get; set; }

        [JsonPropertyName("appId")]
        public ApplicationType AppId { get; set; }

        [JsonPropertyName("plan")]
        public SubscriptionPlan Plan { get; set; }

        [JsonPropertyName("status")]
        public SubscriptionStatus Status { get; set; }

        [JsonPropertyName("start")]
        public long StartTime { get; set; }

        [JsonPropertyName("expiry")]
        public long ExpiryTime { get; set; }

        [JsonPropertyName("orderId")]
        public string OrderId { get; set; }

        [JsonPropertyName("subscriptionId")]
        public string SubscriptionId { get; set; }

        [JsonPropertyName("productId")]
        public string ProductId { get; set; }

        [JsonPropertyName("timestamp")]
        public long Timestamp { get; set; }

        [JsonPropertyName("trialExpiry")]
        public long TrialExpiryTime { get; set; }

        [JsonPropertyName("googlePurchaseToken")]
        public string? GooglePurchaseToken { get; set; }
    }
}