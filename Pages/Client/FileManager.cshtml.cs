using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Interfaces;

namespace SRXPanel.Pages.Client;

public class FileManagerModel : PageModel
{
    private readonly IFileManagerService _fm;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditLogService _audit;
    private readonly ICommandRunner _runner;

    public FileManagerModel(IFileManagerService fm, UserManager<ApplicationUser> userManager,
        IAuditLogService audit, ICommandRunner runner)
    {
        _fm = fm;
        _userManager = userManager;
        _audit = audit;
        _runner = runner;
    }

    [BindProperty(SupportsGet = true)] public string Path { get; set; } = string.Empty;
    [BindProperty(SupportsGet = true)] public string? Query { get; set; }
    [BindProperty(SupportsGet = true)] public bool ShowHidden { get; set; }

    public IReadOnlyList<FileEntry> Entries { get; set; } = Array.Empty<FileEntry>();
    public IReadOnlyList<FileEntry> Tree { get; set; } = Array.Empty<FileEntry>();
    public IReadOnlyList<string> Crumbs { get; set; } = Array.Empty<string>();
    public bool Editing { get; set; }
    public string? EditContent { get; set; }
    public string? EditFile { get; set; }

    private async Task<string?> UidAsync() => (await _userManager.GetUserAsync(User))?.Id;

    public async Task<IActionResult> OnGetAsync()
    {
        var uid = await UidAsync();
        if (uid == null) return Challenge();
        _fm.EnsureUserRoot(uid);
        Tree = _fm.DirectoryTree(uid);
        try
        {
            Entries = string.IsNullOrWhiteSpace(Query)
                ? _fm.List(uid, Path).Where(e => ShowHidden || !e.Name.StartsWith('.')).ToList()
                : _fm.Search(uid, Path, Query!, ShowHidden);
            Crumbs = _fm.BreadcrumbSegments(Path);
        }
        catch (FileManagerException ex) { TempData["Error"] = ex.Message; }
        return Page();
    }

    public async Task<IActionResult> OnGetEditAsync(string file)
    {
        var uid = await UidAsync();
        if (uid == null) return Challenge();
        try
        {
            var content = _fm.ReadTextFile(uid, file, out var editable);
            if (!editable) { TempData["Error"] = "This file cannot be edited as text."; return RedirectToPage(new { path = Parent(file) }); }
            Editing = true; EditFile = file; EditContent = content; Path = Parent(file);
            Tree = _fm.DirectoryTree(uid); Crumbs = _fm.BreadcrumbSegments(Path);
            Entries = _fm.List(uid, Path);
        }
        catch (FileManagerException ex) { TempData["Error"] = ex.Message; return RedirectToPage(); }
        return Page();
    }

    public async Task<IActionResult> OnGetDownloadAsync(string file)
    {
        var uid = await UidAsync();
        if (uid == null) return Challenge();
        try { var (s, name) = _fm.OpenDownload(uid, file); return File(s, "application/octet-stream", name); }
        catch (FileManagerException ex) { TempData["Error"] = ex.Message; return RedirectToPage(new { path = Parent(file) }); }
    }

    public Task<IActionResult> OnPostNewFolderAsync(string name) => Do(uid => _fm.CreateDirectory(uid, Path, name), "NewFolder", name);
    public Task<IActionResult> OnPostNewFileAsync(string name) => Do(uid => _fm.CreateFile(uid, Path, name), "NewFile", name);
    public Task<IActionResult> OnPostDeleteAsync(string target) => Do(uid => _fm.Delete(uid, target), "Delete", target);
    public Task<IActionResult> OnPostRenameAsync(string target, string newName) => Do(uid => _fm.Rename(uid, target, newName), "Rename", newName);
    public Task<IActionResult> OnPostExtractAsync(string target) => Do(uid => _fm.ExtractArchive(uid, target), "Extract", target);
    public Task<IActionResult> OnPostCopyAsync(string target, string dest) => Do(uid => _fm.Copy(uid, target, dest), "Copy", target);
    public Task<IActionResult> OnPostMoveAsync(string target, string dest) => Do(uid => _fm.Move(uid, target, dest), "Move", target);

    public async Task<IActionResult> OnPostChmodAsync(string target, string mode)
    {
        var uid = await UidAsync();
        if (uid == null) return Challenge();
        try { _fm.Chmod(uid, target, mode); await _runner.RunAsync($"chmod {mode} {target}", "filemanager"); TempData["Success"] = $"Permissions set to {mode}."; }
        catch (FileManagerException ex) { TempData["Error"] = ex.Message; }
        return RedirectToPage(new { path = Path });
    }

    public async Task<IActionResult> OnPostCompressAsync(string[] items, string zipName)
    {
        var uid = await UidAsync();
        if (uid == null) return Challenge();
        try { _fm.CompressZip(uid, Path, items ?? Array.Empty<string>(), zipName); TempData["Success"] = "Archive created."; }
        catch (FileManagerException ex) { TempData["Error"] = ex.Message; }
        return RedirectToPage(new { path = Path });
    }

    public async Task<IActionResult> OnPostSaveFileAsync(string file, string content)
    {
        var uid = await UidAsync();
        if (uid == null) return Challenge();
        try { _fm.WriteTextFile(uid, file, content); await _audit.LogAsync("Edit", "File", null, file); TempData["Success"] = "Saved."; }
        catch (FileManagerException ex) { TempData["Error"] = ex.Message; }
        return RedirectToPage(new { path = Parent(file) });
    }

    public async Task<IActionResult> OnPostUploadAsync(List<IFormFile> uploads)
    {
        var uid = await UidAsync();
        if (uid == null) return Challenge();
        int ok = 0;
        foreach (var f in uploads ?? new())
        {
            if (f.Length == 0) continue;
            try { await using var s = f.OpenReadStream(); await _fm.SaveUploadAsync(uid, Path, f.FileName, s, f.Length); ok++; }
            catch (FileManagerException ex) { TempData["Error"] = ex.Message; }
        }
        if (ok > 0) { await _audit.LogAsync("Upload", "File", null, $"{ok} file(s)"); TempData["Success"] = $"Uploaded {ok} file(s)."; }
        return RedirectToPage(new { path = Path });
    }

    public bool CanEdit(string name) => _fm.IsTextEditable(name);

    private async Task<IActionResult> Do(Action<string> action, string auditAction, string detail)
    {
        var uid = await UidAsync();
        if (uid == null) return Challenge();
        try { action(uid); await _audit.LogAsync(auditAction, "File", null, detail); TempData["Success"] = "Done."; }
        catch (FileManagerException ex) { TempData["Error"] = ex.Message; }
        return RedirectToPage(new { path = Path });
    }

    private static string Parent(string rel)
    {
        rel = (rel ?? "").Replace('\\', '/').Trim('/');
        var i = rel.LastIndexOf('/');
        return i < 0 ? "" : rel[..i];
    }
}
