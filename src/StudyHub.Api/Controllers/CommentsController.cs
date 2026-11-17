using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using StudyHub.Api.Common;
using StudyHub.Api.Contracts;
using StudyHub.Api.Services;

namespace StudyHub.Api.Controllers;

[ApiController]
[Route("api/materials/{materialId:guid}/comments")]
public class CommentsController(ICommentService comments) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CommentResponse>>> List(Guid materialId, CancellationToken ct)
        => Ok(await comments.ListAsync(materialId, ct));

    [Authorize]
    [HttpPost]
    [EnableRateLimiting(RateLimits.Writes)]
    public async Task<ActionResult<CommentResponse>> Add(Guid materialId, CommentRequest request, CancellationToken ct)
    {
        var created = await comments.AddAsync(User.GetUserId(), materialId, request, ct);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    [Authorize]
    [HttpDelete("{commentId:guid}")]
    public async Task<IActionResult> Delete(Guid materialId, Guid commentId, CancellationToken ct)
    {
        await comments.DeleteAsync(User.GetUserId(), materialId, commentId, ct);
        return NoContent();
    }
}
