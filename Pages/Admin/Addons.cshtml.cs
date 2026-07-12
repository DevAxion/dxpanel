using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;

namespace SRXPanel.Pages.Admin;

public class AddonsModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public AddonsModel(ApplicationDbContext db)
    {
        _db = db;
    }

    public List<Addon> Addons { get; private set; } = new();
    [BindProperty] public Addon Input { get; set; } = new();
    public bool Editing => Input.Id != 0;

    public async Task OnGetAsync(int? editId)
    {
        if (editId.HasValue)
        {
            var a = await _db.Addons.FindAsync(editId.Value);
            if (a != null) Input = a;
        }
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        Addons = (await _db.Addons.ToListAsync()).OrderBy(a => a.SortOrder).ToList();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (string.IsNullOrWhiteSpace(Input.Name))
        {
            ModelState.AddModelError("Input.Name", "Name is required.");
            await LoadAsync();
            return Page();
        }

        if (Input.Id == 0)
        {
            Input.CreatedAt = DateTime.UtcNow;
            _db.Addons.Add(Input);
        }
        else
        {
            var a = await _db.Addons.FindAsync(Input.Id);
            if (a == null) return RedirectToPage();
            a.Name = Input.Name;
            a.Description = Input.Description;
            a.Type = Input.Type;
            a.Value = Input.Value;
            a.Price = Input.Price;
            a.Currency = Input.Currency;
            a.BillingCycle = Input.BillingCycle;
            a.IsActive = Input.IsActive;
            a.SortOrder = Input.SortOrder;
        }
        await _db.SaveChangesAsync();
        TempData["Success"] = "Add-on saved.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleAsync(int id)
    {
        var a = await _db.Addons.FindAsync(id);
        if (a != null) { a.IsActive = !a.IsActive; await _db.SaveChangesAsync(); }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var a = await _db.Addons.FindAsync(id);
        if (a != null) { _db.Addons.Remove(a); await _db.SaveChangesAsync(); }
        return RedirectToPage();
    }
}
