using StudyHub.Api.Security;

namespace StudyHub.Tests;

public class PasswordHasherTests
{
    private readonly Pbkdf2PasswordHasher _hasher = new();

    [Fact]
    public void Verify_returns_true_for_the_right_password()
    {
        var hash = _hasher.Hash("correct horse battery staple");

        Assert.True(_hasher.Verify("correct horse battery staple", hash));
    }

    [Fact]
    public void Verify_returns_false_for_the_wrong_password()
    {
        var hash = _hasher.Hash("correct horse battery staple");

        Assert.False(_hasher.Verify("wrong password", hash));
    }

    [Fact]
    public void Hash_never_contains_the_plain_password_and_uses_a_new_salt_each_time()
    {
        var first = _hasher.Hash("my-password-123");
        var second = _hasher.Hash("my-password-123");

        Assert.DoesNotContain("my-password-123", first);
        Assert.NotEqual(first, second);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("1.2")]
    [InlineData("abc.def.ghi")]
    public void Verify_returns_false_for_malformed_stored_values(string stored)
    {
        Assert.False(_hasher.Verify("anything", stored));
    }
}
