using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Capabilities
{
    public interface INetworkMonitorService
    {
        /// <summary>获取网络统计信息</summary>
        Task<HostApiResponse<NetworkStats>> GetNetworkStatsAsync();

        /// <summary>获取实时网速</summary>
        Task<HostApiResponse<NetworkSpeed>> GetNetworkSpeedAsync();

        /// <summary>获取网络接口列表</summary>
        Task<HostApiResponse<List<NetworkInterfaceInfo>>> GetNetworkInterfacesAsync();
    }

    public class NetworkStats
    {
        public long TotalBytesSent { get; set; }
        public long TotalBytesReceived { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class NetworkSpeed
    {
        public double UploadSpeed { get; set; }
        public double DownloadSpeed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class NetworkInterfaceInfo
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Type { get; set; } = "";
        public string Status { get; set; } = "";
        public long Speed { get; set; }
        public string MacAddress { get; set; } = "";
    }
}
