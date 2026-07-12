using Microsoft.Extensions.Options;
using SRXPanel.Services.Interfaces;
using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace SRXPanel.Services.Integration;

/// <summary>
/// Sends transactional SMS via Twilio. In simulation mode (or when Twilio
/// credentials are not configured) the message is logged instead of sent, so the
/// panel works end-to-end on dev/Windows. In production with credentials present
/// it calls the real Twilio API.
/// </summary>
public interface ITwilioService
{
    /// <summary>Sends an SMS. Returns true if sent (or logged in simulation), false on error.</summary>
    Task<bool> SendSmsAsync(string? to, string message);
}

public class TwilioService : ITwilioService
{
    private const string ServiceName = "twilio";
    private readonly TwilioSettings _settings;
    private readonly ICommandRunner _log;
    private readonly ILogger<TwilioService> _logger;

    public TwilioService(IOptions<TwilioSettings> settings, ICommandRunner log, ILogger<TwilioService> logger)
    {
        _settings = settings.Value;
        _log = log;
        _logger = logger;
    }

    public async Task<bool> SendSmsAsync(string? to, string message)
    {
        if (string.IsNullOrWhiteSpace(to)) return false;

        // Simulation, or no Twilio credentials: log the would-be send and succeed.
        if (_log.SimulationMode || !_settings.IsConfigured)
        {
            _logger.LogInformation("Would send SMS to {To}: {Message}", to, message);
            await _log.LogExternalAsync(
                $"twilio.messages.create(to={to}, from={_settings.FromNumber})",
                $"[SIMULATED] Would send SMS to {to}: {message}", simulated: true, ServiceName);
            return true;
        }

        try
        {
            TwilioClient.Init(_settings.AccountSid, _settings.AuthToken);
            var result = await MessageResource.CreateAsync(
                to: new PhoneNumber(to),
                from: new PhoneNumber(_settings.FromNumber),
                body: message);

            await _log.LogExternalAsync(
                $"twilio.messages.create(to={to})",
                $"SMS sent (sid={result.Sid}, status={result.Status})", simulated: false, ServiceName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send SMS to {To}", to);
            await _log.LogExternalAsync(
                $"twilio.messages.create(to={to})",
                $"SMS failed: {ex.Message}", simulated: false, ServiceName, exitCode: 1);
            return false;
        }
    }
}
