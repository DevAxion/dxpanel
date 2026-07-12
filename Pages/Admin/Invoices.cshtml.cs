using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services.Billing;

namespace SRXPanel.Pages.Admin;

public class InvoicesModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public InvoicesModel(ApplicationDbContext db)
    {
        _db = db;
    }

    public List<Invoice> Invoices { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? StatusFilter { get; set; }
    [BindProperty(SupportsGet = true)]
    public DateTime? FromDate { get; set; }
    [BindProperty(SupportsGet = true)]
    public DateTime? ToDate { get; set; }

    public decimal TotalPaid { get; set; }

    public static string Money(decimal amount, string currency) => BillingService.FormatMoney(amount, currency);

    private IQueryable<Invoice> BuildQuery()
    {
        var query = _db.Invoices.Include(i => i.User).AsQueryable();
        if (Enum.TryParse<InvoiceStatus>(StatusFilter, out var status))
            query = query.Where(i => i.Status == status);
        if (FromDate.HasValue)
            query = query.Where(i => i.CreatedAt >= FromDate.Value);
        if (ToDate.HasValue)
            query = query.Where(i => i.CreatedAt <= ToDate.Value.AddDays(1));
        return query;
    }

    public async Task OnGetAsync()
    {
        Invoices = await BuildQuery().OrderByDescending(i => i.CreatedAt).ToListAsync();
        TotalPaid = Invoices.Where(i => i.Status == InvoiceStatus.Paid).Sum(i => i.Amount);
    }

    public async Task<IActionResult> OnGetExportAsync()
    {
        var invoices = await BuildQuery().OrderByDescending(i => i.CreatedAt).ToListAsync();
        var sb = new StringBuilder();
        sb.AppendLine("Number,Customer,Email,Amount,Currency,Status,Created,PaidAt");
        foreach (var i in invoices)
        {
            sb.AppendLine($"{i.Number},{i.User?.UserName},{i.User?.Email},{i.Amount},{i.Currency},{i.Status},{i.CreatedAt:yyyy-MM-dd},{(i.PaidAt.HasValue ? i.PaidAt.Value.ToString("yyyy-MM-dd") : "")}");
        }
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", $"invoices-{DateTime.UtcNow:yyyyMMdd}.csv");
    }
}
