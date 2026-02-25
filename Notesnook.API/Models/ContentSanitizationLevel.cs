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

namespace Notesnook.API.Models
{
    public enum ContentSanitizationLevel
    {
        Unknown = 0,
        /// <summary>
        /// Full sanitization applied: links, iframes, images, and other embeds are stripped.
        /// Applied to monographs published by free-tier users.
        /// </summary>
        Full = 1,

        /// <summary>
        /// Partial sanitization: only unsafe/malicious URLs are removed; rich content is preserved.
        /// Applied to monographs published by subscribed users. Requires re-sanitization if the
        /// publisher's subscription lapses.
        /// </summary>
        Partial = 2
    }
}
