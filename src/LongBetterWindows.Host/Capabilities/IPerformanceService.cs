using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Capabilities
{
    public interface IPerformanceService
    {
        /// <summary>获取 CPU 使用率 (0-100)</summary>
        Task<HostApiResponse<double>> GetCpuUsageAsync();

        /// <summary>获取内存信息</summary>
        Task<HostApiResponse<MemoryInfo>> GetMemoryInfoAsync();

        /// <summary>获取磁盘信息</summary>
        Task<HostApiResponse<List<DiskInfo>>> GetDiskInfoAsync();

        /// <summary>获取系统信息</summary>
        Task<HostApiResponse<SystemInfo>> GetSystemInfoAsync();

        /// <summary>获取进程资源占用 Top N</summary>
        Task<HostApiResponse<List<ProcessResourceInfo>>> GetTopProcessesByCpuAsync(int count = 10);

        /// <summary>获取进程内存占用 Top N</summary>
        Task<HostApiResponse<List<ProcessResourceInfo>>> GetTopProcessesByMemoryAsync(int count = 10);
    }

    public class MemoryInfo
    {
        public long TotalPhysicalMemory { get; set; }
        public long AvailablePhysicalMemory { get; set; }
        public long UsedPhysicalMemory { get; set; }
        public double UsagePercentage { get; set; }
    }

    public class DiskInfo
    {
        public string Name { get; set; } = "";
        public string DriveType { get; set; } = "";
        public long TotalSize { get; set; }
        public long FreeSpace { get; set; }
        public long UsedSpace { get; set; }
        public double UsagePercentage { get; set; }
    }

    public class SystemInfo
    {
        public string OsName { get; set; } = "";
        public string OsVersion { get; set; } = "";
        public string MachineName { get; set; } = "";
        public string ProcessorName { get; set; } = "";
        public int ProcessorCount { get; set; }
        public long TotalRam { get; set; }
        public string UserName { get; set; } = "";
        public TimeSpan Uptime { get; set; }
    }

    public class ProcessResourceInfo
    {
        public int ProcessId { get; set; }
        public string ProcessName { get; set; } = "";
        public double CpuUsage { get; set; }
        public long MemoryUsage { get; set; }
        public int ThreadCount { get; set; }
    }
}
