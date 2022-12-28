/*
This file is part of the Notesnook Sync Server project (https://notesnook.com/)

Copyright (C) 2022 Streetwriters (Private) Limited

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
