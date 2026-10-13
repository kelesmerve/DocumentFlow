using DocumentFlow.Application.Audit;
using DocumentFlow.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocumentFlow.Api.Controllers;

[ApiController, Authorize(Roles = "Admin"), Route("api/audit")]
public sealed class AuditController(IAuditQueryService audit) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<AuditPage>> List(
        [FromQuery] AuditAction? action,
        [FromQuery] Guid? userId,
        [FromQuery] string? documentNumber,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        Ok(await audit.ListAsync(action, userId, documentNumber, page, pageSize, cancellationToken));
}
