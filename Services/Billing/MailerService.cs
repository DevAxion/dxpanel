using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;
using SRXPanel.Services;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Services.Billing;

/// <summary>
/// Renders HTML email templates (EmailTemplates/*.html) with {{TOKEN}} substitution
/// and sends transactional email via SMTP. In simulation mode the rendered email is
/// logged to the CommandLog instead of being sent.
/// This is separate from the Postfix mailbox-management IEmailService (Phase 3).
/// </summary>
public interface IMailerService
{
    Task SendTemplateAsync(string toEmail, string subject, string templateName, Dictionary<string, string> tokens);
    string RenderTemplate(string templateName, Dictionary<string, string> tokens);
}

public class MailerService : IMailerService
{
    private readonly ICommandRunner _log;
    private readonly PanelSettings _panel;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<MailerService> _logger;

    public MailerService(ICommandRunner log, IOptionsMonitor<PanelSettings> panel,
        IWebHostEnvironment env, ILogger<MailerService> logger)
    {
        _log = log;
        _panel = panel.CurrentValue;
        _env = env;
        _logger = logger;
    }

    public string RenderTemplate(string templateName, Dictionary<string, string> tokens)
    {
        var dir = Path.Combine(_env.ContentRootPath, "EmailTemplates");
        var bodyPath = Path.Combine(dir, $"{templateName}.html");
        var basePath = Path.Combine(dir, "_base.html");

        var body = File.Exists(bodyPath) ? File.ReadAllText(bodyPath) : "<p>{{BODY}}</p>";
        var shell = File.Exists(basePath) ? File.ReadAllText(basePath) : "{{BODY}}";

        var allTokens = new Dictionary<string, string>(tokens)
        {
            ["HOSTNAME"] = _panel.Hostname
        };

        foreach (var (k, v) in allTokens)
        {
            body = body.Replace("{{" + k + "}}", v ?? string.Empty);
        }

        var html = shell.Replace("{{BODY}}", body);
        foreach (var (k, v) in allTokens)
        {
            html = html.Replace("{{" + k + "}}", v ?? string.Empty);
        }
        return html;
    }

    public async Task SendTemplateAsync(string toEmail, string subject, string templateName, Dictionary<string, string> tokens)
    {
        var html = RenderTemplate(templateName, tokens);
        var simulated = _log.SimulationMode || string.IsNullOrWhiteSpace(_panel.Smtp.Host) || _panel.Smtp.Host == "localhost";

        if (simulated)
        {
            await _log.LogExternalAsync(
                $"email.send(to={toEmail}, template={templateName}, subject=\"{subject}\")",
                $"Rendered '{templateName}' ({html.Length} bytes) — SMTP not sent in simulation.",
                true, "email");
            return;
        }

        try
        {
            using var message = new MailMessage
            {
                From = new MailAddress(_panel.Smtp.From, "SRXPanel"),
                Subject = subject,
                Body = html,
                IsBodyHtml = true
            };
            message.To.Add(toEmail);

            using var client = new SmtpClient(_panel.Smtp.Host, _panel.Smtp.Port)
            {
                EnableSsl = true,
                Credentials = new NetworkCredential(_panel.Smtp.User, _panel.Smtp.Password)
            };
            await client.SendMailAsync(message);
            await _log.LogExternalAsync($"email.send(to={toEmail}, template={templateName})", "Sent via SMTP.", false, "email");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {To}", toEmail);
            await _log.LogExternalAsync($"email.send(to={toEmail}, template={templateName})", $"FAILED: {ex.Message}", false, "email", 1);
        }
    }
}
