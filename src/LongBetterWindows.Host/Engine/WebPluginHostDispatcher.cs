using System.IO;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using Serilog;

namespace LongBetterWindows.Host.Engine
{
    internal sealed class WebPluginHostDispatcher : IDisposable
    {
        private readonly string _pluginId;
        private readonly IHostApi _host;
        private readonly Action<string> _postMessage;
        private readonly SemaphoreSlim _clipboardGate = new(1, 1);
        private readonly object _clipboardStateLock = new();
        private Task<HostApiResponse>? _clipboardAcquireTask;
        private bool _clipboardSubscribed;
        private bool _disposed;

        internal WebPluginHostDispatcher(
            string pluginId,
            IHostApi host,
            Action<string> postMessage)
        {
            _pluginId = pluginId;
            _host = host;
            _postMessage = postMessage;
        }

        internal async Task<object?> DispatchAsync(string method, object?[] args)
        {
            var h = _host;
            return await (method switch
            {
                // === long.app ===
                "app.openUrl" => Ok(h.ShellExecute.OpenUrlAsync(WebPluginArguments.GetString(args, 0))),
                "app.openFolder" => Ok(h.ShellExecute.OpenFolderAsync(WebPluginArguments.GetString(args, 0))),
                "app.openWithDefault" => Ok(h.ShellExecute.OpenWithDefaultAsync(WebPluginArguments.GetString(args, 0))),
                "app.showNotification" => Task.FromResult<object?>(UIToast(WebPluginArguments.GetString(args, 0) + "\n" + WebPluginArguments.GetString(args, 1))),
                "app.getVersion" => Task.FromResult<object?>(new { version = App.ProductVersion }),
                "app.log" => Task.FromResult<object?>(PluginLog(args)),

                // === long.clipboard ===
                "clipboard.getText" => Ok(h.Clipboard.GetTextAsync()),
                "clipboard.setText" => Ok(h.Clipboard.SetTextAsync(WebPluginArguments.GetString(args, 0))),
                "clipboard.clear" => Ok(h.Clipboard.ClearAsync()),
                "clipboard.startMonitoring" => ClipboardStartMonitoringAsync(),
                "clipboard.stopMonitoring" => ClipboardStopMonitoringAsync(),

                // === long.shell ===
                "shell.getActiveFolder" => Ok(h.ShellSelection.GetActiveExplorerFolderPathAsync()),
                "shell.getSelectedItems" => OkList(h.ShellSelection.GetSelectedItemsAsync()),
                "shell.getItemScreenRect" => Ok(h.ShellSelection.GetSelectedItemScreenRectAsync()),

                // === long.shell execute ===
                "shell.openUrl" => Ok(h.ShellExecute.OpenUrlAsync(WebPluginArguments.GetString(args, 0))),
                "shell.openFolder" => Ok(h.ShellExecute.OpenFolderAsync(WebPluginArguments.GetString(args, 0))),
                "shell.openWithDefault" => Ok(h.ShellExecute.OpenWithDefaultAsync(WebPluginArguments.GetString(args, 0))),

                // === long.http ===
                "http.get" => Ok(h.Http.GetAsync(WebPluginArguments.GetString(args, 0), WebPluginArguments.GetHeaders(args, 1))),
                "http.post" => HttpPost(args),
                "http.download" => HttpDownload(args),

                // === long.fs.ads ===
                "fs.ads.read" => Ok(h.ADS.ReadAsync(WebPluginArguments.GetString(args, 0), WebPluginArguments.GetString(args, 1, "long_note"))),
                "fs.ads.write" => Ok(h.ADS.WriteAsync(WebPluginArguments.GetString(args, 0), WebPluginArguments.GetString(args, 1, "long_note"), WebPluginArguments.GetString(args, 2))),
                "fs.ads.delete" => Ok(h.ADS.DeleteAsync(WebPluginArguments.GetString(args, 0), WebPluginArguments.GetString(args, 1, "long_note"))),
                "fs.ads.exists" => Ok(h.ADS.ExistsAsync(WebPluginArguments.GetString(args, 0), WebPluginArguments.GetString(args, 1, "long_note"))),
                "fs.ads.isNTFS" => Ok(h.ADS.IsNTFSVolumeAsync(WebPluginArguments.GetString(args, 0))),

                // === long.hotkey ===
                "hotkey.register" => HotKeyRegister(args),
                "hotkey.unregister" => HotKeyUnregister(args),
                "hotkey.isConflict" => Ok(h.HotKey.IsConflictAsync(WebPluginArguments.GetString(args, 0))),

                // === long.registry ===
                "registry.read" => Ok(h.Registry.ReadValueAsync(WebPluginArguments.GetString(args, 0), WebPluginArguments.GetString(args, 1))),
                "registry.write" => Ok(h.Registry.WriteValueAsync(WebPluginArguments.GetString(args, 0), WebPluginArguments.GetString(args, 1), WebPluginArguments.GetString(args, 2))),
                "registry.delete" => Ok(h.Registry.DeleteValueAsync(WebPluginArguments.GetString(args, 0), WebPluginArguments.GetString(args, 1))),

                // === long.storage ===
                "storage.get" => Ok(h.Storage.GetAsync(WebPluginArguments.GetString(args, 0))),
                "storage.set" => Ok(h.Storage.SetAsync(WebPluginArguments.GetString(args, 0), WebPluginArguments.GetString(args, 1))),
                "storage.delete" => Ok(h.Storage.DeleteAsync(WebPluginArguments.GetString(args, 0))),
                "storage.containsKey" => Ok(h.Storage.ContainsKeyAsync(WebPluginArguments.GetString(args, 0))),

                // === long.process ===
                "process.start" => Ok(h.Process.StartAsync(WebPluginArguments.GetString(args, 0), WebPluginArguments.GetString(args, 1, null!))),
                "process.getList" => OkList(h.Process.GetRunningProcessesAsync(WebPluginArguments.GetString(args, 0, null!))),
                "process.kill" => Ok(h.Process.KillAsync(WebPluginArguments.GetInt(args, 0))),

                // === long.fileOps ===
                "fileOps.copy" => Ok(h.FileOps.CopyAsync(WebPluginArguments.GetString(args, 0), WebPluginArguments.GetString(args, 1))),
                "fileOps.move" => Ok(h.FileOps.MoveAsync(WebPluginArguments.GetString(args, 0), WebPluginArguments.GetString(args, 1))),
                "fileOps.delete" => Ok(h.FileOps.DeleteAsync(WebPluginArguments.GetString(args, 0))),
                "fileOps.exists" => Ok(h.FileOps.ExistsAsync(WebPluginArguments.GetString(args, 0))),

                // === long.performance ===
                "performance.getCpuUsage" => Ok(h.Performance.GetCpuUsageAsync()),
                "performance.getMemoryInfo" => Ok(h.Performance.GetMemoryInfoAsync()),
                "performance.getDiskInfo" => OkList(h.Performance.GetDiskInfoAsync()),
                "performance.getSystemInfo" => Ok(h.Performance.GetSystemInfoAsync()),
                "performance.getTopByCpu" => OkList(h.Performance.GetTopProcessesByCpuAsync(WebPluginArguments.GetInt(args, 0, 10))),
                "performance.getTopByMemory" => OkList(h.Performance.GetTopProcessesByMemoryAsync(WebPluginArguments.GetInt(args, 0, 10))),

                // === long.networkPort / long.network ===
                "networkPort.getTcpConnections" => OkList(h.NetworkPort.GetTcpConnectionsAsync()),
                "networkPort.getTcpListeners" => OkList(h.NetworkPort.GetTcpListenersAsync()),
                "networkPort.getUdpEndpoints" => OkList(h.NetworkPort.GetUdpEndpointsAsync()),
                "networkPort.findOwner" => Ok(h.NetworkPort.FindPortOwnerAsync(
                    WebPluginArguments.GetInt(args, 0), WebPluginArguments.GetEnum(args, 1, ProtocolType.Tcp))),
                "networkPort.isInUse" => Ok(h.NetworkPort.IsPortInUseAsync(
                    WebPluginArguments.GetInt(args, 0), WebPluginArguments.GetEnum(args, 1, ProtocolType.Tcp))),
                "networkPort.getSummary" => Ok(h.NetworkPort.GetPortSummaryAsync()),
                "network.getStats" => Ok(h.NetworkMonitor.GetNetworkStatsAsync()),
                "network.getSpeed" => Ok(h.NetworkMonitor.GetNetworkSpeedAsync()),
                "network.getInterfaces" => OkList(h.NetworkMonitor.GetNetworkInterfacesAsync()),

                // === long.audio ===
                "audio.getVolume" => Ok(h.Audio.GetVolumeAsync()),
                "audio.setVolume" => Ok(h.Audio.SetVolumeAsync(WebPluginArguments.GetInt(args, 0))),
                "audio.getMute" => Ok(h.Audio.GetMuteAsync()),
                "audio.setMute" => Ok(h.Audio.SetMuteAsync(WebPluginArguments.GetBool(args, 0))),
                "audio.increase" => Ok(h.Audio.IncreaseVolumeAsync(WebPluginArguments.GetInt(args, 0, 5))),
                "audio.decrease" => Ok(h.Audio.DecreaseVolumeAsync(WebPluginArguments.GetInt(args, 0, 5))),
                "audio.getDevices" => OkList(h.Audio.GetAudioDevicesAsync()),
                "audio.setDefaultDevice" => Ok(h.Audio.SetDefaultDeviceAsync(WebPluginArguments.GetString(args, 0))),

                // === long.power ===
                "power.getStatus" => Ok(h.Power.GetPowerStatusAsync()),
                "power.lock" => Ok(h.Power.LockScreenAsync()),
                "power.sleep" => Ok(h.Power.SleepAsync()),
                "power.hibernate" => Ok(h.Power.HibernateAsync()),
                "power.shutdown" => Ok(h.Power.ShutdownAsync(WebPluginArguments.GetInt(args, 0))),
                "power.reboot" => Ok(h.Power.RebootAsync(WebPluginArguments.GetInt(args, 0))),
                "power.preventSleep" => Ok(h.Power.PreventSleepAsync(WebPluginArguments.GetBool(args, 0))),

                // === long.theme / wallpaper / brightness ===
                "theme.get" => Ok(h.Theme.GetSystemThemeAsync()),
                "theme.set" => Ok(h.Theme.SetSystemThemeAsync(WebPluginArguments.GetEnum(args, 0, SystemTheme.Auto))),
                "theme.toggle" => Ok(h.Theme.ToggleThemeAsync()),
                "theme.getAccentColor" => Ok(h.Theme.GetAccentColorAsync()),
                "theme.setAccentColor" => Ok(h.Theme.SetAccentColorAsync(WebPluginArguments.GetString(args, 0))),
                "wallpaper.get" => Ok(h.Wallpaper.GetWallpaperAsync()),
                "wallpaper.set" => Ok(h.Wallpaper.SetWallpaperAsync(
                    WebPluginArguments.GetString(args, 0), WebPluginArguments.GetEnum(args, 1, WallpaperStyle.Fill))),
                "wallpaper.getStyle" => Ok(h.Wallpaper.GetWallpaperStyleAsync()),
                "brightness.get" => Ok(h.Brightness.GetBrightnessAsync()),
                "brightness.set" => Ok(h.Brightness.SetBrightnessAsync(WebPluginArguments.GetInt(args, 0))),
                "brightness.increase" => Ok(h.Brightness.IncreaseBrightnessAsync(WebPluginArguments.GetInt(args, 0, 10))),
                "brightness.decrease" => Ok(h.Brightness.DecreaseBrightnessAsync(WebPluginArguments.GetInt(args, 0, 10))),

                // === long.pinyin ===
                "pinyin.get" => Ok(h.Pinyin.GetPinyinAsync(WebPluginArguments.GetString(args, 0))),
                "pinyin.getInitials" => Ok(h.Pinyin.GetPinyinInitialsAsync(WebPluginArguments.GetString(args, 0))),
                "pinyin.match" => Ok(h.Pinyin.MatchAsync(WebPluginArguments.GetString(args, 0), WebPluginArguments.GetString(args, 1))),
                "pinyin.filter" => OkList(h.Pinyin.FilterAsync(WebPluginArguments.GetStringList(args, 0), WebPluginArguments.GetString(args, 1))),

                // === long.input ===
                "input.keyPress" => Ok(h.Input.SendKeyPressAsync(WebPluginArguments.GetInt(args, 0))),
                "input.mouseClick" => Ok(h.Input.SendMouseClickAsync(
                    WebPluginArguments.GetInt(args, 0), WebPluginArguments.GetInt(args, 1), WebPluginArguments.GetBool(args, 2))),
                "input.moveCursor" => Ok(h.Input.MoveCursorAsync(WebPluginArguments.GetInt(args, 0), WebPluginArguments.GetInt(args, 1))),

                // === long.fileSystem ===
                "fileSystem.enumerate" => OkList(h.FileSystem.EnumerateFilesAsync(
                    WebPluginArguments.GetString(args, 0), WebPluginArguments.GetString(args, 1, "*.*"), WebPluginArguments.GetBool(args, 2, true))),
                "fileSystem.hash" => Ok(h.FileSystem.ComputeFileHashAsync(WebPluginArguments.GetString(args, 0))),
                "fileSystem.metadata" => Ok(h.FileSystem.GetFileMetadataAsync(WebPluginArguments.GetString(args, 0))),
                "fileSystem.findDuplicates" => OkList(h.FileSystem.FindDuplicateFilesAsync(WebPluginArguments.GetString(args, 0))),
                "fileSystem.batchRename" => Ok(h.FileSystem.BatchRenameAsync(
                    WebPluginArguments.GetJson<List<RenameOperation>>(args, 0) ?? new List<RenameOperation>())),
                "fileSystem.classify" => Ok(h.FileSystem.ClassifyFilesAsync(
                    WebPluginArguments.GetString(args, 0), WebPluginArguments.GetEnum(args, 1, ClassifyMode.ByExtension))),
                "fileSystem.findLarge" => OkList(h.FileSystem.FindLargeFilesAsync(
                    WebPluginArguments.GetString(args, 0), WebPluginArguments.GetLong(args, 1))),
                "fileSystem.searchContent" => OkList(h.FileSystem.SearchFileContentAsync(
                    WebPluginArguments.GetString(args, 0), WebPluginArguments.GetString(args, 1), WebPluginArguments.GetStringList(args, 2))),
                "fileSystem.planOrganization" => OkList(h.FileSystem.PlanFileOrganizationAsync(
                    WebPluginArguments.GetString(args, 0),
                    WebPluginArguments.GetEnum(args, 1, ClassifyMode.ByExtension))),
                "fileSystem.executeOrganization" => Ok(h.FileSystem.ExecuteFileOrganizationAsync(
                    WebPluginArguments.GetString(args, 0),
                    WebPluginArguments.GetEnum(args, 1, ClassifyMode.ByExtension),
                    WebPluginArguments.GetJson<List<FileOrganizationItem>>(args, 2)
                        ?? new List<FileOrganizationItem>())),

                // === long.cache ===
                "cache.cleanTemp" => Ok(h.Cache.CleanTempFilesAsync()),
                "cache.cleanWindowsUpdate" => Ok(h.Cache.CleanWindowsUpdateCacheAsync()),
                "cache.cleanBrowser" => Ok(h.Cache.CleanBrowserCacheAsync(WebPluginArguments.GetString(args, 0))),
                "cache.emptyRecycleBin" => Ok(h.Cache.EmptyRecycleBinAsync()),
                "cache.getStatistics" => Ok(h.Cache.GetCacheStatisticsAsync()),
                "cache.cleanAll" => Ok(h.Cache.CleanAllAsync()),

                // === long.schedule ===
                "schedule.create" => Ok(h.Schedule.CreateTaskAsync(
                    WebPluginArguments.GetJson<ScheduleTask>(args, 0) ?? new ScheduleTask())),
                "schedule.delete" => Ok(h.Schedule.DeleteTaskAsync(WebPluginArguments.GetString(args, 0))),
                "schedule.getAll" => OkList(h.Schedule.GetAllTasksAsync()),
                "schedule.setEnabled" => Ok(h.Schedule.SetTaskEnabledAsync(
                    WebPluginArguments.GetString(args, 0), WebPluginArguments.GetBool(args, 1))),
                "schedule.runNow" => Ok(h.Schedule.RunTaskNowAsync(WebPluginArguments.GetString(args, 0))),

                // === long.shell file ops ===
                "shell.listFiles" => Task.FromResult<object?>(ShellListFiles(WebPluginArguments.GetString(args, 0))),
                "shell.renameFile" => ShellRenameFileAsync(WebPluginArguments.GetString(args, 0), WebPluginArguments.GetString(args, 1)),

                // === long.window ===
                "window.getForeground" => WindowGetForeground(),
                "window.getVisible" => OkList(h.WindowInfo.GetVisibleWindowsAsync()),

                // === long.ui ===
                "ui.showToast" => Task.FromResult<object?>(UIToast(WebPluginArguments.GetString(args, 0))),
                "ui.createWindow" => UICreateWindow(args),
                "ui.confirm" => Ok(h.UI.ConfirmAsync(WebPluginArguments.GetString(args, 0), WebPluginArguments.GetString(args, 1, "确认"))),
                "ui.prompt" => Ok(h.UI.PromptAsync(
                    WebPluginArguments.GetString(args, 0), WebPluginArguments.GetString(args, 1, "输入"), WebPluginArguments.GetString(args, 2))),
                "ui.select" => Ok(h.UI.SelectAsync(
                    WebPluginArguments.GetString(args, 0), WebPluginArguments.GetStringList(args, 1).ToArray(), WebPluginArguments.GetString(args, 2, "选择"))),
                "ui.closeWindow" => Ok(h.UI.CloseWindowAsync(WebPluginArguments.GetString(args, 0))),
                "ui.sendMessage" => Ok(h.UI.SendMessageToWindowAsync(WebPluginArguments.GetString(args, 0), WebPluginArguments.GetString(args, 1))),

                // === long.screenshot ===
                "screenshot.captureFull" => Ok(h.ScreenCapture.CaptureFullScreenAsync()),
                "screenshot.captureRegion" => CaptureRegionToFile(args),

                _ => Task.FromResult<object?>(new { success = false, error = $"未知方法: {method}" }),
            });
        }

        /// <summary>
        /// 获取调用指定方法所需的 capability
        /// </summary>

        // 辅助方法
        private static async Task<object?> Ok<T>(Task<HostApiResponse<T>> t) { var r = await t; return new { success = r.IsSuccess, data = r.Data, error = r.ErrorMessage }; }

        private static async Task<object?> Ok(Task<HostApiResponse> t) { var r = await t; return new { success = r.IsSuccess, error = r.ErrorMessage }; }

        private static async Task<object?> OkList<T>(Task<HostApiResponse<List<T>>> t) { var r = await t; return new { success = r.IsSuccess, data = r.Data, error = r.ErrorMessage }; }

        private static object OkObj() => new { success = true };

        private async Task<object?> ClipboardStartMonitoringAsync()
        {
            await _clipboardGate.WaitAsync();
            try
            {
                Task<HostApiResponse> acquireTask;
                lock (_clipboardStateLock)
                {
                    if (_disposed)
                        return new { success = false, error = "Web 插件运行时已释放" };
                    if (_clipboardSubscribed)
                        return OkObj();

                    _host.Clipboard.ClipboardChanged += OnClipboardChanged;
                    _clipboardSubscribed = true;
                    _clipboardAcquireTask = _host.Clipboard.StartMonitoringAsync();
                    acquireTask = _clipboardAcquireTask;
                }

                var result = await acquireTask;
                if (!result.IsSuccess)
                {
                    lock (_clipboardStateLock)
                        UnsubscribeClipboardCore();
                }
                return new { success = result.IsSuccess, error = result.ErrorMessage };
            }
            finally
            {
                _clipboardGate.Release();
            }
        }

        private async Task<object?> ClipboardStopMonitoringAsync()
        {
            await _clipboardGate.WaitAsync();
            try
            {
                lock (_clipboardStateLock)
                {
                    if (_disposed || !_clipboardSubscribed)
                        return OkObj();
                    UnsubscribeClipboardCore();
                }

                var result = await _host.Clipboard.StopMonitoringAsync();
                if (!result.IsSuccess)
                {
                    var retryRelease = false;
                    lock (_clipboardStateLock)
                    {
                        if (!_disposed)
                        {
                            _host.Clipboard.ClipboardChanged += OnClipboardChanged;
                            _clipboardSubscribed = true;
                        }
                        else
                        {
                            retryRelease = true;
                        }
                    }
                    if (retryRelease)
                        _ = ReleaseClipboardAfterAcquireAsync(null);
                }
                return new { success = result.IsSuccess, error = result.ErrorMessage };
            }
            finally
            {
                _clipboardGate.Release();
            }
        }

        private void OnClipboardChanged(object? sender, ClipboardChangedEventArgs args)
        {
            try
            {
                _postMessage(WebPluginBridgeProtocol.SerializeClipboardChanged(args));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Web:{PluginId}] 剪贴板事件发送失败", _pluginId);
            }
        }

        private void UnsubscribeClipboardCore()
        {
            if (!_clipboardSubscribed) return;
            _host.Clipboard.ClipboardChanged -= OnClipboardChanged;
            _clipboardSubscribed = false;
        }

        public void Dispose()
        {
            Task<HostApiResponse>? acquireTask;
            lock (_clipboardStateLock)
            {
                if (_disposed) return;
                _disposed = true;
                if (!_clipboardSubscribed) return;
                UnsubscribeClipboardCore();
                acquireTask = _clipboardAcquireTask;
            }
            _ = ReleaseClipboardAfterAcquireAsync(acquireTask);
        }

        private async Task ReleaseClipboardAfterAcquireAsync(
            Task<HostApiResponse>? acquireTask)
        {
            try
            {
                if (acquireTask != null && !(await acquireTask).IsSuccess)
                    return;
                var result = await _host.Clipboard.StopMonitoringAsync();
                if (!result.IsSuccess)
                {
                    Log.Warning(
                        "[Web:{PluginId}] 释放剪贴板监听租约失败: {Error}",
                        _pluginId,
                        result.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Web:{PluginId}] 释放剪贴板监听租约失败", _pluginId);
            }
        }

        private object PluginLog(object?[] args)
        {
            Log.Information("[Web:{PluginId}] {Message}", _pluginId, string.Join(" ", args.Select(a => a?.ToString())));
            return OkObj();
        }

        private async Task<object?> CaptureRegionToFile(object?[] args)
        {
            var x = WebPluginArguments.GetInt(args, 0);
            var y = WebPluginArguments.GetInt(args, 1);
            var w = WebPluginArguments.GetInt(args, 2);
            var h = WebPluginArguments.GetInt(args, 3);
            var result = await _host.ScreenCapture.CaptureRegionAsync(x, y, w, h);
            if (!result.IsSuccess || result.Data == null)
                return new { success = false, error = result.ErrorMessage };

            // 保存到临时文件并返回路径
            var tempDir = System.IO.Path.GetTempPath();
            var fileName = $"long_screenshot_{DateTime.Now:yyyyMMddHHmmss}.png";
            var filePath = System.IO.Path.Combine(tempDir, fileName);
            using (var stream = new System.IO.FileStream(filePath, System.IO.FileMode.Create))
            {
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(result.Data));
                encoder.Save(stream);
            }
            return new { success = true, filePath = filePath.Replace("\\", "/") };
        }

        private object ShellListFiles(string dir)
        {
            if (!Directory.Exists(dir)) return new { success = false };
            var files = Directory.GetFiles(dir).Select(f => new { name = Path.GetFileName(f), path = f.Replace("\\", "/") }).ToList();
            return new { success = true, files };
        }

        private async Task<object?> ShellRenameFileAsync(string oldPath, string newName)
        {
            var directory = Path.GetDirectoryName(oldPath);
            if (directory == null)
                return new { success = false, error = "源路径无效" };

            var newPath = Path.Combine(directory, newName);
            var result = await _host.FileOps.MoveAsync(oldPath, newPath);
            return new { success = result.IsSuccess, error = result.ErrorMessage };
        }

        private async Task<object?> WindowGetForeground()
        {
            var result = await _host.WindowInfo.GetForegroundWindowInfoAsync();
            return result.IsSuccess ? result.Data! : new { success = false };
        }

        private object UIToast(string msg)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                Views.FloatingHudWindow.ShowToast(msg));
            return OkObj();
        }

        // === HTTP 辅助方法 ===

        private async Task<object?> HttpPost(object?[] args)
        {
            var url = WebPluginArguments.GetString(args, 0);
            var body = WebPluginArguments.GetString(args, 1);
            var contentType = WebPluginArguments.GetString(args, 2, "application/json");
            var headers = WebPluginArguments.GetHeaders(args, 3);
            var r = await _host.Http.PostAsync(url, body, contentType, headers);
            return new { success = r.IsSuccess, data = r.Data, error = r.ErrorMessage };
        }

        private async Task<object?> HttpDownload(object?[] args)
        {
            var url = WebPluginArguments.GetString(args, 0);
            var r = await _host.Http.DownloadAsync(url);
            if (!r.IsSuccess || r.Data == null)
                return new { success = false, error = r.ErrorMessage };

            // 保存到临时文件
            var tempDir = System.IO.Path.GetTempPath();
            var fileName = $"long_download_{DateTime.Now:yyyyMMddHHmmss}_{System.IO.Path.GetFileName(new Uri(url).AbsolutePath)}";
            if (string.IsNullOrEmpty(System.IO.Path.GetExtension(fileName)))
                fileName += ".bin";
            var filePath = System.IO.Path.Combine(tempDir, fileName);
            await System.IO.File.WriteAllBytesAsync(filePath, r.Data);
            return new { success = true, filePath = filePath.Replace("\\", "/"), size = r.Data.Length };
        }

        private async Task<object?> HotKeyRegister(object?[] args)
        {
            var hotkey = WebPluginArguments.GetString(args, 0);
            var r = await _host.HotKey.RegisterAsync(hotkey, () =>
            {
                _postMessage(WebPluginBridgeProtocol.SerializeHotkey(hotkey));
            });
            return new { success = r.IsSuccess, error = r.ErrorMessage };
        }

        private async Task<object?> HotKeyUnregister(object?[] args)
        {
            var hotkey = WebPluginArguments.GetString(args, 0);
            var r = await _host.HotKey.UnregisterAsync(hotkey);
            return new { success = r.IsSuccess, error = r.ErrorMessage };
        }

        private async Task<object?> UICreateWindow(object?[] args)
        {
            var title = WebPluginArguments.GetString(args, 0);
            var htmlContent = WebPluginArguments.GetString(args, 1);
            var width = WebPluginArguments.GetInt(args, 2, 600);
            var height = WebPluginArguments.GetInt(args, 3, 400);
            var resizable = WebPluginArguments.GetBool(args, 4, true);

            var r = await _host.UI.CreateWindowAsync(title, htmlContent, width, height, resizable);
            return new { success = r.IsSuccess, data = r.Data, error = r.ErrorMessage };
        }
    }
}
