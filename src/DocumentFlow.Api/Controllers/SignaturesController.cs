using System.Security.Claims;
using DocumentFlow.Application.Signatures;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocumentFlow.Api.Controllers;

[ApiController, Authorize, Route("api/documents/{id:guid}/versions/{versionId:guid}")]
public sealed class SignaturesController(IDocumentSignatureService signatures) : ControllerBase
{
    [HttpPost("sign")]
    [Authorize(Roles = "Manager")]
    public async Task<IActionResult> Sign(Guid id, Guid versionId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        try { return Ok(await signatures.SignAsync(id, versionId, userId, cancellationToken)); }
        catch (SignatureAccessException exception) { return ForbidOrNotFound(exception); }
        catch (SignatureStateException exception) { return Conflict(new { message = exception.Message }); }
        catch (SignatureIntegrityException exception) { return Conflict(new { message = exception.Message }); }
        catch (SignatureProviderUnavailableException exception) { return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = exception.Message }); }
    }

    [HttpGet("signatures")]
    public async Task<IActionResult> List(Guid id, Guid versionId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var result = await signatures.ListAsync(id, versionId, userId, User.IsInRole("Admin"), cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("verify")]
    public async Task<IActionResult> Verify(Guid id, Guid versionId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var result = await signatures.VerifyAsync(id, versionId, userId, User.IsInRole("Admin"), cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    private IActionResult ForbidOrNotFound(SignatureAccessException exception) =>
        User.IsInRole("Manager") ? NotFound(new { message = exception.Message }) : Forbid();

    private bool TryGetUserId(out Guid userId) => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
}
