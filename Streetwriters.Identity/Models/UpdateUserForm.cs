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
    public class UpdateUserForm
    {
        [Required]
        [BindProperty(Name = "type")]
        public string Type
        {
            get; set;
        }

        [BindProperty(Name = "enabled")]
        public bool Enabled
        {
            get; set;
        }

        [BindProperty(Name = "old_password")]
        public string OldPassword
        {
            get; set;
        }

        [BindProperty(Name = "new_password")]
        public string NewPassword
        {
            get; set;
        }


        [BindProperty(Name = "password")]
        public string Password
        {
            get; set;
        }

        [BindProperty(Name = "new_email")]
        public string NewEmail
        {
            get; set;
        }


        [BindProperty(Name = "verification_code")]
        public string VerificationCode
        {
            get; set;
        }
    }
}