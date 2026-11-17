using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using StudyHub.Api.Common;
using StudyHub.Api.Contracts;
using StudyHub.Api.Services;

namespace StudyHub.Api.Controllers;

// Anyone can practise, logged in or not. Only logged-in students' runs are saved to history.
[ApiController]
[Route("api/practice")]
public class PracticeController(IPracticeService practice) : ControllerBase
{
    [HttpGet("quizzes")]
    public async Task<ActionResult<IReadOnlyList<PublicQuizSummary>>> Quizzes(
        [FromQuery] string? topic, [FromQuery] string? search, CancellationToken ct)
        => Ok(await practice.ListPublicAsync(topic, search, ct));

    [HttpGet("topics")]
    public async Task<ActionResult<IReadOnlyList<string>>> Topics(CancellationToken ct)
        => Ok(await practice.TopicsAsync(ct));

    [HttpPost("start")]
    [EnableRateLimiting(RateLimits.PracticeStart)]
    public async Task<ActionResult<PracticeStartResponse>> Start(PracticeStartRequest request, CancellationToken ct)
        => Ok(await practice.StartAsync(User.GetUserIdOrNull(), request, ct));

    [HttpPost("{id:guid}/answer")]
    public ActionResult<PracticeAnswerResponse> Answer(Guid id, PracticeAnswerRequest request)
        => Ok(practice.Answer(User.GetUserIdOrNull(), id, request));

    [HttpPost("{id:guid}/finish")]
    public async Task<ActionResult<PracticeFinishResponse>> Finish(Guid id, CancellationToken ct)
        => Ok(await practice.FinishAsync(User.GetUserIdOrNull(), id, ct));
}
