using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Services
{
    public class PerformanceService : IPerformanceService
    {
        private readonly Lazy<PerformanceCounter?> _cpuCounter = new(CreateCpuCounter);
        private readonly Dictionary<int, (double lastCpu, DateTime lastTime)> _processCache = new();

        private static PerformanceCounter? CreateCpuCounter()
        {
            try
            {
                var counter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                counter.NextValue();
                return counter;
            }
            catch
            {
                return null;
            }
        }

        public Task<HostApiResponse<double>> GetCpuUsageAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    var cpuCounter = _cpuCounter.Value;
                    if (cpuCounter == null)
                        return HostApiResponse<double>.Failure(ApiErrorCode.Unknown, "无法访问 CPU 性能计数器");

                    var usage = cpuCounter.NextValue();
                    return HostApiResponse<double>.Success(Math.Round(usage, 2));
                }
                catch (Exception ex)
                {
                    return HostApiResponse<double>.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public Task<HostApiResponse<MemoryInfo>> GetMemoryInfoAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    var memStatus = new MEMORYSTATUSEX();
                    if (!GlobalMemoryStatusEx(memStatus))
                        throw new Exception("无法获取内存信息");

                    var info = new MemoryInfo
                    {
                        TotalPhysicalMemory = (long)memStatus.ullTotalPhys,
                        AvailablePhysicalMemory = (long)memStatus.ullAvailPhys,
                        UsedPhysicalMemory = (long)(memStatus.ullTotalPhys - memStatus.ullAvailPhys),
                        UsagePercentage = Math.Round((double)(memStatus.ullTotalPhys - memStatus.ullAvailPhys) / memStatus.ullTotalPhys * 100, 2)
                    };

                    return HostApiResponse<MemoryInfo>.Success(info);
                }
                catch (Exception ex)
                {
                    return HostApiResponse<MemoryInfo>.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public Task<HostApiResponse<List<DiskInfo>>> GetDiskInfoAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    var disks = DriveInfo.GetDrives()
                        .Where(d => d.IsReady)
                        .Select(d => new DiskInfo
                        {
                            Name = d.Name,
                            DriveType = d.DriveType.ToString(),
                            TotalSize = d.TotalSize,
                            FreeSpace = d.AvailableFreeSpace,
                            UsedSpace = d.TotalSize - d.AvailableFreeSpace,
                            UsagePercentage = Math.Round((double)(d.TotalSize - d.AvailableFreeSpace) / d.TotalSize * 100, 2)
                        }).ToList();

                    return HostApiResponse<List<DiskInfo>>.Success(disks);
                }
                catch (Exception ex)
                {
                    return HostApiResponse<List<DiskInfo>>.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public Task<HostApiResponse<SystemInfo>> GetSystemInfoAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    var info = new SystemInfo
                    {
                        OsName = RuntimeInformation.OSDescription,
                        OsVersion = Environment.OSVersion.VersionString,
                        MachineName = Environment.MachineName,
                        ProcessorName = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "Unknown",
                        ProcessorCount = Environment.ProcessorCount,
                        TotalRam = GetTotalPhysicalMemory(),
                        UserName = Environment.UserName,
                        Uptime = TimeSpan.FromMilliseconds(Environment.TickCount64)
                    };

                    return HostApiResponse<SystemInfo>.Success(info);
                }
                catch (Exception ex)
                {
                    return HostApiResponse<SystemInfo>.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public Task<HostApiResponse<List<ProcessResourceInfo>>> GetTopProcessesByCpuAsync(int count = 10)
        {
            return Task.Run(() =>
            {
                try
                {
                    var processes = Process.GetProcesses()
                        .Select(p =>
                        {
                            try
                            {
                                return new ProcessResourceInfo
                                {
                                    ProcessId = p.Id,
                                    ProcessName = p.ProcessName,
                                    CpuUsage = GetProcessCpuUsage(p),
                                    MemoryUsage = p.WorkingSet64,
                                    ThreadCount = p.Threads.Count
                                };
                            }
                            catch
                            {
                                return null;
                            }
                        })
                        .Where(p => p != null)
                        .OrderByDescending(p => p!.CpuUsage)
                        .Take(count)
                        .ToList();

                    return HostApiResponse<List<ProcessResourceInfo>>.Success(processes!);
                }
                catch (Exception ex)
                {
                    return HostApiResponse<List<ProcessResourceInfo>>.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public Task<HostApiResponse<List<ProcessResourceInfo>>> GetTopProcessesByMemoryAsync(int count = 10)
        {
            return Task.Run(() =>
            {
                try
                {
                    var processes = Process.GetProcesses()
                        .Select(p =>
                        {
                            try
                            {
                                return new ProcessResourceInfo
                                {
                                    ProcessId = p.Id,
                                    ProcessName = p.ProcessName,
                                    CpuUsage = 0,
                                    MemoryUsage = p.WorkingSet64,
                                    ThreadCount = p.Threads.Count
                                };
                            }
                            catch
                            {
                                return null;
                            }
                        })
                        .Where(p => p != null)
                        .OrderByDescending(p => p!.MemoryUsage)
                        .Take(count)
                        .ToList();

                    return HostApiResponse<List<ProcessResourceInfo>>.Success(processes!);
                }
                catch (Exception ex)
                {
                    return HostApiResponse<List<ProcessResourceInfo>>.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        private double GetProcessCpuUsage(Process process)
        {
            try
            {
                var now = DateTime.UtcNow;
                var currentCpu = process.TotalProcessorTime.TotalMilliseconds;

                if (_processCache.TryGetValue(process.Id, out var cached))
                {
                    var timeDiff = (now - cached.lastTime).TotalMilliseconds;
                    var cpuDiff = currentCpu - cached.lastCpu;
                    var usage = (cpuDiff / timeDiff) / Environment.ProcessorCount * 100;
                    _processCache[process.Id] = (currentCpu, now);
                    return Math.Round(Math.Min(usage, 100), 2);
                }

                _processCache[process.Id] = (currentCpu, now);
                return 0;
            }
            catch
            {
                return 0;
            }
        }

        private long GetTotalPhysicalMemory()
        {
            var memStatus = new MEMORYSTATUSEX();
            GlobalMemoryStatusEx(memStatus);
            return (long)memStatus.ullTotalPhys;
        }

        #region Win32 API

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MEMORYSTATUSEX
        {
            public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        #endregion
    }
}
