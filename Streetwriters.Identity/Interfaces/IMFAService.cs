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

using System.Threading.Tasks;
using Streetwriters.Common.Interfaces;
using Streetwriters.Common.Models;
using Streetwriters.Identity.Models;

namespace Streetwriters.Identity.Interfaces
{
    public interface IMFAService
    {
        Task EnableMFAAsync(User user, string primaryMethod);
        Task<bool> DisableMFAAsync(User user);
        Task SetSecondaryMethodAsync(User user, string secondaryMethod);
        string GetPrimaryMethod(User user);
        string GetSecondaryMethod(User user);
        Task<int> GetRemainingValidCodesAsync(User user);
        bool IsValidMFAMethod(string method);
        Task<AuthenticatorDetails> GetAuthenticatorDetailsAsync(User user, IClient client);
        Task SendOTPAsync(User user, IClient client, MultiFactorSetupForm form, bool isSetup = false);
        Task<bool> VerifyOTPAsync(User user, string code, string method);
    }
}