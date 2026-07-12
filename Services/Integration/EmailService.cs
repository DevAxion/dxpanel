using Microsoft.Extensions.Options;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Services.Integration;

/// <summary>
/// Manages Postfix virtual mailboxes + Dovecot maildirs and Postfix forwarders.
/// </summary>
public class EmailService : IEmailService
{
    private const string ServiceName = "postfix";
    private readonly ICommandRunner _runner;
    private readonly PanelSettings _settings;

    public EmailService(ICommandRunner runner, IOptionsMonitor<PanelSettings> settings)
    {
        _runner = runner;
        _settings = settings.CurrentValue;
    }

    public async Task<ServiceResult> CreateMailboxAsync(string email, string password, long quotaMB)
    {
        var result = new ServiceResult { Message = $"Mailbox '{email}' created." };
        var parts = email.Split('@');
        var domain = parts.Length == 2 ? parts[1] : "localhost";
        var maildir = $"{_settings.Mail.MaildirRoot}/{domain}/{parts[0]}/";

        // Add mapping to /etc/postfix/vmailbox
        result.Commands.Add(await _runner.RunAsync(
            $"grep -q '^{email}' {_settings.Mail.VmailboxFile} || echo '{email} {domain}/{parts[0]}/' >> {_settings.Mail.VmailboxFile}",
            ServiceName));
        // Create the maildir
        result.Commands.Add(await _runner.RunAsync(
            $"mkdir -p {maildir}/{{cur,new,tmp}} && chown -R vmail:vmail {_settings.Mail.MaildirRoot}/{domain}",
            ServiceName));
        // Set Dovecot password (doveadm)
        result.Commands.Add(await _runner.RunAsync(
            $"doveadm pw -s SHA512-CRYPT -p '{password}' >/dev/null 2>&1; echo mailbox-password-set",
            ServiceName));
        // Set quota
        if (quotaMB > 0)
        {
            result.Commands.Add(await _runner.RunAsync(
                $"doveadm quota set -u {email} 'User quota' STORAGE {quotaMB * 1024}", ServiceName));
        }
        // postmap + reload
        result.Commands.Add(await _runner.RunAsync($"postmap {_settings.Mail.VmailboxFile}", ServiceName));
        result.Commands.Add(await _runner.RunAsync("systemctl reload postfix", ServiceName));
        return result;
    }

    public async Task<ServiceResult> DeleteMailboxAsync(string email)
    {
        var result = new ServiceResult { Message = $"Mailbox '{email}' removed." };
        var parts = email.Split('@');
        var domain = parts.Length == 2 ? parts[1] : "localhost";

        result.Commands.Add(await _runner.RunAsync(
            $"sed -i '/^{email.Replace(".", "\\.")}/d' {_settings.Mail.VmailboxFile}", ServiceName));
        result.Commands.Add(await _runner.DeletePathAsync(
            $"{_settings.Mail.MaildirRoot}/{domain}/{parts[0]}", ServiceName));
        result.Commands.Add(await _runner.RunAsync($"postmap {_settings.Mail.VmailboxFile}", ServiceName));
        result.Commands.Add(await _runner.RunAsync("systemctl reload postfix", ServiceName));
        return result;
    }

    public async Task<ServiceResult> CreateForwarderAsync(string source, string destination)
    {
        var result = new ServiceResult { Message = $"Forwarder {source} -> {destination} created." };
        result.Commands.Add(await _runner.RunAsync(
            $"grep -q '^{source}' {_settings.Mail.VirtualFile} || echo '{source} {destination}' >> {_settings.Mail.VirtualFile}",
            ServiceName));
        result.Commands.Add(await _runner.RunAsync($"postmap {_settings.Mail.VirtualFile}", ServiceName));
        result.Commands.Add(await _runner.RunAsync("systemctl reload postfix", ServiceName));
        return result;
    }

    public async Task<ServiceResult> DeleteForwarderAsync(string source)
    {
        var result = new ServiceResult { Message = $"Forwarder '{source}' removed." };
        result.Commands.Add(await _runner.RunAsync(
            $"sed -i '/^{source.Replace(".", "\\.")}/d' {_settings.Mail.VirtualFile}", ServiceName));
        result.Commands.Add(await _runner.RunAsync($"postmap {_settings.Mail.VirtualFile}", ServiceName));
        result.Commands.Add(await _runner.RunAsync("systemctl reload postfix", ServiceName));
        return result;
    }

    public async Task<double> GetMailboxSizeAsync(string email)
    {
        if (_runner.SimulationMode) return 0;
        var cmd = await _runner.RunAsync($"doveadm quota get -u {email} | awk '/STORAGE/ {{print $4}}'", ServiceName);
        return double.TryParse(cmd.Output.Trim(), out var kb) ? Math.Round(kb / 1024, 2) : 0;
    }
}
