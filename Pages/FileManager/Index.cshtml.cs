using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SRXPanel.Models;
using SRXPanel.Services;

namespace SRXPanel.Pages.FileManager;

public class IndexModel : PageModel
{
    private readonly IFileManagerService _fm;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IRateLimitService _rateLimit;
    private readonly IAuditLogService _audit;

    public IndexModel(IFileManagerService fm, UserManager<ApplicationUser> userManager, IRateLimitService rateLimit, IAuditLogService audit)
    {
        _fm = fm;
        _userManager = userManager;
        _rateLimit = rateLimit;
        _audit = audit;
    }

    [BindProperty(SupportsGet = true)]
    public string Path { get; set; } = string.Empty;

    public IReadOnlyList<FileEntry> Entries { get; set; } = Array.Empty<FileEntry>();
    public IReadOnlyList<string> Crumbs { get; set; } = Array.Empty<string>();
    public string UserId { get; set; } = string.Empty;

    // Edit state
    public bool Editing { get; set; }
    public string? EditFileContent { get; set; }
    public string? EditFilePath { get; set; }

    private async Task<string?> GetUserIdAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        return user?.Id;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var userId = await GetUserIdAsync();
        if (userId == null) return Challenge();
        UserId = userId;

        try
        {
            _fm.EnsureUserRoot(userId);
            Entries = _fm.List(userId, Path);
            Crumbs = _fm.BreadcrumbSegments(Path);
        }
        catch (FileManagerException ex)
        {
            TempData["Error"] = ex.Message;
            Path = string.Empty;
            Entries = _fm.List(userId, Path);
        }
        return Page();
    }

    public async Task<IActionResult> OnGetEditAsync(string file)
    {
        var userId = await GetUserIdAsync();
        if (userId == null) return Challenge();
        UserId = userId;

        try
        {
            var content = _fm.ReadTextFile(userId, file, out var editable);
            if (!editable)
            {
                TempData["Error"] = "This file cannot be edited as text.";
                return RedirectToPage(new { path = ParentOf(file) });
            }
            Editing = true;
            EditFilePath = file;
            EditFileContent = content;
            Path = ParentOf(file);
            Crumbs = _fm.BreadcrumbSegments(Path);
            Entries = _fm.List(userId, Path);
        }
        catch (FileManagerException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToPage();
        }
        return Page();
    }

    public async Task<IActionResult> OnGetDownloadAsync(string file)
    {
        var userId = await GetUserIdAsync();
        if (userId == null) return Challenge();

        try
        {
            var (stream, fileName) = _fm.OpenDownload(userId, file);
            return File(stream, "application/octet-stream", fileName);
        }
        catch (FileManagerException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToPage(new { path = ParentOf(file) });
        }
    }

    public async Task<IActionResult> OnPostNewFolderAsync(string name)
    {
        return await MutateAsync(userId => _fm.CreateDirectory(userId, Path, name), "CreateFolder", name);
    }

    public async Task<IActionResult> OnPostNewFileAsync(string name)
    {
        return await MutateAsync(userId => _fm.CreateFile(userId, Path, name), "CreateFile", name);
    }

    public async Task<IActionResult> OnPostRenameAsync(string target, string newName)
    {
        return await MutateAsync(userId => _fm.Rename(userId, target, newName), "Rename", $"{target} -> {newName}");
    }

    public async Task<IActionResult> OnPostDeleteAsync(string target)
    {
        return await MutateAsync(userId => _fm.Delete(userId, target), "Delete", target);
    }

    public async Task<IActionResult> OnPostSaveFileAsync(string file, string content)
    {
        var userId = await GetUserIdAsync();
        if (userId == null) return Challenge();

        try
        {
            _fm.WriteTextFile(userId, file, content);
            await _audit.LogAsync("Edit", "File", null, file);
            TempData["Success"] = "File saved.";
        }
        catch (FileManagerException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToPage(new { path = ParentOf(file) });
    }

    public async Task<IActionResult> OnPostUploadAsync(IFormFile? upload)
    {
        var userId = await GetUserIdAsync();
        if (userId == null) return Challenge();

        if (!_rateLimit.IsAllowed(userId, "upload"))
        {
            TempData["Error"] = "Rate limit reached. Please wait a minute.";
            return RedirectToPage(new { path = Path });
        }

        if (upload == null || upload.Length == 0)
        {
            TempData["Error"] = "No file selected.";
            return RedirectToPage(new { path = Path });
        }

        try
        {
            await using var stream = upload.OpenReadStream();
            await _fm.SaveUploadAsync(userId, Path, upload.FileName, stream, upload.Length);
            await _audit.LogAsync("Upload", "File", null, upload.FileName);
            TempData["Success"] = $"Uploaded '{upload.FileName}'.";
        }
        catch (FileManagerException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToPage(new { path = Path });
    }

    private async Task<IActionResult> MutateAsync(Action<string> action, string auditAction, string detail)
    {
        var userId = await GetUserIdAsync();
        if (userId == null) return Challenge();

        if (!_rateLimit.IsAllowed(userId, "filemanager"))
        {
            TempData["Error"] = "Rate limit reached. Please wait a minute.";
            return RedirectToPage(new { path = Path });
        }

        try
        {
            action(userId);
            await _audit.LogAsync(auditAction, "File", null, detail);
            TempData["Success"] = "Done.";
        }
        catch (FileManagerException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToPage(new { path = Path });
    }

    public bool HasEditableExtension(string fileName) => _fm.IsTextEditable(fileName);

    private static string ParentOf(string relativePath)
    {
        relativePath = (relativePath ?? string.Empty).Replace('\\', '/').Trim('/');
        var idx = relativePath.LastIndexOf('/');
        return idx < 0 ? string.Empty : relativePath[..idx];
    }
}
