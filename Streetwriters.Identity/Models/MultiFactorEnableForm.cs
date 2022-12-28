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
using Microsoft.AspNetCore.Mvc;

namespace Streetwriters.Identity.Models
{
    public class MultiFactorEnableForm
    {
        [Required]
        [DataType(DataType.Text)]
        [Display(Name = "Authenticator type")]
        [BindProperty(Name = "type")]
        public string Type { get; set; }

        [Required]
        [StringLength(6, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 6)]
        [DataType(DataType.Text)]
        [Display(Name = "Verification Code")]
        [BindProperty(Name = "code")]
        public string VerificationCode { get; set; }

        [BindProperty(Name = "isFallback")]
        public bool IsFallback { get; set; }
    }
}