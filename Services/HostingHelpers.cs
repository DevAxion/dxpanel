using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace SRXPanel.Services;

public static class HostingHelpers
{
    /// <summary>
    /// Builds a resource name prefixed with the (sanitized) username,
    /// e.g. john_mydb / john_ftp1. cPanel-style.
    /// </summary>
    public static string Prefixed(string userName, string suffix)
    {
        var prefix = SanitizeToken(userName);
        var s = SanitizeToken(suffix);
        return string.IsNullOrEmpty(s) ? prefix : $"{prefix}_{s}";
    }

    public static string UserPrefix(string userName) => SanitizeToken(userName);

    private static string SanitizeToken(string value)
    {
        value = (value ?? string.Empty).ToLowerInvariant();
        value = Regex.Replace(value, "[^a-z0-9]", string.Empty);
        return value.Length > 16 ? value[..16] : value;
    }

    public static string GeneratePassword(int length = 16)
    {
        const string chars = "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789!@#$%*";
        var bytes = RandomNumberGenerator.GetBytes(length);
        var sb = new System.Text.StringBuilder(length);
        foreach (var b in bytes)
        {
            sb.Append(chars[b % chars.Length]);
        }
        return sb.ToString();
    }
}
