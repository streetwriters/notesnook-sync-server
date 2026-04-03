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

using Notesnook.API.Models;
using Streetwriters.Common;

namespace Notesnook.API.Helpers
{
    public class UrlHelper
    {
        public static string ConstructPublishUrl(string slug)
        {
            var baseUrl = Constants.MONOGRAPH_PUBLIC_URL;
            return $"{baseUrl}/{slug}";
        }
        public static string ConstructPublishUrl(Monograph monograph)
        {
            if (!string.IsNullOrEmpty(monograph.Slug))
            {
                return ConstructPublishUrl("s/" + monograph.Slug);
            }
            return ConstructPublishUrl(monograph.ItemId ?? monograph.Id);
        }

        public static string ConstructPublishUrl(MonographMetadata metadata)
        {
            if (!string.IsNullOrEmpty(metadata.PublishUrl))
            {
                return ConstructPublishUrl("s/" + metadata.PublishUrl);
            }
            return ConstructPublishUrl(metadata.PublishUrl ?? metadata.ItemId);
        }
    }
}
