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
using System.Linq;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using Notesnook.API.Models;
using Streetwriters.Data.Interfaces;
using Streetwriters.Data.Repositories;

namespace Notesnook.API.Controllers
{
    [ApiController]
    [Route("monographs")]
    [Authorize("Sync")]
    public class MonographsController : ControllerBase
    {
        private Repository<Monograph> Monographs { get; set; }
        private readonly IUnitOfWork unit;
        private const int MAX_DOC_SIZE = 15 * 1024 * 1024;
        public MonographsController(Repository<Monograph> monographs, IUnitOfWork unitOfWork)
        {
            Monographs = monographs;
            unit = unitOfWork;
        }

        [HttpPost]
        public async Task<IActionResult> PublishAsync([FromBody] Monograph monograph)
        {
            var userId = this.User.FindFirstValue("sub");
            if (userId == null) return Unauthorized();

            if (await Monographs.GetAsync(monograph.Id) != null) return base.Conflict("This monograph is already published.");

            if (monograph.EncryptedContent == null)
                monograph.CompressedContent = monograph.Content.CompressBrotli();
            monograph.UserId = userId;
            monograph.DatePublished = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();


            if (monograph.EncryptedContent?.Cipher.Length > MAX_DOC_SIZE || monograph.CompressedContent?.Length > MAX_DOC_SIZE)
                return base.BadRequest("Monograph is too big. Max allowed size is 15mb.");

            Monographs.Insert(monograph);

            if (!await unit.Commit()) return BadRequest();
            return Ok(new
            {
                id = monograph.Id
            });
        }

        [HttpPatch]
        public async Task<IActionResult> UpdateAsync([FromBody] Monograph monograph)
        {
            if (await Monographs.GetAsync(monograph.Id) == null) return NotFound();

            if (monograph.EncryptedContent?.Cipher.Length > MAX_DOC_SIZE || monograph.CompressedContent?.Length > MAX_DOC_SIZE)
                return base.BadRequest("Monograph is too big. Max allowed size is 15mb.");

            if (monograph.EncryptedContent == null)
                monograph.CompressedContent = monograph.Content.CompressBrotli();
            else
                monograph.Content = null;

            monograph.DatePublished = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            Monographs.Update(monograph.Id, monograph);

            if (!await unit.Commit()) return BadRequest();
            return Ok(new
            {
                id = monograph.Id
            });
        }

        [HttpGet]
        public async Task<IActionResult> GetUserMonographsAsync()
        {
            var userId = this.User.FindFirstValue("sub");
            if (userId == null) return Unauthorized();

            var monographs = (await Monographs.Collection.FindAsync(Builders<Monograph>.Filter.Eq("UserId", userId), new FindOptions<Monograph, ObjectWithId>
            {
                Projection = Builders<Monograph>.Projection.Include("Id"),
            })).ToEnumerable();
            return Ok(monographs.Select((m) => m.Id));
        }


        [HttpGet("{id}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetMonographAsync([FromRoute] string id)
        {
            var monograph = await Monographs.GetAsync(id);
            if (monograph == null)
            {
                return NotFound(new
                {
                    error = "invalid_id",
                    error_description = $"No such monograph found."
                });
            }

            if (monograph.EncryptedContent == null)
                monograph.Content = monograph.CompressedContent.DecompressBrotli();
            return Ok(monograph);
        }

        [HttpGet("{id}/destruct")]
        [AllowAnonymous]
        public async Task<IActionResult> DestructMonographAsync([FromRoute] string id)
        {
            var monograph = await Monographs.GetAsync(id);
            if (monograph == null)
            {
                return NotFound(new
                {
                    error = "invalid_id",
                    error_description = $"No such monograph found."
                });
            }

            if (monograph.SelfDestruct)
                await Monographs.DeleteByIdAsync(monograph.Id);

            return Ok();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteAsync([FromRoute] string id)
        {
            Monographs.DeleteById(id);
            if (!await unit.Commit()) return BadRequest();
            return Ok();
        }
    }
}