using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using StudyHub.Api.Common;
using StudyHub.Api.Contracts;
using StudyHub.Api.Security;
using StudyHub.Api.Services;

namespace StudyHub.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(IAuthService auth, IOptions<AdminOptions> adminOptions) : ControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken ct)
        => Ok(await auth.RegisterAsync(request, ct));

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken ct)
        => Ok(await auth.LoginAsync(request, ct));

    // The website asks this to find out whether to show the admin pages.
    [Authorize]
    [HttpGet("me")]
    public ActionResult<MeResponse> Me()
        => Ok(new MeResponse(User.GetUserId(), adminOptions.Value.IsAdmin(User.GetEmail())));
}
