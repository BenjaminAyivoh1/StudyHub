using StudyHub.Api.Common;
using StudyHub.Api.Contracts;
using StudyHub.Api.Security;
using StudyHub.Api.Services;

namespace StudyHub.Tests;

public class AuthServiceTests
{
    private static AuthService CreateService(StudyHub.Api.Data.AppDbContext db)
        => new(db, new Pbkdf2PasswordHasher(), new FakeTokenService());

    [Fact]
    public async Task Register_creates_a_user_with_a_hashed_password_and_returns_a_token()
    {
        await using var db = TestDb.Create();
        var service = CreateService(db);

        var result = await service.RegisterAsync(new RegisterRequest("Ama@Example.com ", "Ama", "a-strong-password"));

        Assert.Equal("token-for-ama@example.com", result.Token);
        var saved = Assert.Single(db.Users);
        Assert.Equal("ama@example.com", saved.Email);
        Assert.NotEqual("a-strong-password", saved.PasswordHash);
    }

    [Fact]
    public async Task Register_rejects_a_duplicate_email_ignoring_case()
    {
        await using var db = TestDb.Create();
        var service = CreateService(db);
        await service.RegisterAsync(new RegisterRequest("ama@example.com", "Ama", "a-strong-password"));

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.RegisterAsync(new RegisterRequest("AMA@example.com", "Someone", "another-password")));
    }

    [Fact]
    public async Task Login_succeeds_with_the_right_credentials()
    {
        await using var db = TestDb.Create();
        var service = CreateService(db);
        await service.RegisterAsync(new RegisterRequest("ama@example.com", "Ama", "a-strong-password"));

        var result = await service.LoginAsync(new LoginRequest("ama@example.com", "a-strong-password"));

        Assert.Equal("Ama", result.DisplayName);
    }

    [Fact]
    public async Task Login_fails_with_a_wrong_password()
    {
        await using var db = TestDb.Create();
        var service = CreateService(db);
        await service.RegisterAsync(new RegisterRequest("ama@example.com", "Ama", "a-strong-password"));

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            service.LoginAsync(new LoginRequest("ama@example.com", "wrong-password")));
    }

    [Fact]
    public async Task Login_fails_for_an_unknown_email()
    {
        await using var db = TestDb.Create();
        var service = CreateService(db);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            service.LoginAsync(new LoginRequest("nobody@example.com", "whatever-password")));
    }
}
