using System.Security.Cryptography;
using System.Text;

namespace DndMcpAICsharpFun.Features.Auth;

public static class PasswordHasher
{
    private const int SaltSize = 16;
    private const int Iterations = 100_000;
    private const int KeySize = 32;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(key)}";
    }

    public static bool Verify(string password, string hash)
    {
        var parts = hash.Split(':');
        if (parts.Length != 2) return false;
        byte[] salt, expectedKey;
        try
        {
            salt = Convert.FromBase64String(parts[0]);
            expectedKey = Convert.FromBase64String(parts[1]);
        }
        catch (FormatException)
        {
            // A stored hash that is not valid base64 is treated as a failed verification, never a throw.
            return false;
        }
        var actualKey = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return CryptographicOperations.FixedTimeEquals(actualKey, expectedKey);
    }
}
