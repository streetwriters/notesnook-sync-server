using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Notesnook.API.Controllers;

namespace Notesnook.API.Tests.Helpers
{
  public static class ControllerTestHelper
  {
    public static void SetupControllerContext(MonographsController controller, string? userId = null, string? jti = null)
    {
      var claims = new List<Claim>();

      if (!string.IsNullOrEmpty(userId))
      {
        claims.Add(new Claim("sub", userId));
      }

      if (!string.IsNullOrEmpty(jti))
      {
        claims.Add(new Claim("jti", jti));
      }

      var identity = new ClaimsIdentity(claims, "Test");
      var principal = new ClaimsPrincipal(identity);
      controller.ControllerContext = new ControllerContext
      {
        HttpContext = new DefaultHttpContext
        {
          User = principal
        }
      };
    }

    public static void SetupUnauthenticatedControllerContext(MonographsController controller)
    {
      controller.ControllerContext = new ControllerContext
      {
        HttpContext = new DefaultHttpContext
        {
          User = new ClaimsPrincipal(new ClaimsIdentity())
        }
      };
    }
  }
}
