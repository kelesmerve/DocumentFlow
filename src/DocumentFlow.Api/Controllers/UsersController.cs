using DocumentFlow.Application.Approvals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocumentFlow.Api.Controllers;

[ApiController, Authorize, Route("api/users")]
public sealed class UsersController(IApprovalService approvals) : ControllerBase
{
    [HttpGet("managers")]
    public async Task<IActionResult> Managers(CancellationToken cancellationToken) => Ok(await approvals.ListManagersAsync(cancellationToken));
}
