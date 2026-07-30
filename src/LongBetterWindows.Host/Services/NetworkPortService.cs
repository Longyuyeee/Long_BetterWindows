using System.Diagnostics;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Services
{
    public class NetworkPortService : INetworkPortService
    {
        public Task<HostApiResponse<List<PortInfo>>> GetTcpConnectionsAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    var connections = IPGlobalProperties.GetIPGlobalProperties()
                        .GetActiveTcpConnections();

                    var portInfos = connections.Select(conn => new PortInfo
                    {
                        LocalPort = conn.LocalEndPoint.Port,
                        LocalAddress = conn.LocalEndPoint.Address.ToString(),
                        RemotePort = conn.RemoteEndPoint.Port,
                        RemoteAddress = conn.RemoteEndPoint.Address.ToString(),
                        Protocol = "TCP",
                        State = conn.State.ToString(),
                        ProcessId = GetProcessIdForPort(conn.LocalEndPoint.Port, isUdp: false),
                    }).ToList();

                    // 填充进程信息
                    foreach (var info in portInfos)
                    {
                        if (info.ProcessId > 0)
                        {
                            try
                            {
                                var process = Process.GetProcessById(info.ProcessId);
                                info.ProcessName = process.ProcessName;
                                info.ProcessPath = process.MainModule?.FileName ?? "";
                                info.ProcessIdentity = GetProcessIdentity(process);
                            }
                            catch { }
                        }
                    }

                    return HostApiResponse<List<PortInfo>>.Success(portInfos);
                }
                catch (Exception ex)
                {
                    return HostApiResponse<List<PortInfo>>.Failure(
                        ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public Task<HostApiResponse<List<PortInfo>>> GetTcpListenersAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    var listeners = IPGlobalProperties.GetIPGlobalProperties()
                        .GetActiveTcpListeners();

                    var portInfos = listeners.Select(endpoint => new PortInfo
                    {
                        LocalPort = endpoint.Port,
                        LocalAddress = endpoint.Address.ToString(),
                        Protocol = "TCP",
                        State = "LISTENING",
                        ProcessId = GetProcessIdForPort(endpoint.Port, isUdp: false),
                    }).ToList();

                    // 填充进程信息
                    foreach (var info in portInfos)
                    {
                        if (info.ProcessId > 0)
                        {
                            try
                            {
                                var process = Process.GetProcessById(info.ProcessId);
                                info.ProcessName = process.ProcessName;
                                info.ProcessPath = process.MainModule?.FileName ?? "";
                                info.ProcessIdentity = GetProcessIdentity(process);
                            }
                            catch { }
                        }
                    }

                    return HostApiResponse<List<PortInfo>>.Success(portInfos);
                }
                catch (Exception ex)
                {
                    return HostApiResponse<List<PortInfo>>.Failure(
                        ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public Task<HostApiResponse<List<PortInfo>>> GetUdpEndpointsAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    var endpoints = IPGlobalProperties.GetIPGlobalProperties()
                        .GetActiveUdpListeners();

                    var portInfos = endpoints.Select(endpoint => new PortInfo
                    {
                        LocalPort = endpoint.Port,
                        LocalAddress = endpoint.Address.ToString(),
                        Protocol = "UDP",
                        State = "LISTENING",
                        ProcessId = GetProcessIdForPort(endpoint.Port, isUdp: true),
                    }).ToList();

                    // 填充进程信息
                    foreach (var info in portInfos)
                    {
                        if (info.ProcessId > 0)
                        {
                            try
                            {
                                var process = Process.GetProcessById(info.ProcessId);
                                info.ProcessName = process.ProcessName;
                                info.ProcessPath = process.MainModule?.FileName ?? "";
                                info.ProcessIdentity = GetProcessIdentity(process);
                            }
                            catch { }
                        }
                    }

                    return HostApiResponse<List<PortInfo>>.Success(portInfos);
                }
                catch (Exception ex)
                {
                    return HostApiResponse<List<PortInfo>>.Failure(
                        ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public async Task<HostApiResponse<PortInfo?>> FindPortOwnerAsync(int port, Capabilities.ProtocolType protocol)
        {
            try
            {
                List<PortInfo> ports;
                if (protocol == Capabilities.ProtocolType.Tcp)
                {
                    var listenersResult = await GetTcpListenersAsync();
                    if (!listenersResult.IsSuccess) return HostApiResponse<PortInfo?>.Failure(listenersResult.ErrorCode, listenersResult.ErrorMessage);
                    ports = listenersResult.Data ?? new();
                }
                else
                {
                    var udpResult = await GetUdpEndpointsAsync();
                    if (!udpResult.IsSuccess) return HostApiResponse<PortInfo?>.Failure(udpResult.ErrorCode, udpResult.ErrorMessage);
                    ports = udpResult.Data ?? new();
                }

                var owner = ports.FirstOrDefault(p => p.LocalPort == port);
                return HostApiResponse<PortInfo?>.Success(owner);
            }
            catch (Exception ex)
            {
                return HostApiResponse<PortInfo?>.Failure(ApiErrorCode.Unknown, ex.Message);
            }
        }

        public async Task<HostApiResponse<bool>> IsPortInUseAsync(int port, Capabilities.ProtocolType protocol)
        {
            var result = await FindPortOwnerAsync(port, protocol);
            if (!result.IsSuccess) return HostApiResponse<bool>.Failure(result.ErrorCode, result.ErrorMessage);
            return HostApiResponse<bool>.Success(result.Data != null);
        }

        public async Task<HostApiResponse<PortSummary>> GetPortSummaryAsync()
        {
            try
            {
                var tcpConnResult = await GetTcpConnectionsAsync();
                var tcpListenResult = await GetTcpListenersAsync();
                var udpResult = await GetUdpEndpointsAsync();

                var summary = new PortSummary
                {
                    TotalTcpConnections = tcpConnResult.Data?.Count ?? 0,
                    TotalTcpListeners = tcpListenResult.Data?.Count ?? 0,
                    TotalUdpEndpoints = udpResult.Data?.Count ?? 0,
                };

                // 统计常用端口
                var allPorts = new List<int>();
                if (tcpListenResult.Data != null)
                    allPorts.AddRange(tcpListenResult.Data.Select(p => p.LocalPort));
                if (udpResult.Data != null)
                    allPorts.AddRange(udpResult.Data.Select(p => p.LocalPort));

                summary.CommonPorts = allPorts.GroupBy(p => p)
                    .OrderByDescending(g => g.Count())
                    .Take(10)
                    .Select(g => g.Key)
                    .ToList();

                // 统计进程端口数
                var processCount = new Dictionary<string, int>();
                foreach (var port in tcpListenResult.Data ?? new())
                {
                    if (!string.IsNullOrEmpty(port.ProcessName))
                    {
                        processCount[port.ProcessName] = processCount.GetValueOrDefault(port.ProcessName) + 1;
                    }
                }

                summary.ProcessPortCount = processCount;
                return HostApiResponse<PortSummary>.Success(summary);
            }
            catch (Exception ex)
            {
                return HostApiResponse<PortSummary>.Failure(ApiErrorCode.Unknown, ex.Message);
            }
        }

        private int GetProcessIdForPort(int port, bool isUdp)
        {
            try
            {
                if (isUdp)
                {
                    var table = GetExtendedUdpTable();
                    var row = table.FirstOrDefault(r => r.LocalPort == port);
                    return row.ProcessId;
                }
                else
                {
                    var table = GetExtendedTcpTable();
                    var row = table.FirstOrDefault(r => r.LocalPort == port);
                    return row.ProcessId;
                }
            }
            catch
            {
                return 0;
            }
        }

        private static string GetProcessIdentity(Process process)
        {
            return process.StartTime
                .ToUniversalTime()
                .ToString("O", CultureInfo.InvariantCulture);
        }

        #region Win32 API for Port-Process Mapping

        [StructLayout(LayoutKind.Sequential)]
        private struct MIB_TCPROW_OWNER_PID
        {
            public uint State;
            public uint LocalAddr;
            public uint LocalPort;
            public uint RemoteAddr;
            public uint RemotePort;
            public int ProcessId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MIB_UDPROW_OWNER_PID
        {
            public uint LocalAddr;
            public uint LocalPort;
            public int ProcessId;
        }

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(IntPtr tcpTable, ref int size, bool sort,
            int ipVersion, int tableClass, int reserved);

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedUdpTable(IntPtr udpTable, ref int size, bool sort,
            int ipVersion, int tableClass, int reserved);

        private List<(int LocalPort, int ProcessId)> GetExtendedTcpTable()
        {
            var result = new List<(int, int)>();
            int size = 0;
            GetExtendedTcpTable(IntPtr.Zero, ref size, true, 2, 5, 0);

            IntPtr tcpTable = Marshal.AllocHGlobal(size);
            try
            {
                if (GetExtendedTcpTable(tcpTable, ref size, true, 2, 5, 0) == 0)
                {
                    int numEntries = Marshal.ReadInt32(tcpTable);
                    IntPtr rowPtr = (IntPtr)((long)tcpTable + 4);

                    for (int i = 0; i < numEntries; i++)
                    {
                        var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(rowPtr);
                        int port = (int)((row.LocalPort >> 8) | ((row.LocalPort & 0xFF) << 8));
                        result.Add((port, row.ProcessId));
                        rowPtr = (IntPtr)((long)rowPtr + Marshal.SizeOf<MIB_TCPROW_OWNER_PID>());
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(tcpTable);
            }

            return result;
        }

        private List<(int LocalPort, int ProcessId)> GetExtendedUdpTable()
        {
            var result = new List<(int, int)>();
            int size = 0;
            GetExtendedUdpTable(IntPtr.Zero, ref size, true, 2, 1, 0);

            IntPtr udpTable = Marshal.AllocHGlobal(size);
            try
            {
                if (GetExtendedUdpTable(udpTable, ref size, true, 2, 1, 0) == 0)
                {
                    int numEntries = Marshal.ReadInt32(udpTable);
                    IntPtr rowPtr = (IntPtr)((long)udpTable + 4);

                    for (int i = 0; i < numEntries; i++)
                    {
                        var row = Marshal.PtrToStructure<MIB_UDPROW_OWNER_PID>(rowPtr);
                        int port = (int)((row.LocalPort >> 8) | ((row.LocalPort & 0xFF) << 8));
                        result.Add((port, row.ProcessId));
                        rowPtr = (IntPtr)((long)rowPtr + Marshal.SizeOf<MIB_UDPROW_OWNER_PID>());
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(udpTable);
            }

            return result;
        }

        #endregion
    }
}
