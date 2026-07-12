using SRXPanel.Services.Interfaces;

namespace SRXPanel.Services.Integration;

/// <summary>
/// Manages vsftpd users. Uses system users with a nologin shell confined to
/// their home directory (typical vsftpd + PAM setup on Ubuntu 22.04).
/// </summary>
public class FtpService : IFtpService
{
    private const string ServiceName = "vsftpd";
    private readonly ICommandRunner _runner;

    public FtpService(ICommandRunner runner)
    {
        _runner = runner;
    }

    public async Task<ServiceResult> CreateFtpUserAsync(string username, string password, string homeDir)
    {
        var result = new ServiceResult { Message = $"FTP user '{username}' created." };

        // Create system user with nologin shell and the requested home directory.
        result.Commands.Add(await _runner.RunAsync(
            $"useradd -m -d {homeDir} -s /usr/sbin/nologin {username}", ServiceName));
        // Set password non-interactively.
        result.Commands.Add(await _runner.RunAsync(
            $"echo '{username}:{password}' | chpasswd", ServiceName));
        // Ensure home ownership.
        result.Commands.Add(await _runner.RunAsync(
            $"mkdir -p {homeDir} && chown -R {username}:{username} {homeDir}", ServiceName));
        // Add to vsftpd userlist (allowed users).
        result.Commands.Add(await _runner.RunAsync(
            $"grep -qx '{username}' /etc/vsftpd.userlist || echo '{username}' >> /etc/vsftpd.userlist", ServiceName));
        result.Commands.Add(await _runner.RunAsync("systemctl reload vsftpd", ServiceName));
        return result;
    }

    public async Task<ServiceResult> DeleteFtpUserAsync(string username)
    {
        var result = new ServiceResult { Message = $"FTP user '{username}' removed." };
        result.Commands.Add(await _runner.RunAsync($"userdel -r {username}", ServiceName));
        result.Commands.Add(await _runner.RunAsync(
            $"sed -i '/^{username}$/d' /etc/vsftpd.userlist", ServiceName));
        result.Commands.Add(await _runner.RunAsync("systemctl reload vsftpd", ServiceName));
        return result;
    }

    public async Task<ServiceResult> ChangePasswordAsync(string username, string newPassword)
    {
        var cmd = await _runner.RunAsync($"echo '{username}:{newPassword}' | chpasswd", ServiceName);
        return new ServiceResult { Success = cmd.Success, Message = $"Password changed for '{username}'.", Commands = { cmd } };
    }

    public async Task<ServiceResult> SetQuotaAsync(string username, long quotaMB)
    {
        // Uses the Linux quota subsystem (blocks are 1KB units).
        var blocks = quotaMB * 1024;
        var cmd = await _runner.RunAsync(
            $"setquota -u {username} {blocks} {blocks} 0 0 /", ServiceName);
        return new ServiceResult
        {
            Success = cmd.Success,
            Message = quotaMB == 0 ? $"Quota removed for '{username}'." : $"Quota set to {quotaMB} MB for '{username}'.",
            Commands = { cmd }
        };
    }
}
