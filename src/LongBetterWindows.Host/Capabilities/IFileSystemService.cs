using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Capabilities
{
    public interface IFileSystemService
    {
        /// <summary>递归遍历目录，返回文件列表</summary>
        Task<HostApiResponse<List<FileItem>>> EnumerateFilesAsync(string path, string searchPattern = "*.*", bool recursive = true);

        /// <summary>计算文件 MD5 哈希值</summary>
        Task<HostApiResponse<string>> ComputeFileHashAsync(string filePath);

        /// <summary>获取文件元数据</summary>
        Task<HostApiResponse<FileMetadata>> GetFileMetadataAsync(string filePath);

        /// <summary>查找重复文件（按哈希值）</summary>
        Task<HostApiResponse<List<DuplicateFileGroup>>> FindDuplicateFilesAsync(string path);

        /// <summary>批量重命名文件</summary>
        Task<HostApiResponse<int>> BatchRenameAsync(List<RenameOperation> operations);

        /// <summary>按类型/日期分类文件</summary>
        Task<HostApiResponse<Dictionary<string, List<string>>>> ClassifyFilesAsync(string path, ClassifyMode mode);

        /// <summary>查找大文件（超过指定大小）</summary>
        Task<HostApiResponse<List<FileItem>>> FindLargeFilesAsync(string path, long minSizeBytes);

        /// <summary>搜索文件内容（全文搜索）</summary>
        Task<HostApiResponse<List<SearchResult>>> SearchFileContentAsync(string path, string keyword, List<string> extensions);
    }

    public class FileItem
    {
        public string FullPath { get; set; } = "";
        public string Name { get; set; } = "";
        public long Size { get; set; }
        public DateTime CreatedTime { get; set; }
        public DateTime ModifiedTime { get; set; }
        public string Extension { get; set; } = "";
    }

    public class FileMetadata
    {
        public string FullPath { get; set; } = "";
        public long Size { get; set; }
        public DateTime CreatedTime { get; set; }
        public DateTime ModifiedTime { get; set; }
        public DateTime AccessedTime { get; set; }
        public string Extension { get; set; } = "";
        public bool IsReadOnly { get; set; }
        public bool IsHidden { get; set; }
        public string Hash { get; set; } = "";
    }

    public class DuplicateFileGroup
    {
        public string Hash { get; set; } = "";
        public long Size { get; set; }
        public List<string> FilePaths { get; set; } = new();
    }

    public class RenameOperation
    {
        public string OldPath { get; set; } = "";
        public string NewName { get; set; } = "";
    }

    public class SearchResult
    {
        public string FilePath { get; set; } = "";
        public int LineNumber { get; set; }
        public string MatchedLine { get; set; } = "";
        public string Context { get; set; } = "";
    }

    public enum ClassifyMode
    {
        ByExtension,    // 按扩展名分类
        ByDate,         // 按日期分类（年-月）
        BySize          // 按大小分类（小/中/大）
    }
}
