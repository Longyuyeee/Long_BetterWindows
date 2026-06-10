using LongBetterWindows.Host.Services;
using LongBetterWindows.Host.Views;
using Serilog;
using Application = System.Windows.Application;

namespace LongBetterWindows.Host.Tools
{
    public class FolderNoteTool
    {
        private const string DefaultHotkey = "Alt+M";
        private bool _isEnabled;
        private bool _isActive;

        public string Name => "文件夹备注助手";
        public string Description => "为文件夹添加备注，数据存储于 NTFS 备用数据流中。";
        public bool IsEnabled => _isEnabled;
        public bool IsActive => _isActive;

        public async Task<bool> EnableAsync()
        {
            if (_isEnabled) return true;

            var result = await ServicesInitializer.HotKey.RegisterAsync(
                DefaultHotkey, OnHotkeyTriggered);

            if (!result.IsSuccess)
            {
                Log.Warning("文件夹备注助手热键注册失败: {Error}", result.ErrorMessage);
                return false;
            }

            _isEnabled = true;
            Log.Information("文件夹备注助手已启用，热键: {Hotkey}", DefaultHotkey);
            return true;
        }

        public async Task DisableAsync()
        {
            if (!_isEnabled) return;

            await ServicesInitializer.HotKey.UnregisterAsync(DefaultHotkey);
            _isEnabled = false;
            Log.Information("文件夹备注助手已禁用");
        }

        private async void OnHotkeyTriggered()
        {
            if (_isActive) return;
            _isActive = true;

            try
            {
                await ShowNoteHudAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "文件夹备注助手执行失败");
            }
            finally
            {
                _isActive = false;
            }
        }

        private async Task ShowNoteHudAsync()
        {
            var folderResult = await ServicesInitializer.ShellSelection
                .GetActiveExplorerFolderPathAsync();

            if (!folderResult.IsSuccess || folderResult.Data == null)
            {
                Log.Debug("未找到活跃的 Explorer 文件夹");
                FloatingHudWindow.ShowToast("请先打开一个资源管理器窗口，并选中文件夹。");
                return;
            }

            var folderPath = folderResult.Data;

            var noteResult = await ServicesInitializer.ADS
                .ReadAsync(folderPath, "long_note");

            string? existingNote = null;
            if (noteResult.IsSuccess && noteResult.Data != null)
            {
                existingNote = noteResult.Data;
            }

            var rectResult = await ServicesInitializer.ShellSelection
                .GetSelectedItemScreenRectAsync();

            double hudX, hudY;

            if (rectResult.IsSuccess)
            {
                hudX = rectResult.Data.X;
                hudY = rectResult.Data.Y + rectResult.Data.Height + 8;
            }
            else
            {
                GetCursorPos(out var pt);
                hudX = pt.X;
                hudY = pt.Y;
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                FloatingHudWindow.ShowAt(hudX, hudY, existingNote, folderPath,
                    async (text) =>
                    {
                        await SaveNoteAsync(folderPath, text);
                    });
            });
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        private async Task SaveNoteAsync(string folderPath, string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                await ServicesInitializer.ADS.DeleteAsync(folderPath, "long_note");
                Log.Information("备注已删除: {Path}", folderPath);
            }
            else
            {
                await ServicesInitializer.ADS.WriteAsync(folderPath, "long_note", text);
                Log.Information("备注已保存: {Path} ({Length} 字符)", folderPath, text.Length);
            }
        }
    }
}
