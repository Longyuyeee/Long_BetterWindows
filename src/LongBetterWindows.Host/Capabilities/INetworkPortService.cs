using LongBetterWindows.Host.Contracts;
using System.Net.NetworkInformation;

namespace LongBetterWindows.Host.Capabilities
{
    public interface INetworkPortService
    {
        /// <summary>获取所有活动的 TCP 连接</summary>
        Task<HostApiResponse<List<PortInfo>>> GetTcpConnectionsAsync();

        /// <summary>获取所有监听中的 TCP 端口</summary>
        Task<HostApiResponse<List<PortInfo>>> GetTcpListenersAsync();

        /// <summary>获取所有 UDP 端点</summary>
        Task<HostApiResponse<List<PortInfo>>> GetUdpEndpointsAsync();

        /// <summary>查找占用指定端口的进程</summary>
        Task<HostApiResponse<PortInfo?>> FindPortOwnerAsync(int port, ProtocolType protocol);

        /// <summary>检查端口是否被占用</summary>
        Task<HostApiResponse<bool>> IsPortInUseAsync(int port, ProtocolType protocol);

        /// <summary>获取所有端口占用情况汇总</summary>
        Task<HostApiResponse<PortSummary>> GetPortSummaryAsync();
    }

    public class PortInfo
    {
        public int LocalPort { get; set; }
        public string LocalAddress { get; set; } = "";
        public int RemotePort { get; set; }
        public string RemoteAddress { get; set; } = "";
        public string Protocol { get; set; } = "";
        public string State { get; set; } = "";
        public int ProcessId { get; set; }
        public string ProcessName { get; set; } = "";
        public string ProcessPath { get; set; } = "";
        public string ProcessIdentity { get; set; } = "";
    }

    public class PortSummary
    {
        public int TotalTcpConnections { get; set; }
        public int TotalTcpListeners { get; set; }
        public int TotalUdpEndpoints { get; set; }
        public List<int> CommonPorts { get; set; } = new();
        public Dictionary<string, int> ProcessPortCount { get; set; } = new();
    }

    public enum ProtocolType
    {
        Tcp,
        Udp
    }
}
