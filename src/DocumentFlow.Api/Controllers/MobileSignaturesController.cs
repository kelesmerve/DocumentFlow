using System.Security.Claims;
using DocumentFlow.Application.Signatures;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DocumentFlow.Api.Controllers;

[ApiController, Authorize(Roles = "Manager"), Route("api/mobile-signature-requests")]
public sealed class MobileSignaturesController(IMobileSignatureService requests) : ControllerBase
{
    public sealed record ApproveBody(string? Password);

    [HttpPost("/api/documents/{documentId:guid}/versions/{versionId:guid}/mobile-signature-requests")]
    public Task<IActionResult> Create(Guid documentId, Guid versionId, CancellationToken cancellationToken) =>
        RunAsync(userId => requests.CreateAsync(documentId, versionId, userId, cancellationToken), cancellationToken);

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        if (!TryUser(out var userId)) return Unauthorized();
        try { return Ok(await requests.ListAsync(userId, cancellationToken)); }
        catch (MobileSignatureAccessException) { return Forbid(); }
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        if (!TryUser(out var userId)) return Unauthorized();
        try
        {
            var result = await requests.GetAsync(id, userId, cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }
        catch (MobileSignatureAccessException) { return NotFound(); }
    }

    [HttpPost("{id:guid}/approve")]
    public Task<IActionResult> Approve(Guid id, [FromBody] ApproveBody body, CancellationToken cancellationToken) =>
        RunAsync(userId => requests.ApproveAsync(id, userId, body.Password ?? string.Empty, cancellationToken), cancellationToken);

    [HttpPost("{id:guid}/reject")]
    public Task<IActionResult> Reject(Guid id, CancellationToken cancellationToken) =>
        RunAsync(userId => requests.RejectAsync(id, userId, cancellationToken), cancellationToken);

    private async Task<IActionResult> RunAsync(Func<Guid, Task<MobileSignatureSummary>> action, CancellationToken cancellationToken)
    {
        if (!TryUser(out var userId)) return Unauthorized();
        try { return Ok(await action(userId)); }
        catch (MobileSignatureAccessException) { return NotFound(); }
        catch (MobileSignaturePasswordException) { return Unauthorized(new { message = "Parola doğrulanamadı." }); }
        catch (MobileSignatureStateException exception) { return Conflict(new { message = exception.Message }); }
        catch (SignatureStateException exception) { return Conflict(new { message = exception.Message }); }
        catch (SignatureIntegrityException exception) { return Conflict(new { message = exception.Message }); }
        catch (SignatureAccessException) { return NotFound(); }
        catch (SignatureProviderUnavailableException exception) { return StatusCode(503, new { message = exception.Message }); }
        catch (DbUpdateException exception) when (exception.GetBaseException() is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { return Conflict(new { message = "Bu versiyon zaten imzalanmış." }); }
    }

    private bool TryUser(out Guid id) => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out id);
}
