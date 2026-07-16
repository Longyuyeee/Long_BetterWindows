using System.IO;
using System.Security.Cryptography;
using System.Text;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Services
{
    public class FileSystemService : IFileSystemService
    {
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
                try
                {
                    int count = 0;
                    foreach (var op in operations)
                    {
                        try
                        {
                            var dir = Path.GetDirectoryName(op.OldPath);
                            var newPath = Path.Combine(dir!, op.NewName);
                            File.Move(op.OldPath, newPath);
                            count++;
                        }
                        catch { }
                    }

                    return HostApiResponse<int>.Success(count);
                }
                catch (Exception ex)
                {
                    return HostApiResponse<int>.Failure(ApiErrorCode.Unknown, ex.Message);
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
