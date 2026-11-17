using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StudyHub.Api.Common;
using StudyHub.Api.Contracts;
using StudyHub.Api.Services;

namespace StudyHub.Api.Controllers;

// Writing and editing quizzes. Playing a quiz live happens over the hub in Live/QuizHub.cs.
[ApiController]
[Authorize]
[Route("api/quizzes")]
public class QuizzesController(IQuizService quizzes, IGameHistoryService history) : ControllerBase
{
    [HttpGet("mine")]
    public async Task<ActionResult<IReadOnlyList<QuizSummary>>> Mine(CancellationToken ct)
        => Ok(await quizzes.ListMineAsync(User.GetUserId(), ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<QuizResponse>> Get(Guid id, CancellationToken ct)
        => Ok(await quizzes.GetAsync(User.GetUserId(), id, ct));

    // How the quiz has been played. Only the student who wrote it can see this.
    [HttpGet("{id:guid}/stats")]
    public async Task<ActionResult<QuizStatsResponse>> Stats(Guid id, CancellationToken ct)
        => Ok(await history.QuizStatsAsync(User.GetUserId(), id, ct));

    [HttpPost]
    public async Task<ActionResult<QuizResponse>> Create(QuizRequest request, CancellationToken ct)
    {
        var created = await quizzes.CreateAsync(User.GetUserId(), request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<QuizResponse>> Update(Guid id, QuizRequest request, CancellationToken ct)
        => Ok(await quizzes.UpdateAsync(User.GetUserId(), id, request, ct));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await quizzes.DeleteAsync(User.GetUserId(), id, ct);
        return NoContent();
    }
}
