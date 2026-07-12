using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;

namespace SRXPanel.Pages;

[AllowAnonymous]
public class VpsModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public VpsModel(ApplicationDbContext db)
    {
        _db = db;
    }

    public List<VpsPlan> Plans { get; set; } = new();

    public async Task OnGetAsync()
    {
        Plans = (await _db.VpsPlans.Where(v => v.IsActive).ToListAsync())
            .OrderBy(v => v.SortOrder).ThenBy(v => v.Price).ToList();
    }
}
