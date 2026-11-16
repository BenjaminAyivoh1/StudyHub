using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StudyHub.Api.Contracts;
using StudyHub.Api.Security;
using StudyHub.Api.Services;

namespace StudyHub.Api.Controllers;

// Every route here needs a logged-in user whose email is in the Admin:Emails setting.
// Anyone else gets "403 Forbidden", even if they know the address.
[ApiController]
[Authorize(Policy = AdminOptions.Policy)]
[Route("api/admin")]
public class AdminController(IAdminService admin) : ControllerBase
{
    [HttpGet("reports")]
    public async Task<ActionResult<IReadOnlyList<AdminReportResponse>>> Reports(CancellationToken ct)
        => Ok(await admin.ListOpenReportsAsync(ct));

    [HttpPost("reports/{reportId:guid}/dismiss")]
    public async Task<IActionResult> DismissReport(Guid reportId, CancellationToken ct)
    {
        await admin.DismissReportAsync(reportId, ct);
        return NoContent();
    }

    [HttpDelete("materials/{materialId:guid}")]
    public async Task<IActionResult> RemoveMaterial(Guid materialId, CancellationToken ct)
    {
        await admin.RemoveMaterialAsync(materialId, ct);
        return NoContent();
    }

    [HttpDelete("comments/{commentId:guid}")]
    public async Task<IActionResult> RemoveComment(Guid commentId, CancellationToken ct)
    {
        await admin.RemoveCommentAsync(commentId, ct);
        return NoContent();
    }
}
