using SRXPanel.Services.Integration;

namespace SRXPanel.Services.Store;

/// <summary>
/// Sends SMS to customers. Delegates to <see cref="ITwilioService"/>, which sends
/// via Twilio in production and logs the message in simulation.
/// </summary>
public interface ISmsSender
{
    Task SendAsync(string? phoneNumber, string message);
}

public class SmsSender : ISmsSender
{
    private readonly ITwilioService _twilio;

    public SmsSender(ITwilioService twilio)
    {
        _twilio = twilio;
    }

    public Task SendAsync(string? phoneNumber, string message) =>
        _twilio.SendSmsAsync(phoneNumber, message);
}
