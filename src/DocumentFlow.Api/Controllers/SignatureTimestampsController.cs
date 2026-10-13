using System.Security.Claims;
using DocumentFlow.Application.Timestamps;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocumentFlow.Api.Controllers;

[ApiController, Authorize, Route("api/signatures/{signatureId:guid}")]
public sealed class SignatureTimestampsController(ISignatureTimestampService timestamps) : ControllerBase
{
    [HttpPost("timestamp")]
    public async Task<IActionResult> Create(Guid signatureId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        try { return Ok(await timestamps.CreateAsync(signatureId, userId, User.IsInRole("Admin"), cancellationToken)); }
        catch (TimestampAccessException) { return Forbid(); }
        catch (TimestampStateException exception) { return Conflict(new { message = exception.Message }); }
        catch (TimestampProviderUnavailableException exception) { return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = exception.Message }); }
    }

    [HttpGet("timestamps")]
    public async Task<IActionResult> List(Guid signatureId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var result = await timestamps.ListAsync(signatureId, userId, User.IsInRole("Admin"), cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("timestamps/{timestampId:guid}/verify")]
    public async Task<IActionResult> Verify(Guid signatureId, Guid timestampId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var result = await timestamps.VerifyAsync(signatureId, timestampId, userId, User.IsInRole("Admin"), cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    private bool TryGetUserId(out Guid userId) => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
}
