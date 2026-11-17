using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StudyHub.Api.Common;
using StudyHub.Api.Contracts;
using StudyHub.Api.Services;

namespace StudyHub.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/history")]
public class HistoryController(IGameHistoryService history) : ControllerBase
{
    // The quiz games the logged-in student has played. Nobody else can see this list.
    [HttpGet("mine")]
    public async Task<ActionResult<IReadOnlyList<HistoryItem>>> Mine(CancellationToken ct)
        => Ok(await history.ListMineAsync(User.GetUserId(), ct));
}
