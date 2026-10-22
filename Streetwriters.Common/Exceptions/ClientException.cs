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

namespace Streetwriters.Common.Exceptions
{
    // Only these messages reach clients; any other exception becomes a generic 500.
    public class ClientException(string message, int statusCode = 400, Exception? innerException = null) : Exception(message, innerException)
    {
        public int StatusCode { get; } = statusCode;
    }

    public class NotFoundException(string message) : ClientException(message, 404);

    public class ConflictException(string message) : ClientException(message, 409);

    public class ServiceUnavailableException(string message, Exception? innerException = null) : ClientException(message, 503, innerException)
    {
        public TimeSpan RetryAfter { get; init; } = TimeSpan.FromSeconds(30);
    }
}
