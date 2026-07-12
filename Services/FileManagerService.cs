using System.IO.Compression;

namespace SRXPanel.Services;

public class FileEntry
{
    public string Name { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public long Size { get; set; }
    public DateTime Modified { get; set; }
    public string Permissions { get; set; } = string.Empty;
    // Path relative to the user's sandbox root, using forward slashes.
    public string RelativePath { get; set; } = string.Empty;
}

public class FileManagerException : Exception
{
    public FileManagerException(string message) : base(message) { }
}

public interface IFileManagerService
{
    string EnsureUserRoot(string userId);
    long GetUsedBytes(string userId);
    IReadOnlyList<FileEntry> List(string userId, string relativePath);
    IReadOnlyList<string> BreadcrumbSegments(string relativePath);
    void CreateDirectory(string userId, string relativePath, string name);
    void CreateFile(string userId, string relativePath, string name);
    void Delete(string userId, string relativePath);
    void Rename(string userId, string relativePath, string newName);
    Task SaveUploadAsync(string userId, string relativePath, string fileName, Stream content, long length);
    string ReadTextFile(string userId, string relativePath, out bool isEditable);
    void WriteTextFile(string userId, string relativePath, string content);
    (Stream stream, string fileName) OpenDownload(string userId, string relativePath);
    bool IsTextEditable(string fileName);

    // Phase 5 enhancements
    void Copy(string userId, string relativePath, string destDir);
    void Move(string userId, string relativePath, string destDir);
    void Chmod(string userId, string relativePath, string mode);
    string CompressZip(string userId, string currentDir, IEnumerable<string> relativePaths, string zipName);
    void ExtractArchive(string userId, string relativePath);
    IReadOnlyList<FileEntry> Search(string userId, string relativePath, string query, bool includeHidden);
    IReadOnlyList<FileEntry> DirectoryTree(string userId, int maxDepth = 2);
}

public class FileManagerService : IFileManagerService
{
    private readonly string _homesRoot;
    private readonly HashSet<string> _allowedUploadExtensions;
    public const long MaxUploadBytes = 100L * 1024 * 1024; // 100 MB

    public FileManagerService(IWebHostEnvironment env, IConfiguration config)
    {
        _homesRoot = Path.Combine(env.ContentRootPath, "App_Data", "homes");
        Directory.CreateDirectory(_homesRoot);

        var configured = config.GetSection("FileManager:AllowedUploadExtensions").Get<string[]>();
        _allowedUploadExtensions = new HashSet<string>(
            configured ?? new[]
            {
                ".txt", ".html", ".htm", ".css", ".js", ".json", ".xml", ".md",
                ".php", ".jpg", ".jpeg", ".png", ".gif", ".svg", ".webp", ".ico",
                ".pdf", ".zip", ".gz", ".tar", ".csv", ".woff", ".woff2", ".ttf",
                ".env", ".htaccess", ".yml", ".yaml", ".sql"
            },
            StringComparer.OrdinalIgnoreCase);
    }

    private static readonly HashSet<string> TextEditableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".html", ".htm", ".css", ".js", ".json", ".xml", ".md",
        ".php", ".env", ".htaccess", ".yml", ".yaml", ".sql", ".config", ".ini", ".log", ".sh", ".conf"
    };

    public string EnsureUserRoot(string userId)
    {
        var root = Path.Combine(_homesRoot, Sanitize(userId), "public_html");
        Directory.CreateDirectory(root);
        return root;
    }

    private string UserRoot(string userId) => EnsureUserRoot(userId);

    public long GetUsedBytes(string userId)
    {
        var root = UserRoot(userId);
        try
        {
            return new DirectoryInfo(root)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(f => f.Length);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Resolves a relative path against the user's sandbox root and guarantees
    /// the result stays inside that root (prevents ../ traversal).
    /// </summary>
    private string ResolveSafe(string userId, string relativePath)
    {
        var root = UserRoot(userId);
        var rootFull = Path.GetFullPath(root);

        relativePath = (relativePath ?? string.Empty).Replace('\\', '/').TrimStart('/');
        var combined = Path.GetFullPath(Path.Combine(rootFull, relativePath));

        if (!combined.Equals(rootFull, StringComparison.OrdinalIgnoreCase) &&
            !combined.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new FileManagerException("Access denied: path is outside your home directory.");
        }

        return combined;
    }

    private string ToRelative(string userId, string fullPath)
    {
        var rootFull = Path.GetFullPath(UserRoot(userId));
        var rel = Path.GetRelativePath(rootFull, fullPath);
        return rel == "." ? string.Empty : rel.Replace('\\', '/');
    }

    public IReadOnlyList<FileEntry> List(string userId, string relativePath)
    {
        var dir = ResolveSafe(userId, relativePath);
        if (!Directory.Exists(dir))
        {
            return Array.Empty<FileEntry>();
        }

        var entries = new List<FileEntry>();

        foreach (var d in Directory.GetDirectories(dir))
        {
            var info = new DirectoryInfo(d);
            entries.Add(new FileEntry
            {
                Name = info.Name,
                IsDirectory = true,
                Size = 0,
                Modified = info.LastWriteTime,
                Permissions = "drwxr-xr-x",
                RelativePath = ToRelative(userId, d)
            });
        }

        foreach (var f in Directory.GetFiles(dir))
        {
            var info = new FileInfo(f);
            entries.Add(new FileEntry
            {
                Name = info.Name,
                IsDirectory = false,
                Size = info.Length,
                Modified = info.LastWriteTime,
                Permissions = "-rw-r--r--",
                RelativePath = ToRelative(userId, f)
            });
        }

        return entries
            .OrderByDescending(e => e.IsDirectory)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<string> BreadcrumbSegments(string relativePath)
    {
        relativePath = (relativePath ?? string.Empty).Replace('\\', '/').Trim('/');
        return string.IsNullOrEmpty(relativePath)
            ? Array.Empty<string>()
            : relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
    }

    public void CreateDirectory(string userId, string relativePath, string name)
    {
        name = Sanitize(name);
        if (string.IsNullOrWhiteSpace(name)) throw new FileManagerException("Invalid folder name.");
        var target = ResolveSafe(userId, CombineRel(relativePath, name));
        if (Directory.Exists(target) || File.Exists(target)) throw new FileManagerException("An item with that name already exists.");
        Directory.CreateDirectory(target);
    }

    public void CreateFile(string userId, string relativePath, string name)
    {
        name = Sanitize(name);
        if (string.IsNullOrWhiteSpace(name)) throw new FileManagerException("Invalid file name.");
        var target = ResolveSafe(userId, CombineRel(relativePath, name));
        if (File.Exists(target) || Directory.Exists(target)) throw new FileManagerException("An item with that name already exists.");
        File.WriteAllText(target, string.Empty);
    }

    public void Delete(string userId, string relativePath)
    {
        var target = ResolveSafe(userId, relativePath);
        var rootFull = Path.GetFullPath(UserRoot(userId));
        if (target.Equals(rootFull, StringComparison.OrdinalIgnoreCase))
            throw new FileManagerException("Cannot delete the home directory.");

        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
        else if (File.Exists(target)) File.Delete(target);
        else throw new FileManagerException("Item not found.");
    }

    public void Rename(string userId, string relativePath, string newName)
    {
        newName = Sanitize(newName);
        if (string.IsNullOrWhiteSpace(newName)) throw new FileManagerException("Invalid name.");
        var source = ResolveSafe(userId, relativePath);
        var parent = Path.GetDirectoryName(relativePath.Replace('\\', '/'))?.Replace('\\', '/') ?? string.Empty;
        var target = ResolveSafe(userId, CombineRel(parent, newName));
        if (File.Exists(target) || Directory.Exists(target)) throw new FileManagerException("An item with that name already exists.");

        if (Directory.Exists(source)) Directory.Move(source, target);
        else if (File.Exists(source)) File.Move(source, target);
        else throw new FileManagerException("Item not found.");
    }

    public async Task SaveUploadAsync(string userId, string relativePath, string fileName, Stream content, long length)
    {
        if (length > MaxUploadBytes)
            throw new FileManagerException("File exceeds the 100 MB upload limit.");

        fileName = Sanitize(Path.GetFileName(fileName));
        var ext = Path.GetExtension(fileName);
        if (!_allowedUploadExtensions.Contains(ext))
            throw new FileManagerException($"File type '{ext}' is not allowed.");

        var target = ResolveSafe(userId, CombineRel(relativePath, fileName));
        await using var fs = new FileStream(target, FileMode.Create, FileAccess.Write);
        await content.CopyToAsync(fs);
    }

    public string ReadTextFile(string userId, string relativePath, out bool isEditable)
    {
        var target = ResolveSafe(userId, relativePath);
        if (!File.Exists(target)) throw new FileManagerException("File not found.");

        isEditable = IsTextEditable(target);
        if (!isEditable) return string.Empty;
        if (new FileInfo(target).Length > 2 * 1024 * 1024)
        {
            isEditable = false;
            return string.Empty;
        }
        return File.ReadAllText(target);
    }

    public void WriteTextFile(string userId, string relativePath, string content)
    {
        var target = ResolveSafe(userId, relativePath);
        if (!File.Exists(target)) throw new FileManagerException("File not found.");
        if (!IsTextEditable(target)) throw new FileManagerException("This file type cannot be edited.");
        File.WriteAllText(target, content ?? string.Empty);
    }

    public (Stream stream, string fileName) OpenDownload(string userId, string relativePath)
    {
        var target = ResolveSafe(userId, relativePath);
        if (!File.Exists(target)) throw new FileManagerException("File not found.");
        var stream = new FileStream(target, FileMode.Open, FileAccess.Read);
        return (stream, Path.GetFileName(target));
    }

    public bool IsTextEditable(string fileName) => TextEditableExtensions.Contains(Path.GetExtension(fileName));

    public void Copy(string userId, string relativePath, string destDir)
    {
        var source = ResolveSafe(userId, relativePath);
        var name = Path.GetFileName(source);
        var target = ResolveSafe(userId, CombineRel(destDir, name));
        if (Directory.Exists(source))
        {
            CopyDirectory(source, target);
        }
        else if (File.Exists(source))
        {
            File.Copy(source, target, overwrite: false);
        }
        else throw new FileManagerException("Item not found.");
    }

    public void Move(string userId, string relativePath, string destDir)
    {
        var source = ResolveSafe(userId, relativePath);
        var name = Path.GetFileName(source);
        var target = ResolveSafe(userId, CombineRel(destDir, name));
        if (source.Equals(target, StringComparison.OrdinalIgnoreCase)) return;
        if (Directory.Exists(source)) Directory.Move(source, target);
        else if (File.Exists(source)) File.Move(source, target);
        else throw new FileManagerException("Item not found.");
    }

    public void Chmod(string userId, string relativePath, string mode)
    {
        // Validate target stays in sandbox; chmod bits are a no-op on Windows dev,
        // stored intent only. On Linux the caller runs the actual chmod via CommandRunner.
        ResolveSafe(userId, relativePath);
        if (!System.Text.RegularExpressions.Regex.IsMatch(mode, "^[0-7]{3,4}$"))
            throw new FileManagerException("Invalid permissions (use octal like 755).");
    }

    public string CompressZip(string userId, string currentDir, IEnumerable<string> relativePaths, string zipName)
    {
        zipName = Sanitize(zipName);
        if (!zipName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) zipName += ".zip";
        var zipTarget = ResolveSafe(userId, CombineRel(currentDir, zipName));
        if (File.Exists(zipTarget)) throw new FileManagerException("A file with that name already exists.");

        using var archive = System.IO.Compression.ZipFile.Open(zipTarget, System.IO.Compression.ZipArchiveMode.Create);
        foreach (var rel in relativePaths)
        {
            var full = ResolveSafe(userId, rel);
            var entryName = Path.GetFileName(full);
            if (Directory.Exists(full))
            {
                foreach (var file in Directory.GetFiles(full, "*", SearchOption.AllDirectories))
                {
                    var name = entryName + "/" + Path.GetRelativePath(full, file).Replace('\\', '/');
                    archive.CreateEntryFromFile(file, name);
                }
            }
            else if (File.Exists(full))
            {
                archive.CreateEntryFromFile(full, entryName);
            }
        }
        return ToRelative(userId, zipTarget);
    }

    public void ExtractArchive(string userId, string relativePath)
    {
        var source = ResolveSafe(userId, relativePath);
        if (!File.Exists(source)) throw new FileManagerException("Archive not found.");
        var destDir = Path.GetDirectoryName(source)!;
        var ext = Path.GetExtension(source).ToLowerInvariant();
        if (ext != ".zip")
            throw new FileManagerException("Only .zip archives can be extracted in simulation mode.");

        // Guard against zip-slip: ensure every entry stays within the sandbox root.
        var rootFull = Path.GetFullPath(UserRoot(userId));
        using var archive = System.IO.Compression.ZipFile.OpenRead(source);
        foreach (var entry in archive.Entries)
        {
            var destPath = Path.GetFullPath(Path.Combine(destDir, entry.FullName));
            if (!destPath.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                throw new FileManagerException("Archive contains unsafe paths.");
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destPath);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            entry.ExtractToFile(destPath, overwrite: true);
        }
    }

    public IReadOnlyList<FileEntry> Search(string userId, string relativePath, string query, bool includeHidden)
    {
        var dir = ResolveSafe(userId, relativePath);
        if (!Directory.Exists(dir)) return Array.Empty<FileEntry>();

        var results = new List<FileEntry>();
        foreach (var path in Directory.EnumerateFileSystemEntries(dir, "*", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(path);
            if (!includeHidden && name.StartsWith('.')) continue;
            if (!name.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;

            var isDir = Directory.Exists(path);
            var info = new FileInfo(path);
            results.Add(new FileEntry
            {
                Name = name,
                IsDirectory = isDir,
                Size = isDir ? 0 : info.Length,
                Modified = info.LastWriteTime,
                Permissions = isDir ? "drwxr-xr-x" : "-rw-r--r--",
                RelativePath = ToRelative(userId, path)
            });
            if (results.Count >= 200) break;
        }
        return results;
    }

    public IReadOnlyList<FileEntry> DirectoryTree(string userId, int maxDepth = 2)
    {
        var root = UserRoot(userId);
        var list = new List<FileEntry>();
        void Walk(string dir, int depth)
        {
            if (depth > maxDepth) return;
            foreach (var d in Directory.GetDirectories(dir))
            {
                list.Add(new FileEntry
                {
                    Name = Path.GetFileName(d),
                    IsDirectory = true,
                    RelativePath = ToRelative(userId, d),
                    Permissions = new string('—', depth) // depth marker for indentation
                });
                Walk(d, depth + 1);
            }
        }
        Walk(root, 0);
        return list;
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: false);
        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
    }

    private static string CombineRel(string relativePath, string name)
    {
        relativePath = (relativePath ?? string.Empty).Replace('\\', '/').Trim('/');
        return string.IsNullOrEmpty(relativePath) ? name : $"{relativePath}/{name}";
    }

    // Strips path separators and traversal tokens from a single name segment.
    private static string Sanitize(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        name = name.Trim().Replace("\\", string.Empty).Replace("/", string.Empty);
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c.ToString(), string.Empty);
        }
        if (name is "." or "..") return string.Empty;
        return name;
    }
}
