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

using Streetwriters.Common.Models;

namespace Streetwriters.Identity.Models
{
    /// <summary>The outcome of a sign in grant, independent of the server issuing the tokens.</summary>
    public abstract record GrantResult
    {
        private GrantResult() { }

        /// <summary>The user is signed in: start a session.</summary>
        public sealed record Authenticated(User User, string AuthenticationMethod) : GrantResult;

        /// <summary>The user passed a step of signing in: issue a short-lived token for the next step.</summary>
        public sealed record Step(User User, string Scope, MFARequiredResponse? AdditionalData) : GrantResult;

        /// <summary>The password grant needs a second factor: respond with <see cref="Data"/> and a token for the mfa step.</summary>
        public sealed record MFARequired(User User, MFARequiredResponse Data) : GrantResult;

        public sealed record Failed(string Error, string? Description = null) : GrantResult;

        public static readonly Failed InvalidGrant = new("invalid_grant");
    }
}
