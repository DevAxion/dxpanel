using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Options;

namespace SRXPanel.Services.Developer;

public enum DnsRecordKind
{
    A = 1,
    NS = 2,
    CNAME = 5,
    SOA = 6,
    MX = 15,
    TXT = 16,
    AAAA = 28
}

public record DnsAnswer(DnsRecordKind Type, string Value, int Ttl, int? Priority = null);

/// <summary>The result of querying one resolver. <see cref="Error"/> is set when the query failed.</summary>
public record DnsResolverResult(string ResolverName, string ResolverIp, string? Location,
    List<DnsAnswer> Answers, long ElapsedMs, string? Error)
{
    public bool Ok => Error == null;
}

public record DnsLookupResult(string Domain, DnsRecordKind Type, List<DnsResolverResult> Results)
{
    /// <summary>Distinct answer sets across resolvers — more than one means propagation is incomplete.</summary>
    public bool Propagated =>
        Results.Where(r => r.Ok).Select(r => string.Join(",", r.Answers.Select(a => a.Value).OrderBy(v => v)))
            .Distinct().Count() <= 1;

    public int Responding => Results.Count(r => r.Ok);
}

public record DnsResolver(string Name, string Ip, string? Location);

public interface IDnsLookupService
{
    IReadOnlyList<DnsResolver> PrimaryResolvers { get; }
    IReadOnlyList<DnsResolver> PropagationResolvers { get; }

    Task<DnsLookupResult> LookupAsync(string domain, DnsRecordKind type, IEnumerable<DnsResolver>? resolvers = null,
        CancellationToken ct = default);

    /// <summary>Runs the lookup against ten geographically spread resolvers.</summary>
    Task<DnsLookupResult> CheckPropagationAsync(string domain, DnsRecordKind type, CancellationToken ct = default);

    /// <summary>The value this panel expects to see for the record, when it can say.</summary>
    string? ExpectedValue(DnsRecordKind type);
}

/// <summary>
/// A minimal DNS-over-UDP client. It speaks the wire protocol directly rather than
/// shelling out to dig, so lookups work identically on Windows and Linux and never
/// touch a shell. Resolvers that do not answer within the timeout are reported as
/// timed out rather than failing the whole lookup.
/// </summary>
public class DnsLookupService : IDnsLookupService
{
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(3);

    private readonly PanelSettings _panel;
    private readonly ILogger<DnsLookupService> _logger;

    public DnsLookupService(IOptionsMonitor<PanelSettings> panel, ILogger<DnsLookupService> logger)
    {
        _panel = panel.CurrentValue;
        _logger = logger;
    }

    public IReadOnlyList<DnsResolver> PrimaryResolvers => new[]
    {
        new DnsResolver("Google", "8.8.8.8", "Global anycast"),
        new DnsResolver("Cloudflare", "1.1.1.1", "Global anycast"),
        new DnsResolver("Local nameserver", LocalNameserver, "This server")
    };

    private string LocalNameserver =>
        string.IsNullOrWhiteSpace(_panel.Bind.ServerIp) ? "9.9.9.9" : _panel.Bind.ServerIp;

    public IReadOnlyList<DnsResolver> PropagationResolvers => new[]
    {
        new DnsResolver("Google", "8.8.8.8", "United States"),
        new DnsResolver("Google Secondary", "8.8.4.4", "United States"),
        new DnsResolver("Cloudflare", "1.1.1.1", "Global"),
        new DnsResolver("Cloudflare Secondary", "1.0.0.1", "Global"),
        new DnsResolver("Quad9", "9.9.9.9", "Switzerland"),
        new DnsResolver("OpenDNS", "208.67.222.222", "United States"),
        new DnsResolver("OpenDNS 2", "208.67.220.220", "United States"),
        new DnsResolver("Level3", "4.2.2.2", "United States"),
        new DnsResolver("DNS.WATCH", "84.200.69.80", "Germany"),
        new DnsResolver("Yandex", "77.88.8.8", "Russia")
    };

    public string? ExpectedValue(DnsRecordKind type) => type switch
    {
        DnsRecordKind.A => _panel.Bind.ServerIp,
        DnsRecordKind.NS => _panel.Bind.DefaultNs?.TrimEnd('.'),
        _ => null
    };

    public Task<DnsLookupResult> CheckPropagationAsync(string domain, DnsRecordKind type, CancellationToken ct = default) =>
        LookupAsync(domain, type, PropagationResolvers, ct);

    public async Task<DnsLookupResult> LookupAsync(string domain, DnsRecordKind type,
        IEnumerable<DnsResolver>? resolvers = null, CancellationToken ct = default)
    {
        var name = NormalizeDomain(domain);
        var targets = (resolvers ?? PrimaryResolvers).ToList();

        // Query every resolver in parallel; a slow one must not hold up the rest.
        var tasks = targets.Select(r => QueryResolverAsync(name, type, r, ct));
        var results = await Task.WhenAll(tasks);

        return new DnsLookupResult(name, type, results.ToList());
    }

    private async Task<DnsResolverResult> QueryResolverAsync(string domain, DnsRecordKind type,
        DnsResolver resolver, CancellationToken ct)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            if (!IPAddress.TryParse(resolver.Ip, out var address))
                return new DnsResolverResult(resolver.Name, resolver.Ip, resolver.Location, new(), 0, "Invalid resolver address");

            var answers = await QueryAsync(domain, type, address, ct);
            started.Stop();
            return new DnsResolverResult(resolver.Name, resolver.Ip, resolver.Location, answers, started.ElapsedMilliseconds, null);
        }
        catch (OperationCanceledException)
        {
            started.Stop();
            return new DnsResolverResult(resolver.Name, resolver.Ip, resolver.Location, new(), started.ElapsedMilliseconds, "Timed out");
        }
        catch (SocketException ex)
        {
            started.Stop();
            _logger.LogDebug(ex, "DNS query to {Resolver} failed", resolver.Ip);
            return new DnsResolverResult(resolver.Name, resolver.Ip, resolver.Location, new(), started.ElapsedMilliseconds, "Unreachable");
        }
        catch (Exception ex)
        {
            started.Stop();
            _logger.LogDebug(ex, "DNS query to {Resolver} failed", resolver.Ip);
            return new DnsResolverResult(resolver.Name, resolver.Ip, resolver.Location, new(), started.ElapsedMilliseconds, ex.Message);
        }
    }

    // ---------------- Wire protocol ----------------

    private static async Task<List<DnsAnswer>> QueryAsync(string domain, DnsRecordKind type, IPAddress resolver, CancellationToken ct)
    {
        using var udp = new UdpClient(resolver.AddressFamily);
        udp.Connect(resolver, 53);

        var transactionId = (ushort)Random.Shared.Next(1, ushort.MaxValue);
        var request = BuildQuery(transactionId, domain, type);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(QueryTimeout);

        await udp.SendAsync(request, timeout.Token);
        var response = await udp.ReceiveAsync(timeout.Token);

        return ParseResponse(response.Buffer, transactionId);
    }

    private static byte[] BuildQuery(ushort transactionId, string domain, DnsRecordKind type)
    {
        using var ms = new MemoryStream();

        Span<byte> header = stackalloc byte[12];
        BinaryPrimitives.WriteUInt16BigEndian(header[..2], transactionId);
        BinaryPrimitives.WriteUInt16BigEndian(header.Slice(2, 2), 0x0100); // standard query, recursion desired
        BinaryPrimitives.WriteUInt16BigEndian(header.Slice(4, 2), 1);      // one question
        header[6..].Clear();
        ms.Write(header);

        foreach (var label in domain.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            if (bytes.Length > 63) throw new InvalidOperationException("DNS label too long.");
            ms.WriteByte((byte)bytes.Length);
            ms.Write(bytes);
        }
        ms.WriteByte(0); // root label

        Span<byte> tail = stackalloc byte[4];
        BinaryPrimitives.WriteUInt16BigEndian(tail[..2], (ushort)type);
        BinaryPrimitives.WriteUInt16BigEndian(tail.Slice(2, 2), 1); // class IN
        ms.Write(tail);

        return ms.ToArray();
    }

    private static List<DnsAnswer> ParseResponse(byte[] buffer, ushort expectedId)
    {
        if (buffer.Length < 12) throw new InvalidOperationException("Truncated DNS response.");

        var id = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(0, 2));
        if (id != expectedId) throw new InvalidOperationException("DNS transaction id mismatch.");

        var flags = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(2, 2));
        var responseCode = flags & 0x000F;
        if (responseCode == 3) return new List<DnsAnswer>(); // NXDOMAIN — no records, not an error
        if (responseCode != 0) throw new InvalidOperationException($"DNS server returned code {responseCode}.");

        var questionCount = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(4, 2));
        var answerCount = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(6, 2));

        var offset = 12;
        for (var i = 0; i < questionCount; i++)
        {
            SkipName(buffer, ref offset);
            offset += 4; // qtype + qclass
        }

        var answers = new List<DnsAnswer>();
        for (var i = 0; i < answerCount && offset < buffer.Length; i++)
        {
            SkipName(buffer, ref offset);
            if (offset + 10 > buffer.Length) break;

            var type = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(offset, 2));
            var ttl = BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(offset + 4, 4));
            var dataLength = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(offset + 8, 2));
            offset += 10;

            if (offset + dataLength > buffer.Length) break;
            var dataStart = offset;

            switch ((DnsRecordKind)type)
            {
                case DnsRecordKind.A when dataLength == 4:
                    answers.Add(new DnsAnswer(DnsRecordKind.A, new IPAddress(buffer[dataStart..(dataStart + 4)]).ToString(), ttl));
                    break;

                case DnsRecordKind.AAAA when dataLength == 16:
                    answers.Add(new DnsAnswer(DnsRecordKind.AAAA, new IPAddress(buffer[dataStart..(dataStart + 16)]).ToString(), ttl));
                    break;

                case DnsRecordKind.CNAME:
                {
                    var p = dataStart;
                    answers.Add(new DnsAnswer(DnsRecordKind.CNAME, ReadName(buffer, ref p), ttl));
                    break;
                }

                case DnsRecordKind.NS:
                {
                    var p = dataStart;
                    answers.Add(new DnsAnswer(DnsRecordKind.NS, ReadName(buffer, ref p), ttl));
                    break;
                }

                case DnsRecordKind.MX:
                {
                    var priority = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(dataStart, 2));
                    var p = dataStart + 2;
                    answers.Add(new DnsAnswer(DnsRecordKind.MX, ReadName(buffer, ref p), ttl, priority));
                    break;
                }

                case DnsRecordKind.TXT:
                {
                    // TXT rdata is one or more length-prefixed character strings.
                    var text = new StringBuilder();
                    var p = dataStart;
                    while (p < dataStart + dataLength)
                    {
                        var length = buffer[p++];
                        if (p + length > buffer.Length) break;
                        text.Append(Encoding.UTF8.GetString(buffer, p, length));
                        p += length;
                    }
                    answers.Add(new DnsAnswer(DnsRecordKind.TXT, text.ToString(), ttl));
                    break;
                }

                case DnsRecordKind.SOA:
                {
                    var p = dataStart;
                    var primary = ReadName(buffer, ref p);
                    var mailbox = ReadName(buffer, ref p);
                    var serial = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(p, 4));
                    var refresh = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(p + 4, 4));
                    var retry = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(p + 8, 4));
                    var expire = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(p + 12, 4));
                    var minimum = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(p + 16, 4));
                    answers.Add(new DnsAnswer(DnsRecordKind.SOA,
                        $"{primary} {mailbox} {serial} {refresh} {retry} {expire} {minimum}", ttl));
                    break;
                }
            }

            offset = dataStart + dataLength;
        }

        return answers;
    }

    /// <summary>Reads a (possibly compressed) domain name and advances the offset past it.</summary>
    private static string ReadName(byte[] buffer, ref int offset)
    {
        var labels = new List<string>();
        var jumped = false;
        var originalOffset = offset;
        var safety = 0;

        while (offset < buffer.Length && safety++ < 128)
        {
            var length = buffer[offset];

            if (length == 0)
            {
                offset++;
                break;
            }

            // Two high bits set marks a pointer to an earlier name.
            if ((length & 0xC0) == 0xC0)
            {
                if (offset + 1 >= buffer.Length) break;
                var pointer = ((length & 0x3F) << 8) | buffer[offset + 1];
                if (!jumped) originalOffset = offset + 2;
                offset = pointer;
                jumped = true;
                continue;
            }

            offset++;
            if (offset + length > buffer.Length) break;
            labels.Add(Encoding.ASCII.GetString(buffer, offset, length));
            offset += length;
        }

        if (jumped) offset = originalOffset;
        return string.Join('.', labels);
    }

    private static void SkipName(byte[] buffer, ref int offset)
    {
        while (offset < buffer.Length)
        {
            var length = buffer[offset];
            if (length == 0) { offset++; return; }
            if ((length & 0xC0) == 0xC0) { offset += 2; return; }
            offset += length + 1;
        }
    }

    internal static string NormalizeDomain(string domain)
    {
        var name = (domain ?? "").Trim().Trim('.').ToLowerInvariant();
        if (name.Length == 0) throw new InvalidOperationException("Enter a domain name.");

        // Accept a pasted URL and use just its host.
        if (name.Contains("://") && Uri.TryCreate(name, UriKind.Absolute, out var uri)) name = uri.Host;
        if (name.Contains('/')) name = name.Split('/')[0];

        if (name.Length > 253 || !name.Contains('.'))
            throw new InvalidOperationException("Enter a fully qualified domain name, e.g. example.com.");

        foreach (var label in name.Split('.'))
        {
            if (label.Length is 0 or > 63)
                throw new InvalidOperationException("Each label in the domain must be 1-63 characters.");
            if (!label.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'))
                throw new InvalidOperationException($"'{label}' contains characters that are not valid in a hostname.");
        }

        return name;
    }
}
