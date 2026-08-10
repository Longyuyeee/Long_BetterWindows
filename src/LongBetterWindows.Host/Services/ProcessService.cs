using System.Diagnostics;
using System.Globalization;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Services
{
    public class ProcessService : IProcessService
    {
        private readonly INetworkPortService _networkPortService;

        public ProcessService(INetworkPortService networkPortService)
        {
            _networkPortService = networkPortService;
        }

        public Task<HostApiResponse> StartAsync(string path, string? args = null)
        {
            return Task.Run(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = path,
                        Arguments = args ?? "",
                        UseShellExecute = true,
                    };
                    Process.Start(psi);
                    return HostApiResponse.Success();
                }
                catch (Exception ex) { return HostApiResponse.Failure(ApiErrorCode.Unknown, ex.Message); }
            });
        }

        public Task<HostApiResponse<List<ProcessInfo>>> GetRunningProcessesAsync(string? nameFilter = null)
        {
            return Task.Run(() =>
            {
                try
                {
                    var procs = Process.GetProcesses()
                        .Where(p =>
                        {
                            try { return nameFilter == null || p.ProcessName.Contains(nameFilter, StringComparison.OrdinalIgnoreCase); }
                            catch { return false; }
                        })
                        .Take(50)
                        .Select(p =>
                        {
                            try { return new ProcessInfo { Id = p.Id, Name = p.ProcessName, MainWindowTitle = p.MainWindowTitle }; }
                            catch { return new ProcessInfo { Id = p.Id, Name = p.ProcessName }; }
                        })
                        .ToList();
                    return HostApiResponse<List<ProcessInfo>>.Success(procs);
                }
                catch (Exception ex) { return HostApiResponse<List<ProcessInfo>>.Failure(ApiErrorCode.Unknown, ex.Message); }
            });
        }

        public Task<HostApiResponse> KillAsync(int processId)
        {
            return Task.Run(() =>
            {
                try
                {
                    var proc = Process.GetProcessById(processId);
                    proc.Kill();
                    return HostApiResponse.Success();
                }
                catch (Exception ex) { return HostApiResponse.Failure(ApiErrorCode.Unknown, ex.Message); }
            });
        }

        public Task<HostApiResponse> KillVerifiedAsync(
            int processId,
            string expectedName,
            string expectedIdentity)
        {
            return Task.Run(() =>
            {
                if (string.IsNullOrWhiteSpace(expectedName) ||
                    string.IsNullOrWhiteSpace(expectedIdentity))
                {
                    return HostApiResponse.Failure(
                        ApiErrorCode.InvalidArgument,
                        "Process identity is required.");
                }

                try
                {
                    using var process = Process.GetProcessById(processId);
                    var actualIdentity = process.StartTime
                        .ToUniversalTime()
                        .ToString("O", CultureInfo.InvariantCulture);
                    if (!string.Equals(process.ProcessName, expectedName, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(actualIdentity, expectedIdentity, StringComparison.Ordinal))
                    {
                        return HostApiResponse.Failure(
                            ApiErrorCode.InvalidArgument,
                            "Process identity changed. Refresh the port list and try again.");
                    }

                    if (IsProtectedProcess(processId))
                    {
                        return HostApiResponse.Failure(
                            ApiErrorCode.InvalidArgument,
                            "The host or a protected system process cannot be terminated.");
                    }

                    process.Kill();
                    if (!process.WaitForExit(5_000))
                    {
                        return HostApiResponse.Failure(
                            ApiErrorCode.Unknown,
                            "The process did not exit within the expected time.");
                    }
                    return HostApiResponse.Success();
                }
                catch (ArgumentException)
                {
                    return HostApiResponse.Failure(
                        ApiErrorCode.InvalidArgument,
                        "Process no longer exists.");
                }
                catch (Exception ex)
                {
                    return HostApiResponse.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public Task<HostApiResponse> KillPortOwnerVerifiedAsync(PortInfo expectedPort)
        {
            return Task.Run(async () =>
            {
                if (expectedPort == null ||
                    expectedPort.ProcessId <= 0 ||
                    expectedPort.LocalPort is < 1 or > 65535 ||
                    string.IsNullOrWhiteSpace(expectedPort.ProcessName) ||
                    string.IsNullOrWhiteSpace(expectedPort.ProcessIdentity) ||
                    string.IsNullOrWhiteSpace(expectedPort.Protocol) ||
                    string.IsNullOrWhiteSpace(expectedPort.State))
                {
                    return HostApiResponse.Failure(
                        ApiErrorCode.InvalidArgument,
                        "A complete port ownership snapshot is required.");
                }

                try
                {
                    using var process = Process.GetProcessById(expectedPort.ProcessId);
                    if (!MatchesIdentity(process, expectedPort.ProcessName, expectedPort.ProcessIdentity))
                    {
                        return HostApiResponse.Failure(
                            ApiErrorCode.InvalidArgument,
                            "Process identity changed. Refresh the port list and try again.");
                    }

                    if (IsProtectedProcess(expectedPort.ProcessId))
                    {
                        return HostApiResponse.Failure(
                            ApiErrorCode.InvalidArgument,
                            "The host or a protected system process cannot be terminated.");
                    }

                    var ownerResult = await _networkPortService.IsCurrentOwnerAsync(expectedPort);
                    if (!ownerResult.IsSuccess)
                    {
                        return HostApiResponse.Failure(
                            ownerResult.ErrorCode,
                            ownerResult.ErrorMessage);
                    }

                    if (ownerResult.Data != true)
                    {
                        return HostApiResponse.Failure(
                            ApiErrorCode.InvalidArgument,
                            "Port ownership changed. Refresh the port list and try again.");
                    }

                    process.Refresh();
                    if (process.HasExited ||
                        !MatchesIdentity(process, expectedPort.ProcessName, expectedPort.ProcessIdentity))
                    {
                        return HostApiResponse.Failure(
                            ApiErrorCode.InvalidArgument,
                            "Process identity changed. Refresh the port list and try again.");
                    }

                    process.Kill();
                    if (!process.WaitForExit(5_000))
                    {
                        return HostApiResponse.Failure(
                            ApiErrorCode.Unknown,
                            "The process did not exit within the expected time.");
                    }

                    return HostApiResponse.Success();
                }
                catch (ArgumentException)
                {
                    return HostApiResponse.Failure(
                        ApiErrorCode.InvalidArgument,
                        "Process no longer exists.");
                }
                catch (InvalidOperationException)
                {
                    return HostApiResponse.Failure(
                        ApiErrorCode.InvalidArgument,
                        "Process no longer exists.");
                }
                catch (Exception ex)
                {
                    return HostApiResponse.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        private static bool MatchesIdentity(
            Process process,
            string expectedName,
            string expectedIdentity)
        {
            var actualIdentity = process.StartTime
                .ToUniversalTime()
                .ToString("O", CultureInfo.InvariantCulture);
            return string.Equals(
                    process.ProcessName,
                    expectedName,
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(actualIdentity, expectedIdentity, StringComparison.Ordinal);
        }

        private static bool IsProtectedProcess(int processId) =>
            processId <= 4 || processId == Environment.ProcessId;
    }
}
