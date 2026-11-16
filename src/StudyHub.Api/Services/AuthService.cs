using Microsoft.EntityFrameworkCore;
using StudyHub.Api.Common;
using StudyHub.Api.Contracts;
using StudyHub.Api.Data;
using StudyHub.Api.Domain;
using StudyHub.Api.Security;

namespace StudyHub.Api.Services;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default);
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);
}

public class AuthService(AppDbContext db, IPasswordHasher hasher, ITokenService tokens) : IAuthService
{
    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        var email = NormalizeEmail(request.Email);

        if (await db.Users.AnyAsync(u => u.Email == email, ct))
        {
            throw new ConflictException("An account with this email already exists.");
        }

        var user = new User
        {
            Email = email,
            DisplayName = request.DisplayName.Trim(),
            PasswordHash = hasher.Hash(request.Password)
        };

        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        return new AuthResponse(tokens.CreateToken(user), user.Id, user.DisplayName);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var email = NormalizeEmail(request.Email);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

        // Same message for "no such user" and "wrong password" so we don't reveal which emails exist.
        if (user is null || !hasher.Verify(request.Password, user.PasswordHash))
        {
            throw new UnauthorizedException("Invalid email or password.");
        }

        return new AuthResponse(tokens.CreateToken(user), user.Id, user.DisplayName);
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
