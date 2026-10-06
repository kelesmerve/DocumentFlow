using System.Security.Claims;
using DocumentFlow.Application.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocumentFlow.Api.Controllers;

[ApiController, Route("api/auth")]
public sealed class AuthController(IAuthenticationService authentication) : ControllerBase
{
    [HttpPost("login"), AllowAnonymous]
    [ProducesResponseType(typeof(LoginResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await authentication.LoginAsync(request.Email, request.Password, cancellationToken);
        return result is null ? Unauthorized(new { message = "Invalid email or password." }) : Ok(result);
    }

    [HttpGet("me"), Authorize]
    [ProducesResponseType(typeof(AuthenticatedUser), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return !Guid.TryParse(value, out var id) ? Unauthorized() : Ok(await authentication.GetUserAsync(id, cancellationToken));
    }
}
