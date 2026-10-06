using System.Security.Claims;
using DocumentFlow.Application.Approvals;
using DocumentFlow.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocumentFlow.Api.Controllers;

[ApiController, Authorize, Route("api/approvals")]
public sealed class ApprovalsController(IApprovalService approvals) : ControllerBase
{
    [HttpGet, Authorize(Roles = "Manager,Admin")]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? Ok(await approvals.ListAsync(userId, User.IsInRole("Admin"), page, pageSize, cancellationToken)) : Unauthorized();

    [HttpGet("{id:guid}"), Authorize(Roles = "Manager,Admin")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
        var result = await approvals.GetAsync(id, userId, User.IsInRole("Admin"), cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("{id:guid}/approve"), Authorize(Roles = "Manager")]
    public Task<IActionResult> Approve(Guid id, [FromBody] ApprovalInput input, CancellationToken cancellationToken) => Decide(id, ApprovalStatus.Approved, input.Comment, cancellationToken);
    [HttpPost("{id:guid}/reject"), Authorize(Roles = "Manager")]
    public Task<IActionResult> Reject(Guid id, [FromBody] ApprovalInput input, CancellationToken cancellationToken) => Decide(id, ApprovalStatus.Rejected, input.Comment, cancellationToken);
    [HttpPost("{id:guid}/request-revision"), Authorize(Roles = "Manager")]
    public Task<IActionResult> RequestRevision(Guid id, [FromBody] ApprovalInput input, CancellationToken cancellationToken) => Decide(id, ApprovalStatus.RevisionRequested, input.Comment, cancellationToken);

    private async Task<IActionResult> Decide(Guid id, ApprovalStatus status, string? comment, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
        try
        {
            var result = await approvals.DecideAsync(id, userId, status, comment, cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }
        catch (ApprovalInputException ex) { return Conflict(new { message = ex.Message }); }
    }
}
