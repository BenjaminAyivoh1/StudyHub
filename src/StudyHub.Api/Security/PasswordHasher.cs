using System.Security.Cryptography;

namespace StudyHub.Api.Security;

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string storedHash);
}

/// <summary>
/// Hashes passwords with PBKDF2-SHA256 and a random salt per password.
/// Stored format: iterations.salt.hash (both base64).
/// Never store the plain password, and never invent your own algorithm.
/// </summary>
public class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 210_000;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
    }

    public bool Verify(string password, string storedHash)
    {
        var parts = storedHash.Split('.');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations) || iterations <= 0)
        {
            return false;
        }

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[1]);
            expected = Convert.FromBase64String(parts[2]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);

        // Constant-time comparison so timing can't leak how much of the hash matched.
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
