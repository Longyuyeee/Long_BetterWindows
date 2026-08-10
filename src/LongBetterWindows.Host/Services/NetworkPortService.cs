using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Services
{
    public class NetworkPortService : INetworkPortService
    {
        private const int AddressFamilyIpv4 = 2;
        private const int AddressFamilyIpv6 = 23;
        private const int TcpTableOwnerPidAll = 5;
        private const int UdpTableOwnerPid = 1;
        private const uint ErrorSuccess = 0;
        private const uint ErrorNotSupported = 50;
        private const uint ErrorInsufficientBuffer = 122;
        private const uint ErrorNoData = 232;

        public Task<HostApiResponse<List<PortInfo>>> GetTcpConnectionsAsync()
        {
            return ReadPortsAsync(() => ReadTcpRows()
                .Where(row => !string.Equals(row.State, "LISTENING", StringComparison.Ordinal))
                .ToList());
        }

        public Task<HostApiResponse<List<PortInfo>>> GetTcpListenersAsync()
        {
            return ReadPortsAsync(() => ReadTcpRows()
                .Where(row => string.Equals(row.State, "LISTENING", StringComparison.Ordinal))
                .ToList());
        }

        public Task<HostApiResponse<List<PortInfo>>> GetUdpEndpointsAsync()
        {
            return ReadPortsAsync(ReadUdpRows);
        }

        public async Task<HostApiResponse<PortInfo?>> FindPortOwnerAsync(
            int port,
            ProtocolType protocol)
        {
            if (port is < 1 or > 65535)
            {
                return HostApiResponse<PortInfo?>.Failure(
                    ApiErrorCode.InvalidArgument,
                    "Port must be between 1 and 65535.");
            }

            try
            {
                if (protocol == ProtocolType.Udp)
                {
                    var udpResult = await GetUdpEndpointsAsync();
                    if (!udpResult.IsSuccess)
                    {
                        return HostApiResponse<PortInfo?>.Failure(
                            udpResult.ErrorCode,
                            udpResult.ErrorMessage);
                    }

                    return HostApiResponse<PortInfo?>.Success(
                        udpResult.Data?.FirstOrDefault(item => item.LocalPort == port));
                }

                var listenerResult = await GetTcpListenersAsync();
                if (!listenerResult.IsSuccess)
                {
                    return HostApiResponse<PortInfo?>.Failure(
                        listenerResult.ErrorCode,
                        listenerResult.ErrorMessage);
                }

                var listener = listenerResult.Data?.FirstOrDefault(item => item.LocalPort == port);
                if (listener != null)
                {
                    return HostApiResponse<PortInfo?>.Success(listener);
                }

                var connectionResult = await GetTcpConnectionsAsync();
                if (!connectionResult.IsSuccess)
                {
                    return HostApiResponse<PortInfo?>.Failure(
                        connectionResult.ErrorCode,
                        connectionResult.ErrorMessage);
                }

                return HostApiResponse<PortInfo?>.Success(
                    connectionResult.Data?.FirstOrDefault(item => item.LocalPort == port));
            }
            catch (Exception ex)
            {
                return HostApiResponse<PortInfo?>.Failure(ApiErrorCode.Unknown, ex.Message);
            }
        }

        public async Task<HostApiResponse<bool>> IsPortInUseAsync(
            int port,
            ProtocolType protocol)
        {
            var result = await FindPortOwnerAsync(port, protocol);
            if (!result.IsSuccess)
            {
                return HostApiResponse<bool>.Failure(result.ErrorCode, result.ErrorMessage);
            }

            return HostApiResponse<bool>.Success(result.Data != null);
        }

        public async Task<HostApiResponse<PortSummary>> GetPortSummaryAsync()
        {
            try
            {
                var tcpConnections = await GetTcpConnectionsAsync();
                var tcpListeners = await GetTcpListenersAsync();
                var udpEndpoints = await GetUdpEndpointsAsync();
                var failed = new HostApiResponse[] { tcpConnections, tcpListeners, udpEndpoints }
                    .FirstOrDefault(result => !result.IsSuccess);
                if (failed != null)
                {
                    return HostApiResponse<PortSummary>.Failure(
                        failed.ErrorCode,
                        failed.ErrorMessage);
                }

                var summary = new PortSummary
                {
                    TotalTcpConnections = tcpConnections.Data?.Count ?? 0,
                    TotalTcpListeners = tcpListeners.Data?.Count ?? 0,
                    TotalUdpEndpoints = udpEndpoints.Data?.Count ?? 0,
                };

                var allPorts = new List<int>();
                allPorts.AddRange((tcpListeners.Data ?? []).Select(item => item.LocalPort));
                allPorts.AddRange((udpEndpoints.Data ?? []).Select(item => item.LocalPort));
                summary.CommonPorts = allPorts
                    .GroupBy(port => port)
                    .OrderByDescending(group => group.Count())
                    .Take(10)
                    .Select(group => group.Key)
                    .ToList();

                summary.ProcessPortCount = (tcpListeners.Data ?? [])
                    .Where(item => !string.IsNullOrEmpty(item.ProcessName))
                    .GroupBy(item => item.ProcessName, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Count(),
                        StringComparer.OrdinalIgnoreCase);

                return HostApiResponse<PortSummary>.Success(summary);
            }
            catch (Exception ex)
            {
                return HostApiResponse<PortSummary>.Failure(ApiErrorCode.Unknown, ex.Message);
            }
        }

        public Task<HostApiResponse<bool>> IsCurrentOwnerAsync(PortInfo expectedPort)
        {
            return Task.Run(() =>
            {
                if (expectedPort.ProcessId <= 0 || expectedPort.LocalPort is < 1 or > 65535)
                {
                    return HostApiResponse<bool>.Failure(
                        ApiErrorCode.InvalidArgument,
                        "A valid process and local port are required.");
                }

                try
                {
                    IReadOnlyList<PortInfo> currentPorts = expectedPort.Protocol.ToUpperInvariant() switch
                    {
                        "TCP" => ReadTcpRows(),
                        "UDP" => ReadUdpRows(),
                        _ => throw new ArgumentException("Protocol must be TCP or UDP."),
                    };

                    return HostApiResponse<bool>.Success(
                        currentPorts.Any(current => MatchesEndpoint(current, expectedPort)));
                }
                catch (ArgumentException ex)
                {
                    return HostApiResponse<bool>.Failure(ApiErrorCode.InvalidArgument, ex.Message);
                }
                catch (Exception ex)
                {
                    return HostApiResponse<bool>.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        private static Task<HostApiResponse<List<PortInfo>>> ReadPortsAsync(
            Func<List<PortInfo>> readRows)
        {
            return Task.Run(() =>
            {
                try
                {
                    return HostApiResponse<List<PortInfo>>.Success(
                        EnrichProcessInfo(readRows()));
                }
                catch (Exception ex)
                {
                    return HostApiResponse<List<PortInfo>>.Failure(
                        ApiErrorCode.Unknown,
                        ex.Message);
                }
            });
        }

        private static List<PortInfo> EnrichProcessInfo(List<PortInfo> ports)
        {
            var metadata = new Dictionary<int, ProcessMetadata>();
            foreach (var processId in ports
                .Select(item => item.ProcessId)
                .Where(processId => processId > 0)
                .Distinct())
            {
                metadata[processId] = ReadProcessMetadata(processId);
            }

            foreach (var port in ports)
            {
                if (!metadata.TryGetValue(port.ProcessId, out var process))
                {
                    continue;
                }

                port.ProcessName = process.Name;
                port.ProcessPath = process.Path;
                port.ProcessIdentity = process.Identity;
            }

            return ports;
        }

        private static ProcessMetadata ReadProcessMetadata(int processId)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                var name = process.ProcessName;
                var identity = process.StartTime
                    .ToUniversalTime()
                    .ToString("O", CultureInfo.InvariantCulture);
                var path = string.Empty;
                try
                {
                    path = process.MainModule?.FileName ?? string.Empty;
                }
                catch
                {
                    // Process name and start time are still sufficient for identity validation.
                }

                return new ProcessMetadata(name, path, identity);
            }
            catch
            {
                return ProcessMetadata.Empty;
            }
        }

        private static bool MatchesEndpoint(PortInfo current, PortInfo expected)
        {
            return current.ProcessId == expected.ProcessId
                && current.LocalPort == expected.LocalPort
                && current.RemotePort == expected.RemotePort
                && string.Equals(current.Protocol, expected.Protocol, StringComparison.OrdinalIgnoreCase)
                && string.Equals(current.State, expected.State, StringComparison.OrdinalIgnoreCase)
                && AddressesEqual(current.LocalAddress, expected.LocalAddress)
                && AddressesEqual(current.RemoteAddress, expected.RemoteAddress);
        }

        private static bool AddressesEqual(string current, string expected)
        {
            if (string.IsNullOrWhiteSpace(current) || string.IsNullOrWhiteSpace(expected))
            {
                return string.Equals(current, expected, StringComparison.OrdinalIgnoreCase);
            }

            return IPAddress.TryParse(current, out var currentAddress)
                && IPAddress.TryParse(expected, out var expectedAddress)
                ? currentAddress.Equals(expectedAddress)
                : string.Equals(current, expected, StringComparison.OrdinalIgnoreCase);
        }

        private static List<PortInfo> ReadTcpRows()
        {
            var rows = ReadTcpRows(AddressFamilyIpv4);
            rows.AddRange(ReadTcpRows(AddressFamilyIpv6));
            return rows;
        }

        private static List<PortInfo> ReadTcpRows(int addressFamily)
        {
            var size = 0;
            var result = GetExtendedTcpTableNative(
                IntPtr.Zero,
                ref size,
                true,
                addressFamily,
                TcpTableOwnerPidAll,
                0);
            if (IsUnavailableAddressFamily(result))
            {
                return [];
            }
            if (result != ErrorInsufficientBuffer && result != ErrorSuccess)
            {
                throw new Win32Exception((int)result);
            }

            if (size <= 0)
            {
                return [];
            }

            var table = Marshal.AllocHGlobal(size);
            try
            {
                result = GetExtendedTcpTableNative(
                    table,
                    ref size,
                    true,
                    addressFamily,
                    TcpTableOwnerPidAll,
                    0);
                if (IsUnavailableAddressFamily(result))
                {
                    return [];
                }
                if (result != ErrorSuccess)
                {
                    throw new Win32Exception((int)result);
                }

                return addressFamily == AddressFamilyIpv4
                    ? ParseTcpV4Rows(table)
                    : ParseTcpV6Rows(table);
            }
            finally
            {
                Marshal.FreeHGlobal(table);
            }
        }

        private static List<PortInfo> ReadUdpRows()
        {
            var rows = ReadUdpRows(AddressFamilyIpv4);
            rows.AddRange(ReadUdpRows(AddressFamilyIpv6));
            return rows;
        }

        private static List<PortInfo> ReadUdpRows(int addressFamily)
        {
            var size = 0;
            var result = GetExtendedUdpTableNative(
                IntPtr.Zero,
                ref size,
                true,
                addressFamily,
                UdpTableOwnerPid,
                0);
            if (IsUnavailableAddressFamily(result))
            {
                return [];
            }
            if (result != ErrorInsufficientBuffer && result != ErrorSuccess)
            {
                throw new Win32Exception((int)result);
            }

            if (size <= 0)
            {
                return [];
            }

            var table = Marshal.AllocHGlobal(size);
            try
            {
                result = GetExtendedUdpTableNative(
                    table,
                    ref size,
                    true,
                    addressFamily,
                    UdpTableOwnerPid,
                    0);
                if (IsUnavailableAddressFamily(result))
                {
                    return [];
                }
                if (result != ErrorSuccess)
                {
                    throw new Win32Exception((int)result);
                }

                return addressFamily == AddressFamilyIpv4
                    ? ParseUdpV4Rows(table)
                    : ParseUdpV6Rows(table);
            }
            finally
            {
                Marshal.FreeHGlobal(table);
            }
        }

        private static List<PortInfo> ParseTcpV4Rows(IntPtr table)
        {
            var rows = new List<PortInfo>();
            ReadRows<MibTcpRowOwnerPid>(table, row => rows.Add(new PortInfo
            {
                LocalAddress = Ipv4Address(row.LocalAddress),
                LocalPort = Port(row.LocalPort),
                RemoteAddress = Ipv4Address(row.RemoteAddress),
                RemotePort = Port(row.RemotePort),
                Protocol = "TCP",
                State = TcpStateName(row.State),
                ProcessId = unchecked((int)row.ProcessId),
            }));
            return rows;
        }

        private static List<PortInfo> ParseTcpV6Rows(IntPtr table)
        {
            var rows = new List<PortInfo>();
            ReadRows<MibTcp6RowOwnerPid>(table, row => rows.Add(new PortInfo
            {
                LocalAddress = new IPAddress(row.LocalAddress, row.LocalScopeId).ToString(),
                LocalPort = Port(row.LocalPort),
                RemoteAddress = new IPAddress(row.RemoteAddress, row.RemoteScopeId).ToString(),
                RemotePort = Port(row.RemotePort),
                Protocol = "TCP",
                State = TcpStateName(row.State),
                ProcessId = unchecked((int)row.ProcessId),
            }));
            return rows;
        }

        private static List<PortInfo> ParseUdpV4Rows(IntPtr table)
        {
            var rows = new List<PortInfo>();
            ReadRows<MibUdpRowOwnerPid>(table, row => rows.Add(new PortInfo
            {
                LocalAddress = Ipv4Address(row.LocalAddress),
                LocalPort = Port(row.LocalPort),
                Protocol = "UDP",
                State = "LISTENING",
                ProcessId = unchecked((int)row.ProcessId),
            }));
            return rows;
        }

        private static List<PortInfo> ParseUdpV6Rows(IntPtr table)
        {
            var rows = new List<PortInfo>();
            ReadRows<MibUdp6RowOwnerPid>(table, row => rows.Add(new PortInfo
            {
                LocalAddress = new IPAddress(row.LocalAddress, row.LocalScopeId).ToString(),
                LocalPort = Port(row.LocalPort),
                Protocol = "UDP",
                State = "LISTENING",
                ProcessId = unchecked((int)row.ProcessId),
            }));
            return rows;
        }

        private static void ReadRows<TRow>(IntPtr table, Action<TRow> addRow)
            where TRow : struct
        {
            var count = Marshal.ReadInt32(table);
            var rowSize = Marshal.SizeOf<TRow>();
            var rowPointer = IntPtr.Add(table, sizeof(int));
            for (var index = 0; index < count; index++)
            {
                addRow(Marshal.PtrToStructure<TRow>(rowPointer));
                rowPointer = IntPtr.Add(rowPointer, rowSize);
            }
        }

        private static string Ipv4Address(uint value) =>
            new IPAddress(BitConverter.GetBytes(value)).ToString();

        private static int Port(uint networkOrderPort) =>
            (ushort)IPAddress.NetworkToHostOrder((short)(networkOrderPort & 0xffff));

        private static string TcpStateName(uint value)
        {
            var state = Enum.IsDefined(typeof(TcpState), (int)value)
                ? ((TcpState)value).ToString().ToUpperInvariant()
                : value.ToString(CultureInfo.InvariantCulture);
            return state == "LISTEN" ? "LISTENING" : state;
        }

        private static bool IsUnavailableAddressFamily(uint error) =>
            error is ErrorNotSupported or ErrorNoData;

        [DllImport("iphlpapi.dll", EntryPoint = "GetExtendedTcpTable", SetLastError = true)]
        private static extern uint GetExtendedTcpTableNative(
            IntPtr table,
            ref int size,
            bool sort,
            int addressFamily,
            int tableClass,
            int reserved);

        [DllImport("iphlpapi.dll", EntryPoint = "GetExtendedUdpTable", SetLastError = true)]
        private static extern uint GetExtendedUdpTableNative(
            IntPtr table,
            ref int size,
            bool sort,
            int addressFamily,
            int tableClass,
            int reserved);

        [StructLayout(LayoutKind.Sequential)]
        private struct MibTcpRowOwnerPid
        {
            public uint State;
            public uint LocalAddress;
            public uint LocalPort;
            public uint RemoteAddress;
            public uint RemotePort;
            public uint ProcessId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MibTcp6RowOwnerPid
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            public byte[] LocalAddress;
            public uint LocalScopeId;
            public uint LocalPort;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            public byte[] RemoteAddress;
            public uint RemoteScopeId;
            public uint RemotePort;
            public uint State;
            public uint ProcessId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MibUdpRowOwnerPid
        {
            public uint LocalAddress;
            public uint LocalPort;
            public uint ProcessId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MibUdp6RowOwnerPid
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            public byte[] LocalAddress;
            public uint LocalScopeId;
            public uint LocalPort;
            public uint ProcessId;
        }

        private sealed record ProcessMetadata(string Name, string Path, string Identity)
        {
            public static ProcessMetadata Empty { get; } = new(string.Empty, string.Empty, string.Empty);
        }
    }
}
