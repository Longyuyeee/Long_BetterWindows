using System.IO;
using System.Security.Cryptography;
using System.Text;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Services
{
    public class FileSystemService : IFileSystemService
    {
        private const string OrganizedDirectoryName = "Long Organized";

        public Task<HostApiResponse<List<FileItem>>> EnumerateFilesAsync(string path, string searchPattern = "*.*", bool recursive = true)
        {
            return Task.Run(() =>
            {
                try
                {
                    var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                    var files = Directory.GetFiles(path, searchPattern, searchOption)
                        .Select(f => new FileInfo(f))
                        .Select(fi => new FileItem
                        {
                            FullPath = fi.FullName,
                            Name = fi.Name,
                            Size = fi.Length,
                            CreatedTime = fi.CreationTime,
                            ModifiedTime = fi.LastWriteTime,
                            Extension = fi.Extension
                        }).ToList();

                    return HostApiResponse<List<FileItem>>.Success(files);
                }
                catch (Exception ex)
                {
                    return HostApiResponse<List<FileItem>>.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public Task<HostApiResponse<string>> ComputeFileHashAsync(string filePath)
        {
            return Task.Run(() =>
            {
                try
                {
                    using var md5 = MD5.Create();
                    using var stream = File.OpenRead(filePath);
                    var hash = md5.ComputeHash(stream);
                    var hashString = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                    return HostApiResponse<string>.Success(hashString);
                }
                catch (Exception ex)
                {
                    return HostApiResponse<string>.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public Task<HostApiResponse<FileMetadata>> GetFileMetadataAsync(string filePath)
        {
            return Task.Run(() =>
            {
                try
                {
                    var fi = new FileInfo(filePath);
                    var metadata = new FileMetadata
                    {
                        FullPath = fi.FullName,
                        Size = fi.Length,
                        CreatedTime = fi.CreationTime,
                        ModifiedTime = fi.LastWriteTime,
                        AccessedTime = fi.LastAccessTime,
                        Extension = fi.Extension,
                        IsReadOnly = fi.IsReadOnly,
                        IsHidden = (fi.Attributes & FileAttributes.Hidden) != 0
                    };

                    return HostApiResponse<FileMetadata>.Success(metadata);
                }
                catch (Exception ex)
                {
                    return HostApiResponse<FileMetadata>.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public Task<HostApiResponse<List<DuplicateFileGroup>>> FindDuplicateFilesAsync(string path)
        {
            return Task.Run(() =>
            {
                try
                {
                    var files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories);
                    var hashGroups = new Dictionary<string, List<string>>();

                    foreach (var file in files)
                    {
                        try
                        {
                            using var md5 = MD5.Create();
                            using var stream = File.OpenRead(file);
                            var hash = md5.ComputeHash(stream);
                            var hashString = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

                            if (!hashGroups.ContainsKey(hashString))
                                hashGroups[hashString] = new List<string>();
                            hashGroups[hashString].Add(file);
                        }
                        catch { }
                    }

                    var duplicates = hashGroups
                        .Where(g => g.Value.Count > 1)
                        .Select(g => new DuplicateFileGroup
                        {
                            Hash = g.Key,
                            Size = new FileInfo(g.Value[0]).Length,
                            FilePaths = g.Value
                        }).ToList();

                    return HostApiResponse<List<DuplicateFileGroup>>.Success(duplicates);
                }
                catch (Exception ex)
                {
                    return HostApiResponse<List<DuplicateFileGroup>>.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public Task<HostApiResponse<int>> BatchRenameAsync(List<RenameOperation> operations)
        {
            return Task.Run(() =>
            {
                var completed = new List<(string Source, string Destination)>();
                try
                {
                    if (operations is null)
                        throw new ArgumentNullException(nameof(operations));

                    var planned = new List<(string Source, string Destination)>();
                    var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var operation in operations)
                    {
                        if (operation is null)
                            throw new InvalidDataException("Rename operation is required.");
                        var source = Path.GetFullPath(operation.OldPath);
                        if (!File.Exists(source))
                            throw new FileNotFoundException(
                                "Rename source does not exist.",
                                source);
                        if (!sources.Add(source))
                            throw new IOException("Duplicate rename source.");
                        ValidateFileName(operation.NewName);
                        var directory = Path.GetDirectoryName(source)
                            ?? throw new InvalidDataException(
                                "Rename source has no parent directory.");
                        var destination = Path.GetFullPath(
                            Path.Combine(directory, operation.NewName));
                        if (!string.Equals(
                                Path.GetDirectoryName(destination),
                                directory,
                                StringComparison.OrdinalIgnoreCase))
                            throw new UnauthorizedAccessException(
                                "Rename destination must remain in the source directory.");
                        if (!destinations.Add(destination))
                            throw new IOException("Duplicate rename destination.");
                        if (!source.Equals(
                                destination,
                                StringComparison.OrdinalIgnoreCase)
                            && File.Exists(destination))
                            throw new IOException(
                                "Rename destination already exists.");
                        planned.Add((source, destination));
                    }

                    foreach (var item in planned.Where(item =>
                                 !item.Source.Equals(
                                     item.Destination,
                                     StringComparison.Ordinal)))
                    {
                        File.Move(item.Source, item.Destination);
                        completed.Add(item);
                    }

                    return HostApiResponse<int>.Success(completed.Count);
                }
                catch (Exception ex)
                {
                    var rollbackFailures = new List<string>();
                    for (var index = completed.Count - 1; index >= 0; index--)
                    {
                        var item = completed[index];
                        try
                        {
                            if (File.Exists(item.Destination)
                                && !File.Exists(item.Source))
                                File.Move(item.Destination, item.Source);
                        }
                        catch (Exception rollbackException)
                        {
                            rollbackFailures.Add(rollbackException.Message);
                        }
                    }
                    var detail = rollbackFailures.Count == 0
                        ? ex.Message
                        : $"{ex.Message} Rollback failed: {string.Join(" | ", rollbackFailures)}";
                    return HostApiResponse<int>.Failure(
                        ApiErrorCode.Unknown,
                        detail);
                }
            });
        }

        public Task<HostApiResponse<Dictionary<string, List<string>>>> ClassifyFilesAsync(string path, ClassifyMode mode)
        {
            return Task.Run(() =>
            {
                try
                {
                    var files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories);
                    var classified = new Dictionary<string, List<string>>();

                    foreach (var file in files)
                    {
                        try
                        {
                            var fi = new FileInfo(file);
                            string category = mode switch
                            {
                                ClassifyMode.ByExtension => fi.Extension.ToLower(),
                                ClassifyMode.ByDate => fi.LastWriteTime.ToString("yyyy-MM"),
                                ClassifyMode.BySize => fi.Length switch
                                {
                                    < 1024 * 1024 => "小文件 (<1MB)",
                                    < 1024 * 1024 * 100 => "中等文件 (1-100MB)",
                                    _ => "大文件 (>100MB)"
                                },
                                _ => "未分类"
                            };

                            if (!classified.ContainsKey(category))
                                classified[category] = new List<string>();
                            classified[category].Add(file);
                        }
                        catch { }
                    }

                    return HostApiResponse<Dictionary<string, List<string>>>.Success(classified);
                }
                catch (Exception ex)
                {
                    return HostApiResponse<Dictionary<string, List<string>>>.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public Task<HostApiResponse<List<FileItem>>> FindLargeFilesAsync(string path, long minSizeBytes)
        {
            return Task.Run(() =>
            {
                try
                {
                    var files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories)
                        .Select(f => new FileInfo(f))
                        .Where(fi => fi.Length >= minSizeBytes)
                        .OrderByDescending(fi => fi.Length)
                        .Select(fi => new FileItem
                        {
                            FullPath = fi.FullName,
                            Name = fi.Name,
                            Size = fi.Length,
                            CreatedTime = fi.CreationTime,
                            ModifiedTime = fi.LastWriteTime,
                            Extension = fi.Extension
                        }).ToList();

                    return HostApiResponse<List<FileItem>>.Success(files);
                }
                catch (Exception ex)
                {
                    return HostApiResponse<List<FileItem>>.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public Task<HostApiResponse<List<SearchResult>>> SearchFileContentAsync(string path, string keyword, List<string> extensions)
        {
            return Task.Run(() =>
            {
                try
                {
                    var results = new List<SearchResult>();
                    var files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories)
                        .Where(f => extensions.Count == 0 || extensions.Contains(Path.GetExtension(f).ToLower()));

                    foreach (var file in files)
                    {
                        try
                        {
                            var lines = File.ReadAllLines(file);
                            for (int i = 0; i < lines.Length; i++)
                            {
                                if (lines[i].Contains(keyword, StringComparison.OrdinalIgnoreCase))
                                {
                                    results.Add(new SearchResult
                                    {
                                        FilePath = file,
                                        LineNumber = i + 1,
                                        MatchedLine = lines[i].Trim(),
                                        Context = GetContext(lines, i, 2)
                                    });
                                }
                            }
                        }
                        catch { }
                    }

                    return HostApiResponse<List<SearchResult>>.Success(results);
                }
                catch (Exception ex)
                {
                    return HostApiResponse<List<SearchResult>>.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public Task<HostApiResponse<List<FileOrganizationItem>>> PlanFileOrganizationAsync(
            string path,
            ClassifyMode mode)
        {
            return Task.Run(() =>
            {
                try
                {
                    var root = NormalizeExistingRoot(path);
                    var items = Directory.GetFiles(root, "*", SearchOption.TopDirectoryOnly)
                        .Select(file => BuildOrganizationItem(root, new FileInfo(file), mode))
                        .OrderBy(item => item.Category, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    return HostApiResponse<List<FileOrganizationItem>>.Success(items);
                }
                catch (Exception ex)
                {
                    return HostApiResponse<List<FileOrganizationItem>>.Failure(
                        ApiErrorCode.Unknown,
                        ex.Message);
                }
            });
        }

        public Task<HostApiResponse<FileOrganizationResult>> ExecuteFileOrganizationAsync(
            string path,
            ClassifyMode mode,
            List<FileOrganizationItem> items)
        {
            return Task.Run(() =>
            {
                try
                {
                    var root = NormalizeExistingRoot(path);
                    var result = new FileOrganizationResult
                    {
                        PlannedCount = items.Count,
                    };
                    var seenSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var seenDestinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var planned = new List<(string Source, string Destination)>();

                    foreach (var requested in items)
                    {
                        if (requested is null)
                        {
                            result.Failures.Add(new FileOrganizationFailure
                            {
                                Detail = "Organization item is required.",
                            });
                            break;
                        }

                        try
                        {
                            var source = Path.GetFullPath(requested.SourcePath);
                            if (!seenSources.Add(source))
                                throw new IOException("Duplicate source path.");
                            if (!File.Exists(source))
                                throw new FileNotFoundException("Source file no longer exists.", source);
                            if (!Path.GetDirectoryName(source)!.Equals(
                                    root,
                                    StringComparison.OrdinalIgnoreCase))
                                throw new UnauthorizedAccessException(
                                    "Source must be a top-level file in the selected folder.");
                            if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
                                throw new UnauthorizedAccessException(
                                    "Reparse-point files cannot be organized.");

                            var expected = BuildOrganizationItem(root, new FileInfo(source), mode);
                            var destination = Path.GetFullPath(requested.DestinationPath);
                            if (!destination.Equals(
                                    expected.DestinationPath,
                                    StringComparison.OrdinalIgnoreCase))
                                throw new UnauthorizedAccessException(
                                    "Destination does not match the current organization rule.");
                            if (!seenDestinations.Add(destination))
                                throw new IOException("Duplicate destination path.");

                            var destinationDirectory = Path.GetDirectoryName(destination)!;
                            EnsureDirectoryIsNotReparsePoint(
                                Path.Combine(root, OrganizedDirectoryName));
                            EnsureDirectoryIsNotReparsePoint(destinationDirectory);
                            if (File.Exists(destination))
                                throw new IOException("Destination file already exists.");

                            planned.Add((source, destination));
                        }
                        catch (Exception ex)
                        {
                            result.Failures.Add(new FileOrganizationFailure
                            {
                                SourcePath = requested.SourcePath,
                                DestinationPath = requested.DestinationPath,
                                Detail = ex.Message,
                            });
                            break;
                        }
                    }

                    if (result.Failures.Count > 0)
                        return HostApiResponse<FileOrganizationResult>.Success(result);

                    var completed = new List<(string Source, string Destination)>();
                    var createdDirectories = new HashSet<string>(
                        StringComparer.OrdinalIgnoreCase);
                    try
                    {
                        foreach (var item in planned)
                        {
                            var destinationDirectory =
                                Path.GetDirectoryName(item.Destination)!;
                            if (!Directory.Exists(destinationDirectory))
                            {
                                var missingDirectories = new Stack<string>();
                                var candidate = destinationDirectory;
                                while (!Directory.Exists(candidate)
                                    && candidate.StartsWith(
                                        root + Path.DirectorySeparatorChar,
                                        StringComparison.OrdinalIgnoreCase))
                                {
                                    missingDirectories.Push(candidate);
                                    candidate = Path.GetDirectoryName(candidate)
                                        ?? root;
                                }
                                Directory.CreateDirectory(destinationDirectory);
                                foreach (var createdDirectory in missingDirectories)
                                    createdDirectories.Add(createdDirectory);
                            }
                            EnsureDirectoryIsNotReparsePoint(destinationDirectory);
                            File.Move(item.Source, item.Destination);
                            completed.Add(item);
                        }
                        result.MovedCount = completed.Count;
                    }
                    catch (Exception executionException)
                    {
                        var rollbackFailures = new List<string>();
                        for (var index = completed.Count - 1; index >= 0; index--)
                        {
                            var item = completed[index];
                            try
                            {
                                if (File.Exists(item.Destination)
                                    && !File.Exists(item.Source))
                                    File.Move(item.Destination, item.Source);
                            }
                            catch (Exception rollbackException)
                            {
                                rollbackFailures.Add(rollbackException.Message);
                            }
                        }
                        foreach (var directory in createdDirectories
                                     .OrderByDescending(item => item.Length))
                        {
                            try
                            {
                                if (Directory.Exists(directory)
                                    && !Directory.EnumerateFileSystemEntries(
                                        directory).Any())
                                    Directory.Delete(directory);
                            }
                            catch (Exception rollbackException)
                            {
                                rollbackFailures.Add(rollbackException.Message);
                            }
                        }
                        result.MovedCount = completed.Count(item =>
                            File.Exists(item.Destination));
                        result.Failures.Add(new FileOrganizationFailure
                        {
                            Detail = rollbackFailures.Count == 0
                                ? executionException.Message
                                : $"{executionException.Message} Rollback failed: {string.Join(" | ", rollbackFailures)}",
                        });
                    }

                    return HostApiResponse<FileOrganizationResult>.Success(result);
                }
                catch (Exception ex)
                {
                    return HostApiResponse<FileOrganizationResult>.Failure(
                        ApiErrorCode.Unknown,
                        ex.Message);
                }
            });
        }

        private static string NormalizeExistingRoot(string path)
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            if (!Directory.Exists(root))
                throw new DirectoryNotFoundException("Selected folder does not exist.");
            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException(
                    "Selected folder cannot be a reparse point.");
            return root;
        }

        private static FileOrganizationItem BuildOrganizationItem(
            string root,
            FileInfo file,
            ClassifyMode mode)
        {
            var category = mode switch
            {
                ClassifyMode.ByDate => file.LastWriteTime.ToString("yyyy-MM"),
                ClassifyMode.BySize => file.Length switch
                {
                    < 1024 * 1024 => "_small",
                    < 1024 * 1024 * 100 => "_medium",
                    _ => "_large",
                },
                _ => ExtensionCategory(file.Extension),
            };
            var destination = Path.GetFullPath(Path.Combine(
                root,
                OrganizedDirectoryName,
                category,
                file.Name));
            return new FileOrganizationItem
            {
                SourcePath = file.FullName,
                DestinationPath = destination,
                Category = category,
                Name = file.Name,
                Size = file.Length,
                HasConflict = File.Exists(destination),
            };
        }

        private static string ExtensionCategory(string extension)
        {
            var category = extension.TrimStart('.').ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(category))
                return "_no-extension";
            foreach (var character in Path.GetInvalidFileNameChars())
                category = category.Replace(character, '_');
            return category;
        }

        private static void ValidateFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)
                || !string.Equals(name, Path.GetFileName(name), StringComparison.Ordinal)
                || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || name.EndsWith(' ')
                || name.EndsWith('.'))
                throw new InvalidDataException(
                    "Rename destination name is invalid.");
        }

        private static void EnsureDirectoryIsNotReparsePoint(string path)
        {
            if (Directory.Exists(path)
                && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new UnauthorizedAccessException(
                    "Organization target cannot be a reparse point.");
            }
        }

        private string GetContext(string[] lines, int index, int contextLines)
        {
            var start = Math.Max(0, index - contextLines);
            var end = Math.Min(lines.Length - 1, index + contextLines);
            var contextBuilder = new StringBuilder();

            for (int i = start; i <= end; i++)
            {
                contextBuilder.AppendLine(lines[i]);
            }

            return contextBuilder.ToString();
        }
    }
}
