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

using System.Reactive.Subjects;
using System.Threading.Tasks;
using Streetwriters.Common.Messages;
using WampSharp.V2;
using WampSharp.V2.Client;

namespace Streetwriters.Common.Helpers
{
    public class WampHelper
    {
        public static async Task<IWampRealmProxy> OpenWampChannelAsync<T>(string server, string realmName)
        {
            DefaultWampChannelFactory channelFactory = new DefaultWampChannelFactory();

            IWampChannel channel = channelFactory.CreateJsonChannel(server, realmName);

            await channel.Open();

            return channel.RealmProxy;
        }

        public static void PublishMessage<T>(IWampRealmProxy realm, string topicName, T message)
        {
            var subject = realm.Services.GetSubject<T>(topicName);
            subject.OnNext(message);
        }
    }
}