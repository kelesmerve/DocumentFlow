using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocumentFlow.IntegrationTests;

[ApiController, Route("api/test/authorization")]
public sealed class AuthorizationProbeController : ControllerBase
{
    [HttpGet("admin"), Authorize(Roles = "Admin")]
    public IActionResult AdminOnly() => Ok(new { authorized = true });

    [HttpGet("failure"), AllowAnonymous]
    public IActionResult Failure() => throw new InvalidOperationException("Sensitive internal diagnostic marker");
}
