namespace SRXPanel.Services;

/// <summary>
/// Wraps BCrypt for hashing service passwords (DB/FTP/email/etc.)
/// separate from ASP.NET Identity's own user-password hashing.
/// </summary>
public interface ISecretHasher
{
    string Hash(string plainText);
    bool Verify(string plainText, string hash);
}

public class BCryptSecretHasher : ISecretHasher
{
    public string Hash(string plainText) => BCrypt.Net.BCrypt.HashPassword(plainText, workFactor: 11);

    public bool Verify(string plainText, string hash)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(plainText, hash);
        }
        catch
        {
            return false;
        }
    }
}
