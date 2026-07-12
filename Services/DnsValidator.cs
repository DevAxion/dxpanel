using System.Net;
using System.Text.RegularExpressions;
using SRXPanel.Models;

namespace SRXPanel.Services;

public static class DnsValidator
{
    private static readonly Regex HostnameRegex = new(
        @"^(?=.{1,253}$)(([A-Za-z0-9_](-?[A-Za-z0-9_])*)\.)*[A-Za-z0-9](-?[A-Za-z0-9])*\.?$",
        RegexOptions.Compiled);

    /// <summary>Validates a record and returns an error message, or null when valid.</summary>
    public static string? Validate(DnsRecordType type, string name, string value, int? priority)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Name is required (use '@' for the root).";
        if (string.IsNullOrWhiteSpace(value))
            return "Value is required.";

        value = value.Trim();

        switch (type)
        {
            case DnsRecordType.A:
                if (!IPAddress.TryParse(value, out var ip4) || ip4.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                    return "A record value must be a valid IPv4 address.";
                break;

            case DnsRecordType.AAAA:
                if (!IPAddress.TryParse(value, out var ip6) || ip6.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6)
                    return "AAAA record value must be a valid IPv6 address.";
                break;

            case DnsRecordType.CNAME:
            case DnsRecordType.NS:
                if (!HostnameRegex.IsMatch(value))
                    return $"{type} record value must be a valid hostname.";
                break;

            case DnsRecordType.MX:
                if (!HostnameRegex.IsMatch(value))
                    return "MX record value must be a valid mail server hostname.";
                if (priority is null or < 0)
                    return "MX record requires a priority (>= 0).";
                break;

            case DnsRecordType.SRV:
                // Expected: "weight port target"
                var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 3 || !int.TryParse(parts[0], out _) || !int.TryParse(parts[1], out _) || !HostnameRegex.IsMatch(parts[2]))
                    return "SRV value must be 'weight port target' (e.g. '5 5060 sip.example.com').";
                if (priority is null or < 0)
                    return "SRV record requires a priority (>= 0).";
                break;

            case DnsRecordType.TXT:
                if (value.Length > 1000)
                    return "TXT record value is too long.";
                break;
        }

        return null;
    }
}
