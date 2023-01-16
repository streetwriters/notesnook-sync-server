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

using System.Collections.Generic;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using AspNetCore.Identity.Mongo.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Streetwriters.Common.Models;
using Streetwriters.Identity.Interfaces;
using Streetwriters.Identity.Models;

namespace Streetwriters.Identity.Controllers
{
    public abstract class IdentityControllerBase : ControllerBase
    {
        protected UserManager<User> UserManager { get; set; }
        protected SignInManager<User> SignInManager { get; set; }
        protected RoleManager<MongoRole> RoleManager { get; set; }
        protected IEmailSender EmailSender { get; set; }
        protected UrlEncoder UrlEncoder { get; set; }
        protected IMFAService MFAService { get; set; }
        public IdentityControllerBase(
            UserManager<User> _userManager,
            IEmailSender _emailSender,
            SignInManager<User> _signInManager,
            RoleManager<MongoRole> _roleManager,
            IMFAService _mfaService
        )
        {
            UserManager = _userManager;
            SignInManager = _signInManager;
            RoleManager = _roleManager;
            EmailSender = _emailSender;
            MFAService = _mfaService;
            UrlEncoder = UrlEncoder.Default;
        }

        public override BadRequestObjectResult BadRequest(object error)
        {
            if (error is IEnumerable<string> errors)
            {
                return base.BadRequest(new { errors });
            }
            return base.BadRequest(new { error });
        }
    }
}