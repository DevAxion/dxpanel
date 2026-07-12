using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Services.Developer;

/// <summary>A newly generated key pair. The private key is returned once and never stored.</summary>
public record GeneratedKeyPair(string PublicKey, string PrivateKeyPem, string Fingerprint);

public interface ISshKeyService
{
    Task<List<SshKey>> GetKeysAsync(string userId);
    Task<SshKey> AddKeyAsync(string userId, string publicKey, string label);
    Task RemoveKeyAsync(string userId, int keyId);

    /// <summary>The authorized_keys file content for a username, as sshd would read it.</summary>
    Task<string> GetAuthorizedKeysAsync(string username);

    Task<SshAccess> GetAccessAsync(string userId);
    Task<ServiceResult> EnableSshAccessAsync(string userId);
    Task<ServiceResult> DisableSshAccessAsync(string userId);
    Task SetPasswordAuthAsync(string userId, bool allow);

    Task<List<SshAccessLog>> GetAccessLogAsync(string userId, int limit = 10);

    /// <summary>Generates an RSA key pair server-side and registers the public half.</summary>
    Task<GeneratedKeyPair> GenerateKeyPairAsync(string userId, string label, int bits = 4096);
}

public class SshKeyService : ISshKeyService
{
    private const string ServiceName = "ssh";

    private static readonly string[] SupportedTypes =
    {
        "ssh-rsa", "ssh-ed25519", "ssh-dss",
        "ecdsa-sha2-nistp256", "ecdsa-sha2-nistp384", "ecdsa-sha2-nistp521",
        "sk-ssh-ed25519@openssh.com", "sk-ecdsa-sha2-nistp256@openssh.com"
    };

    private readonly ApplicationDbContext _db;
    private readonly ICommandRunner _runner;
    private readonly PanelSettings _panel;

    public SshKeyService(ApplicationDbContext db, ICommandRunner runner, IOptionsMonitor<PanelSettings> panel)
    {
        _db = db;
        _runner = runner;
        _panel = panel.CurrentValue;
    }

    public Task<List<SshKey>> GetKeysAsync(string userId) =>
        _db.SshKeys.Where(k => k.UserId == userId).OrderByDescending(k => k.CreatedAt).ToListAsync();

    public async Task<SshKey> AddKeyAsync(string userId, string publicKey, string label)
    {
        var (ok, keyType, fingerprint, error) = ParsePublicKey(publicKey);
        if (!ok) throw new InvalidOperationException(error);

        if (await _db.SshKeys.AnyAsync(k => k.UserId == userId && k.Fingerprint == fingerprint))
            throw new InvalidOperationException("That key has already been added to your account.");

        if (string.IsNullOrWhiteSpace(label))
            throw new InvalidOperationException("Give the key a label so you can recognise it later.");

        var key = new SshKey
        {
            UserId = userId,
            Label = label.Trim(),
            PublicKey = Normalize(publicKey),
            Fingerprint = fingerprint,
            KeyType = keyType,
            CreatedAt = DateTime.UtcNow
        };

        _db.SshKeys.Add(key);
        await _db.SaveChangesAsync();

        await SyncAuthorizedKeysAsync(userId);
        return key;
    }

    public async Task RemoveKeyAsync(string userId, int keyId)
    {
        var key = await _db.SshKeys.FirstOrDefaultAsync(k => k.Id == keyId && k.UserId == userId);
        if (key == null) return;

        _db.SshKeys.Remove(key);
        await _db.SaveChangesAsync();

        await SyncAuthorizedKeysAsync(userId);
    }

    public async Task<string> GetAuthorizedKeysAsync(string username)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.UserName == username);
        if (user == null) return string.Empty;

        var access = await _db.SshAccesses.FirstOrDefaultAsync(a => a.UserId == user.Id);
        if (access is not { IsEnabled: true }) return string.Empty;

        var keys = await _db.SshKeys.Where(k => k.UserId == user.Id).OrderBy(k => k.CreatedAt).ToListAsync();
        var sb = new StringBuilder();
        sb.AppendLine($"# Managed by SRXPanel — do not edit by hand ({DateTime.UtcNow:u})");
        foreach (var key in keys)
            sb.AppendLine($"{key.PublicKey} {key.Label.Replace('\n', ' ')}");
        return sb.ToString();
    }

    public async Task<SshAccess> GetAccessAsync(string userId)
    {
        var access = await _db.SshAccesses.FirstOrDefaultAsync(a => a.UserId == userId);
        if (access != null) return access;

        access = new SshAccess { UserId = userId, IsEnabled = false, Port = 22, AllowPasswordAuth = false };
        _db.SshAccesses.Add(access);
        await _db.SaveChangesAsync();
        return access;
    }

    public async Task<ServiceResult> EnableSshAccessAsync(string userId)
    {
        var access = await GetAccessAsync(userId);
        var user = await _db.Users.FirstAsync(u => u.Id == userId);

        access.IsEnabled = true;
        await _db.SaveChangesAsync();

        var commands = new List<CommandResult>
        {
            await _runner.RunAsync($"usermod -s /bin/bash {user.UserName}", ServiceName),
            await _runner.RunAsync($"mkdir -p /home/{user.UserName}/.ssh && chmod 700 /home/{user.UserName}/.ssh", ServiceName)
        };
        commands.Add(await WriteAuthorizedKeysAsync(user.UserName!, userId));

        return ServiceResult.Ok($"SSH access enabled for {user.UserName}.", commands.ToArray());
    }

    public async Task<ServiceResult> DisableSshAccessAsync(string userId)
    {
        var access = await GetAccessAsync(userId);
        var user = await _db.Users.FirstAsync(u => u.Id == userId);

        access.IsEnabled = false;
        await _db.SaveChangesAsync();

        var commands = new[]
        {
            await _runner.RunAsync($"usermod -s /usr/sbin/nologin {user.UserName}", ServiceName),
            await _runner.DeleteFileAsync($"/home/{user.UserName}/.ssh/authorized_keys", ServiceName)
        };

        return ServiceResult.Ok($"SSH access disabled for {user.UserName}.", commands);
    }

    public async Task SetPasswordAuthAsync(string userId, bool allow)
    {
        var access = await GetAccessAsync(userId);
        access.AllowPasswordAuth = allow;
        await _db.SaveChangesAsync();
    }

    public Task<List<SshAccessLog>> GetAccessLogAsync(string userId, int limit = 10) =>
        _db.SshAccessLogs.Where(l => l.UserId == userId)
            .OrderByDescending(l => l.ConnectedAt).Take(limit).ToListAsync();

    private async Task SyncAuthorizedKeysAsync(string userId)
    {
        var access = await _db.SshAccesses.FirstOrDefaultAsync(a => a.UserId == userId);
        if (access is not { IsEnabled: true }) return;

        var user = await _db.Users.FirstAsync(u => u.Id == userId);
        await WriteAuthorizedKeysAsync(user.UserName!, userId);
    }

    private async Task<CommandResult> WriteAuthorizedKeysAsync(string username, string userId)
    {
        var content = await GetAuthorizedKeysAsync(username);
        return await _runner.WriteFileAsync($"/home/{username}/.ssh/authorized_keys", content, ServiceName);
    }

    // ---------------- Key parsing ----------------

    /// <summary>
    /// Validates an OpenSSH public key line and derives its SHA256 fingerprint.
    /// The base64 blob must re-declare the same algorithm as the leading token —
    /// that check is what stops a mismatched or corrupted key from being stored.
    /// </summary>
    internal static (bool ok, string keyType, string fingerprint, string error) ParsePublicKey(string? publicKey)
    {
        if (string.IsNullOrWhiteSpace(publicKey))
            return (false, "", "", "Paste a public key.");

        // Check this before tokenizing: a PEM's first token is "-----BEGIN", which would
        // otherwise be reported as an unsupported key type rather than the real mistake.
        if (publicKey.Contains("PRIVATE KEY", StringComparison.OrdinalIgnoreCase))
            return (false, "", "", "That is a PRIVATE key. Paste the matching .pub file instead — never share a private key.");

        var line = publicKey.Trim().Replace("\r", "").Replace("\n", " ");
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2)
            return (false, "", "", "A public key looks like: ssh-ed25519 AAAAC3Nza... user@host");

        var keyType = parts[0];
        if (!SupportedTypes.Contains(keyType, StringComparer.Ordinal))
            return (false, "", "", $"Unsupported key type '{keyType}'. Use ssh-ed25519, ssh-rsa or ecdsa-*.");

        byte[] blob;
        try
        {
            blob = Convert.FromBase64String(parts[1]);
        }
        catch (FormatException)
        {
            return (false, "", "", "The key body is not valid base64.");
        }

        // The blob starts with a length-prefixed copy of the algorithm name.
        if (!TryReadString(blob, 0, out var declared, out _) || declared != keyType)
            return (false, "", "", "The key body does not match its declared type — the key looks corrupted.");

        var hash = SHA256.HashData(blob);
        var fingerprint = "SHA256:" + Convert.ToBase64String(hash).TrimEnd('=');

        return (true, keyType, fingerprint, "");
    }

    private static bool TryReadString(byte[] blob, int offset, out string value, out int next)
    {
        value = string.Empty;
        next = offset;
        if (blob.Length < offset + 4) return false;

        var length = BinaryPrimitives.ReadInt32BigEndian(blob.AsSpan(offset, 4));
        if (length < 0 || blob.Length < offset + 4 + length) return false;

        value = Encoding.ASCII.GetString(blob, offset + 4, length);
        next = offset + 4 + length;
        return true;
    }

    /// <summary>Strips the trailing comment so the stored key is exactly "type base64".</summary>
    private static string Normalize(string publicKey)
    {
        var parts = publicKey.Trim().Replace("\r", "").Replace("\n", " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return $"{parts[0]} {parts[1]}";
    }

    // ---------------- Key generation ----------------

    public async Task<GeneratedKeyPair> GenerateKeyPairAsync(string userId, string label, int bits = 4096)
    {
        if (bits is not (2048 or 3072 or 4096))
            throw new InvalidOperationException("Key size must be 2048, 3072 or 4096 bits.");

        using var rsa = RSA.Create(bits);
        var parameters = rsa.ExportParameters(includePrivateParameters: false);

        var publicKey = "ssh-rsa " + Convert.ToBase64String(BuildRsaBlob(parameters));
        var privatePem = rsa.ExportRSAPrivateKeyPem();

        var user = await _db.Users.FirstAsync(u => u.Id == userId);
        var comment = $"{user.UserName}@{_panel.Hostname}";

        var key = await AddKeyAsync(userId, $"{publicKey} {comment}", label);
        return new GeneratedKeyPair($"{publicKey} {comment}", privatePem, key.Fingerprint);
    }

    /// <summary>Encodes an RSA public key in the OpenSSH wire format: string("ssh-rsa") + mpint(e) + mpint(n).</summary>
    private static byte[] BuildRsaBlob(RSAParameters p)
    {
        using var ms = new MemoryStream();
        WriteString(ms, Encoding.ASCII.GetBytes("ssh-rsa"));
        WriteMpint(ms, p.Exponent!);
        WriteMpint(ms, p.Modulus!);
        return ms.ToArray();

        static void WriteString(Stream s, byte[] data)
        {
            Span<byte> length = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
            s.Write(length);
            s.Write(data);
        }

        static void WriteMpint(Stream s, byte[] value)
        {
            // A leading zero keeps the value positive when the high bit is set.
            if (value.Length > 0 && (value[0] & 0x80) != 0)
            {
                var padded = new byte[value.Length + 1];
                Array.Copy(value, 0, padded, 1, value.Length);
                WriteString(s, padded);
            }
            else
            {
                WriteString(s, value);
            }
        }
    }
}
