using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SRXPanel.Models;
using SRXPanel.Services;
using SRXPanel.Services.Developer;

namespace SRXPanel.Pages.Client.Developer;

public class PackageManagerModel : PageModel
{
    private readonly IPackageManagerService _packages;
    private readonly IFileManagerService _files;
    private readonly UserManager<ApplicationUser> _userManager;

    public PackageManagerModel(IPackageManagerService packages, IFileManagerService files,
        UserManager<ApplicationUser> userManager)
    {
        _packages = packages;
        _files = files;
        _userManager = userManager;
    }

    [BindProperty(SupportsGet = true)] public string WorkingDir { get; set; } = "/";
    [BindProperty(SupportsGet = true)] public PackageRunner Tab { get; set; } = PackageRunner.Composer;

    public IReadOnlyList<string> Directories { get; private set; } = Array.Empty<string>();
    public PackageFile Manifest { get; private set; } = new("", "", false);
    public long DependencySize { get; private set; }
    public IReadOnlyList<string> NodeVersions { get; private set; } = Array.Empty<string>();
    public IReadOnlyList<string> NpmScripts { get; private set; } = Array.Empty<string>();
    public bool LockFileExists { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        Directories = _packages.GetWorkingDirectories(user.Id, Tab);
        if (!Directories.Contains(WorkingDir)) WorkingDir = Directories.FirstOrDefault() ?? "/";

        Manifest = _packages.ReadManifest(user.Id, WorkingDir, Tab);
        DependencySize = _packages.GetDependencySize(user.Id, WorkingDir, Tab);
        NodeVersions = _packages.GetNodeVersions();
        NpmScripts = Tab == PackageRunner.Npm ? _packages.GetNpmScripts(user.Id, WorkingDir) : Array.Empty<string>();

        var lockName = Tab switch
        {
            PackageRunner.Composer => "composer.lock",
            PackageRunner.Npm => "package-lock.json",
            _ => "requirements.txt"
        };
        LockFileExists = Manifest.Exists && FileExists(user.Id, WorkingDir, lockName);

        return Page();
    }

    /// <summary>Probes for a file inside the user's sandbox — the service throws when it is missing.</summary>
    private bool FileExists(string userId, string workingDir, string fileName)
    {
        try
        {
            var relative = workingDir.Trim('/');
            var path = string.IsNullOrEmpty(relative) ? fileName : $"{relative}/{fileName}";
            _files.ReadTextFile(userId, path, out _);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Starts a command and returns the runner id the page subscribes to.</summary>
    public async Task<IActionResult> OnPostRunAsync(string command)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        try
        {
            var runnerId = await _packages.RunAsync(user.Id, WorkingDir, Tab, command);
            return new JsonResult(new { runnerId });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    public async Task<IActionResult> OnPostSaveManifestAsync(string content)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        try
        {
            _packages.WriteManifest(user.Id, WorkingDir, Tab, content);
            TempData["Success"] = $"{Manifest.Name} saved.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToPage(new { workingDir = WorkingDir, tab = Tab });
    }
}
