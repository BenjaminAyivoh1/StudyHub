using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using StudyHub.Api.Common;
using StudyHub.Api.Contracts;
using StudyHub.Api.Services;

namespace StudyHub.Api.Controllers;

[ApiController]
[Route("api/materials")]
public class MaterialsController(IMaterialService materials) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<MaterialSummary>>> Search(
        [FromQuery] MaterialQuery query, CancellationToken ct)
        => Ok(await materials.SearchAsync(query, ct));

    // Anyone can read a note. If the reader is logged in, we also tell them
    // whether they have marked it as helpful.
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<MaterialResponse>> Get(Guid id, CancellationToken ct)
        => Ok(await materials.GetAsync(id, User.GetUserIdOrNull(), ct));

    [Authorize]
    [HttpPost]
    [EnableRateLimiting(RateLimits.Writes)]
    public async Task<ActionResult<MaterialResponse>> Create(MaterialRequest request, CancellationToken ct)
    {
        var created = await materials.CreateAsync(User.GetUserId(), request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [Authorize]
    [HttpPut("{id:guid}")]
    [EnableRateLimiting(RateLimits.Writes)]
    public async Task<ActionResult<MaterialResponse>> Update(Guid id, MaterialRequest request, CancellationToken ct)
        => Ok(await materials.UpdateAsync(User.GetUserId(), id, request, ct));

    [Authorize]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await materials.DeleteAsync(User.GetUserId(), id, ct);
        return NoContent();
    }

    [Authorize]
    [HttpPost("{id:guid}/reports")]
    [EnableRateLimiting(RateLimits.Writes)]
    public async Task<IActionResult> Report(Guid id, ReportRequest request, CancellationToken ct)
    {
        await materials.ReportAsync(User.GetUserId(), id, request, ct);
        return Accepted();
    }

    [Authorize]
    [HttpPut("{id:guid}/helpful")]
    [EnableRateLimiting(RateLimits.Writes)]
    public async Task<ActionResult<HelpfulResponse>> MarkHelpful(Guid id, CancellationToken ct)
        => Ok(await materials.MarkHelpfulAsync(User.GetUserId(), id, ct));

    [Authorize]
    [HttpDelete("{id:guid}/helpful")]
    [EnableRateLimiting(RateLimits.Writes)]
    public async Task<ActionResult<HelpfulResponse>> UnmarkHelpful(Guid id, CancellationToken ct)
        => Ok(await materials.UnmarkHelpfulAsync(User.GetUserId(), id, ct));
}
