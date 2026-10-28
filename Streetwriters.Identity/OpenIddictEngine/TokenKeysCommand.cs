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
using System.IO;
using MongoDB.Driver;
using Streetwriters.Common;

namespace Streetwriters.Identity.OpenIddictEngine
{
    /// <summary>
    /// <c>Streetwriters.Identity keys list|replace</c>, run inside the Identity container, for replacing the token
    /// keys if they may have leaked (see TokenKeys). Identity must then be restarted with no overlap, every instance
    /// stopped before any starts: a rolling restart briefly runs an instance on the old keys, which rejects the new
    /// sessions, and the API servers cache those answers.
    /// </summary>
    public static class TokenKeysCommand
    {
        public const string Name = "keys";

        public static int Run(string[] args)
        {
            var url = MongoUrl.Create(Constants.MONGODB_CONNECTION_STRING);
            return Run(args, new MongoClient(url).GetDatabase(url.DatabaseName), Console.Out, Console.Error, TimeProvider.System);
        }

        public static int Run(string[] args, IMongoDatabase database, TextWriter output, TextWriter error, TimeProvider timeProvider)
        {
            switch (args)
            {
                case ["list"]:
                    foreach (var key in TokenKeys.List(database))
                    {
                        var state = key.RetiredAt is { } retired ? $"retired {retired:u}" : "current";
                        output.WriteLine($"{key.Use,-10} {key.Generation,3}  {key.Kid}  created {key.CreatedAt:u}  {state}");
                    }
                    return 0;
                case ["replace"]:
                    var (signing, encryption) = TokenKeys.Replace(database, timeProvider.GetUtcNow().UtcDateTime);
                    output.WriteLine($"New keys: signing {signing}, encryption {encryption}; every other key is retired.");
                    output.WriteLine("Nothing changes until Identity restarts, which ends every session. Restart it with no overlap: stop every instance, then start them again.");
                    return 0;
                default:
                    error.WriteLine("Usage: keys list | keys replace");
                    return 2;
            }
        }
    }
}
