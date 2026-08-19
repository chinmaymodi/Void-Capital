using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace VoidCapital.Api.Tests;

/// <summary>
/// Attaches an authenticated admin principal to a controller so unit tests
/// (which call actions directly, bypassing the auth middleware) pass the
/// [Authorize] gate and the per-user CanAccess checks.
/// </summary>
public static class ControllerTestAuth
{
    public static T AsAdmin<T>(this T controller) where T : ControllerBase
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim(ClaimTypes.Role, "Admin") }, "test")),
            },
        };
        return controller;
    }
}