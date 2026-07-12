using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;
using SRXPanel.Services;

namespace SRXPanel.Pages.Reseller;

public class BrandingModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IWebHostEnvironment _env;
    private readonly IAuditLogService _audit;

    public BrandingModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager,
        IWebHostEnvironment env, IAuditLogService audit)
    {
        _db = db;
        _userManager = userManager;
        _env = env;
        _audit = audit;
    }

    public ResellerBranding Branding { get; set; } = new();

    [BindProperty] public InputModel Input { get; set; } = new();
    [BindProperty] public IFormFile? Logo { get; set; }
    [BindProperty] public IFormFile? Favicon { get; set; }
    [BindProperty] public IFormFile? Background { get; set; }

    private static readonly string[] AllowedImages = { ".png", ".jpg", ".jpeg", ".svg" };
    private const long MaxUpload = 2L * 1024 * 1024;

    public class InputModel
    {
        [StringLength(100)] public string PanelTitle { get; set; } = "Hosting Panel";
        [StringLength(20)] public string PrimaryColor { get; set; } = "#2563eb";
        [StringLength(20)] public string SecondaryColor { get; set; } = "#151a23";
        [StringLength(20)] public string AccentColor { get; set; } = "#3b82f6";
        [StringLength(300)] public string? LoginBackground { get; set; }
        [StringLength(300)] public string? FooterText { get; set; }
        [StringLength(200)] public string? CustomDomain { get; set; }
        [StringLength(150)] public string? EmailSenderName { get; set; }
        [StringLength(200)] public string? EmailSenderAddress { get; set; }
    }

    private async Task<ResellerBranding> LoadOrCreateAsync(string resellerId)
    {
        var branding = await _db.ResellerBrandings.FirstOrDefaultAsync(b => b.ResellerId == resellerId);
        if (branding == null)
        {
            branding = new ResellerBranding { ResellerId = resellerId };
            _db.ResellerBrandings.Add(branding);
            await _db.SaveChangesAsync();
        }
        return branding;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        Branding = await LoadOrCreateAsync(user.Id);
        Input = new InputModel
        {
            PanelTitle = Branding.PanelTitle,
            PrimaryColor = Branding.PrimaryColor,
            SecondaryColor = Branding.SecondaryColor,
            AccentColor = Branding.AccentColor,
            LoginBackground = Branding.LoginBackground,
            FooterText = Branding.FooterText,
            CustomDomain = Branding.CustomDomain,
            EmailSenderName = Branding.EmailSenderName,
            EmailSenderAddress = Branding.EmailSenderAddress
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        Branding = await LoadOrCreateAsync(user.Id);

        if (!ModelState.IsValid) { return Page(); }

        var logoPath = await SaveUploadAsync(Logo, user.Id, "logo");
        if (logoPath == "INVALID") { TempData["Error"] = "Logo must be PNG/JPG/SVG under 2 MB."; return RedirectToPage(); }
        var favPath = await SaveUploadAsync(Favicon, user.Id, "favicon");
        if (favPath == "INVALID") { TempData["Error"] = "Favicon must be PNG/JPG/SVG under 2 MB."; return RedirectToPage(); }
        var bgPath = await SaveUploadAsync(Background, user.Id, "background");
        if (bgPath == "INVALID") { TempData["Error"] = "Background must be PNG/JPG/SVG under 2 MB."; return RedirectToPage(); }

        Branding.PanelTitle = Input.PanelTitle;
        Branding.PrimaryColor = Input.PrimaryColor;
        Branding.SecondaryColor = Input.SecondaryColor;
        Branding.AccentColor = Input.AccentColor;
        Branding.FooterText = Input.FooterText;
        Branding.CustomDomain = string.IsNullOrWhiteSpace(Input.CustomDomain) ? null : Input.CustomDomain.Trim().ToLowerInvariant();
        Branding.EmailSenderName = Input.EmailSenderName;
        Branding.EmailSenderAddress = Input.EmailSenderAddress;
        // Background: an uploaded image wins, otherwise keep the colour/text value.
        Branding.LoginBackground = bgPath ?? Input.LoginBackground;
        if (logoPath != null) Branding.LogoPath = logoPath;
        if (favPath != null) Branding.FaviconPath = favPath;
        Branding.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        await _audit.LogAsync("Update", "ResellerBranding", Branding.Id.ToString(), "branding updated");
        TempData["Success"] = "Branding saved and applied.";
        return RedirectToPage();
    }

    /// <returns>null = no file; "INVALID" = rejected; otherwise the stored web path.</returns>
    private async Task<string?> SaveUploadAsync(IFormFile? file, string resellerId, string name)
    {
        if (file == null || file.Length == 0) return null;
        if (file.Length > MaxUpload) return "INVALID";
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedImages.Contains(ext)) return "INVALID";

        var dir = Path.Combine(_env.WebRootPath, "branding", resellerId);
        Directory.CreateDirectory(dir);
        var fileName = $"{name}{ext}";
        var full = Path.Combine(dir, fileName);
        await using (var stream = System.IO.File.Create(full))
        {
            await file.CopyToAsync(stream);
        }
        return $"/branding/{resellerId}/{fileName}";
    }
}
