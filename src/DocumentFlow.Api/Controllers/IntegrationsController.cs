using System.Security.Claims;
using DocumentFlow.Application.Integrations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocumentFlow.Api.Controllers;

[ApiController, Authorize(Roles = "Admin"), Route("api/integrations/webhooks")]
public sealed class IntegrationsController(IWebhookManagementService service) : ControllerBase
{
    public sealed record ActiveBody(bool IsActive);
    public sealed record RotateBody(string Secret);

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default) => Ok(await service.ListAsync(page, pageSize, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] WebhookInput input, CancellationToken ct)
    {
        if (!TryUser(out var userId)) return Unauthorized();
        try { return Ok(await service.CreateAsync(userId, input, ct)); }
        catch (WebhookInputException ex) { return BadRequest(new { message = ex.Message }); }
        catch (InvalidOperationException) { return StatusCode(503, new { message = "Webhook encryption is not configured." }); }
    }

    [HttpPatch("{id:guid}/active")]
    public async Task<IActionResult> Active(Guid id, [FromBody] ActiveBody body, CancellationToken ct)
    {
        if (!TryUser(out var userId)) return Unauthorized();
        var result = await service.SetActiveAsync(userId, id, body.IsActive, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (!TryUser(out var userId)) return Unauthorized();
        return await service.DeleteAsync(userId, id, ct) ? NoContent() : NotFound();
    }

    [HttpPost("{id:guid}/rotate-secret")]
    public async Task<IActionResult> Rotate(Guid id, [FromBody] RotateBody body, CancellationToken ct)
    {
        if (!TryUser(out var userId)) return Unauthorized();
        try { return await service.RotateSecretAsync(userId, id, body.Secret, ct) ? NoContent() : NotFound(); }
        catch (WebhookInputException ex) { return BadRequest(new { message = ex.Message }); }
        catch (InvalidOperationException) { return StatusCode(503, new { message = "Webhook encryption is not configured." }); }
    }

    [HttpGet("deliveries")]
    public async Task<IActionResult> Deliveries([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] DateTime? afterCreatedAtUtc = null, [FromQuery] Guid? afterId = null,
        CancellationToken ct = default)
    {
        if (afterCreatedAtUtc.HasValue != afterId.HasValue ||
            (afterCreatedAtUtc.HasValue && (afterCreatedAtUtc.Value.Kind != DateTimeKind.Utc || page != 1)))
            return BadRequest(new { message = "A UTC cursor requires afterCreatedAtUtc, afterId and page=1." });
        return Ok(await service.DeliveriesAsync(page, pageSize, afterCreatedAtUtc, afterId, ct));
    }

    private bool TryUser(out Guid id) => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out id);
}
