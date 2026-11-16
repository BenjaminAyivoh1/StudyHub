using Microsoft.EntityFrameworkCore;
using StudyHub.Api.Data;
using StudyHub.Api.Domain;
using StudyHub.Api.Security;

namespace StudyHub.Tests;

internal static class TestDb
{
    /// <summary>A fresh, empty in-memory database for each test.</summary>
    public static AppDbContext Create()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    public static async Task<User> AddUserAsync(
        AppDbContext db, string email = "ama@example.com", string name = "Ama")
    {
        var user = new User { Email = email, DisplayName = name, PasswordHash = "not-a-real-hash" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }
}

internal sealed class FakeTokenService : ITokenService
{
    public string CreateToken(User user) => $"token-for-{user.Email}";
}
