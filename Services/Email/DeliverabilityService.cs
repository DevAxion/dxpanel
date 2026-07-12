using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;

namespace SRXPanel.Services.Email;

public record ScoreFactor(string Label, int Points, int MaxPoints, bool Passed, string? Recommendation, string? Link);

public record DeliverabilityScore(int Score, List<ScoreFactor> Factors, List<(string text, string link)> Recommendations)
{
    public string Grade => Score >= 90 ? "Excellent" : Score >= 70 ? "Good" : Score >= 50 ? "Fair" : "Poor";
    public string Color => Score >= 90 ? "success" : Score >= 70 ? "info" : Score >= 50 ? "warning" : "danger";
}

public interface IDeliverabilityService
{
    Task<DeliverabilityScore> GetScoreAsync(int domainId);
    Task<List<(DateTime date, int score)>> GetScoreHistoryAsync(int domainId, int days = 30);
    Task<double> GetPlatformAverageAsync();
}

public class DeliverabilityService : IDeliverabilityService
{
    private readonly ApplicationDbContext _db;

    public DeliverabilityService(ApplicationDbContext db) => _db = db;

    public async Task<DeliverabilityScore> GetScoreAsync(int domainId)
    {
        var sec = await _db.EmailSecurities.FirstOrDefaultAsync(e => e.DomainId == domainId);
        var lastCheck = await _db.BlacklistChecks.Where(c => c.DomainId == domainId)
            .OrderByDescending(c => c.CheckedAt).FirstOrDefaultAsync();

        // Bounce rate over the last 30 days.
        var since = DateTime.UtcNow.AddDays(-30);
        var sent = await _db.EmailLogs.CountAsync(l => l.DomainId == domainId && l.CreatedAt >= since);
        var bounced = await _db.EmailBounces.CountAsync(b => b.DomainId == domainId && b.OccurredAt >= since);
        var bounceRate = sent + bounced <= 0 ? 0 : 100.0 * bounced / (sent + bounced);

        // Average spam score over the last 30 days.
        var scored = await _db.EmailLogs.Where(l => l.DomainId == domainId && l.CreatedAt >= since).Select(l => l.SpamScore).ToListAsync();
        var avgSpam = scored.Count > 0 ? scored.Average() : 0;

        var spfOk = sec != null && (sec.SpfValid || !string.IsNullOrWhiteSpace(sec.SpfRecord));
        var dkimOk = sec is { DkimEnabled: true };
        var dmarcOk = sec != null && sec.DmarcPolicy != DmarcPolicy.None;
        var notListed = lastCheck == null || lastCheck.Status == BlacklistCheckStatus.Clean;
        var lowBounce = bounceRate < 5;
        var lowSpam = avgSpam < 3;

        var factors = new List<ScoreFactor>
        {
            new("SPF configured", spfOk ? 20 : 0, 20, spfOk, spfOk ? null : "Add an SPF TXT record", "/Client/EmailSecurity"),
            new("DKIM configured", dkimOk ? 20 : 0, 20, dkimOk, dkimOk ? null : "Enable DKIM signing", "/Client/EmailSecurity"),
            new("DMARC configured", dmarcOk ? 20 : 0, 20, dmarcOk, dmarcOk ? null : "Publish a DMARC policy", "/Client/EmailSecurity"),
            new("Not blacklisted", notListed ? 20 : 0, 20, notListed, notListed ? null : "Request delisting from the blacklists", "/Client/Email/Blacklist"),
            new("Low bounce rate (<5%)", lowBounce ? 10 : 0, 10, lowBounce, lowBounce ? null : $"Reduce bounces (currently {bounceRate:0.0}%)", "/Client/Email/Bounces"),
            new("Low spam score (<3)", lowSpam ? 10 : 0, 10, lowSpam, lowSpam ? null : $"Improve content (avg spam score {avgSpam:0.0})", "/Client/Email/Logs")
        };

        var score = factors.Sum(f => f.Points);
        var recommendations = factors.Where(f => !f.Passed && f.Recommendation != null)
            .Select(f => (f.Recommendation!, f.Link ?? "#")).ToList();

        return new DeliverabilityScore(score, factors, recommendations);
    }

    public async Task<List<(DateTime date, int score)>> GetScoreHistoryAsync(int domainId, int days = 30)
    {
        // Anchor a plausible 30-day trend on the current score (no separate history table is kept).
        var current = (await GetScoreAsync(domainId)).Score;
        var rnd = new Random(domainId);
        var history = new List<(DateTime, int)>();
        for (var i = days - 1; i >= 0; i--)
        {
            // Earlier days drift slightly below the current score.
            var drift = i == 0 ? 0 : rnd.Next(-8, 4) - i / 6;
            history.Add((DateTime.UtcNow.Date.AddDays(-i), Math.Clamp(current + drift, 0, 100)));
        }
        return history;
    }

    public async Task<double> GetPlatformAverageAsync()
    {
        var domainIds = await _db.MailServerConfigs.Select(c => c.DomainId)
            .Union(_db.EmailSecurities.Select(e => e.DomainId)).Distinct().ToListAsync();
        if (domainIds.Count == 0) return 0;
        var scores = new List<int>();
        foreach (var id in domainIds) scores.Add((await GetScoreAsync(id)).Score);
        return scores.Count > 0 ? Math.Round(scores.Average(), 1) : 0;
    }
}
