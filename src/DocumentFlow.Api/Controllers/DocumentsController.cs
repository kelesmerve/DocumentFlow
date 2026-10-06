using System.Security.Claims;
using DocumentFlow.Application.Documents;
using DocumentFlow.Application.Approvals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocumentFlow.Api.Controllers;

[ApiController, Authorize, Route("api/documents")]
public sealed class DocumentsController(IDocumentService documents, IApprovalService approvals) : ControllerBase
{
    private const long MaxRequestSize = 10 * 1024 * 1024 + 64 * 1024;

    [HttpPost]
    [RequestSizeLimit(MaxRequestSize)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestSize)]
    [ProducesResponseType(typeof(DocumentDetails), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromForm] string title, [FromForm] string? description, [FromForm] string category, [FromForm] IFormFile file, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId) || file is null) return BadRequest();
        await using var stream = file.OpenReadStream();
        try
        {
            var result = await documents.CreateAsync(userId, title, description, category,
                new DocumentUpload(file.FileName, file.ContentType, file.Length, stream), cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = result.Document.Id }, result);
        }
        catch (DocumentInputException exception) { return BadRequest(new { message = exception.Message }); }
    }

    [HttpGet]
    [ProducesResponseType(typeof(DocumentPage), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        return Ok(await documents.ListAsync(userId, User.IsInRole("Admin"), page, pageSize, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(DocumentDetails), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var result = await documents.GetAsync(id, userId, User.IsInRole("Admin"), cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("{id:guid}/versions/{versionNumber:int}/download")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(Guid id, int versionNumber, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var result = await documents.DownloadAsync(id, versionNumber, userId, User.IsInRole("Admin"), cancellationToken);
        return result is null ? NotFound() : File(result.Content, result.ContentType, result.FileName, enableRangeProcessing: true);
    }

    [HttpPost("{id:guid}/submit-for-approval")]
    public async Task<IActionResult> SubmitForApproval(Guid id, [FromBody] SubmitApprovalInput input, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        try { return Ok(await approvals.SubmitAsync(id, userId, input, cancellationToken)); }
        catch (ApprovalInputException exception) { return Conflict(new { message = exception.Message }); }
    }

    [HttpGet("{id:guid}/workflow")]
    public async Task<IActionResult> Workflow(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var result = await approvals.GetTimelineAsync(id, userId, User.IsInRole("Admin"), cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("{id:guid}/versions")]
    [RequestSizeLimit(MaxRequestSize)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestSize)]
    [ProducesResponseType(typeof(DocumentDetails), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddVersion(Guid id, [FromForm] IFormFile file, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId) || file is null) return BadRequest();
        await using var stream = file.OpenReadStream();
        try
        {
            var result = await documents.AddVersionAsync(id, userId,
                new DocumentUpload(file.FileName, file.ContentType, file.Length, stream), cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }
        catch (DocumentInputException exception) { return BadRequest(new { message = exception.Message }); }
    }

    private bool TryGetUserId(out Guid userId) => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
}
