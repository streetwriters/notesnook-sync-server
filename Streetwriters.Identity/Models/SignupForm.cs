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
using Microsoft.AspNetCore.Mvc;

namespace Streetwriters.Identity.Models
{
    public class SignupForm
    {
        [Required]
        [StringLength(120, ErrorMessage = "Password must be longer than or equal to 8 characters.", MinimumLength = 8)]
        [BindProperty(Name = "password")]
        public string Password
        {
            get; set;
        }

        [Required]
        [BindProperty(Name = "email")]
        [EmailAddress]
        public string Email
        {
            get; set;
        }

        [BindProperty(Name = "username")]
        public string Username
        {
            get; set;
        }

        [Required]
        [BindProperty(Name = "client_id")]
        public string ClientId
        {
            get; set;
        }
    }
}