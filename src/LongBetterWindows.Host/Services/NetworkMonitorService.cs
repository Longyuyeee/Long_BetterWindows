using System.Net.NetworkInformation;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Services
{
    public class NetworkMonitorService : INetworkMonitorService
    {
        private readonly Dictionary<string, (long sent, long received, DateTime time)> _lastStats = new();

        public Task<HostApiResponse<NetworkStats>> GetNetworkStatsAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                        .Where(nic => nic.OperationalStatus == OperationalStatus.Up &&
                                     nic.NetworkInterfaceType != NetworkInterfaceType.Loopback);

                    long totalSent = 0;
                    long totalReceived = 0;

                    foreach (var nic in interfaces)
                    {
                        var stats = nic.GetIPv4Statistics();
                        totalSent += stats.BytesSent;
                        totalReceived += stats.BytesReceived;
                    }

                    var networkStats = new NetworkStats
                    {
                        TotalBytesSent = totalSent,
                        TotalBytesReceived = totalReceived,
                        Timestamp = DateTime.Now
                    };

                    return HostApiResponse<NetworkStats>.Success(networkStats);
                }
                catch (Exception ex)
                {
                    return HostApiResponse<NetworkStats>.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public Task<HostApiResponse<NetworkSpeed>> GetNetworkSpeedAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    var currentStats = GetCurrentStats();
                    var now = DateTime.Now;

                    double uploadSpeed = 0;
                    double downloadSpeed = 0;

                    if (_lastStats.TryGetValue("global", out var last))
                    {
                        var timeDiff = (now - last.time).TotalSeconds;
                        if (timeDiff > 0)
                        {
                            uploadSpeed = (currentStats.sent - last.sent) / timeDiff;
                            downloadSpeed = (currentStats.received - last.received) / timeDiff;
                        }
                    }

                    _lastStats["global"] = (currentStats.sent, currentStats.received, now);

                    var speed = new NetworkSpeed
                    {
                        UploadSpeed = uploadSpeed,
                        DownloadSpeed = downloadSpeed,
                        Timestamp = now
                    };

                    return HostApiResponse<NetworkSpeed>.Success(speed);
                }
                catch (Exception ex)
                {
                    return HostApiResponse<NetworkSpeed>.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public Task<HostApiResponse<List<NetworkInterfaceInfo>>> GetNetworkInterfacesAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                        .Select(nic => new NetworkInterfaceInfo
                        {
                            Name = nic.Name,
                            Description = nic.Description,
                            Type = nic.NetworkInterfaceType.ToString(),
                            Status = nic.OperationalStatus.ToString(),
                            Speed = nic.Speed,
                            MacAddress = nic.GetPhysicalAddress().ToString()
                        }).ToList();

                    return HostApiResponse<List<NetworkInterfaceInfo>>.Success(interfaces);
                }
                catch (Exception ex)
                {
                    return HostApiResponse<List<NetworkInterfaceInfo>>.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        private (long sent, long received) GetCurrentStats()
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic => nic.OperationalStatus == OperationalStatus.Up &&
                             nic.NetworkInterfaceType != NetworkInterfaceType.Loopback);

            long totalSent = 0;
            long totalReceived = 0;

            foreach (var nic in interfaces)
            {
                var stats = nic.GetIPv4Statistics();
                totalSent += stats.BytesSent;
                totalReceived += stats.BytesReceived;
            }

            return (totalSent, totalReceived);
        }
    }
}
