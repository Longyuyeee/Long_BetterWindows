using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;
using Serilog;
using static LongBetterWindows.Host.Services.NativeMethods;

namespace LongBetterWindows.Host.Services
{
    public class HotKeyService : IHotKeyService, IDisposable
    {
        private IntPtr _hwnd;
        private HwndSource? _source;
        private Dispatcher? _dispatcher;
        private readonly Dictionary<string, HotKeyEntry> _entries = new();
        private readonly object _lock = new();
        private int _nextId = 1;

        private static readonly Dictionary<string, uint> KeyNameMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = 0x41, ["B"] = 0x42, ["C"] = 0x43, ["D"] = 0x44,
            ["E"] = 0x45, ["F"] = 0x46, ["G"] = 0x47, ["H"] = 0x48,
            ["I"] = 0x49, ["J"] = 0x4A, ["K"] = 0x4B, ["L"] = 0x4C,
            ["M"] = 0x4D, ["N"] = 0x4E, ["O"] = 0x4F, ["P"] = 0x50,
            ["Q"] = 0x51, ["R"] = 0x52, ["S"] = 0x53, ["T"] = 0x54,
            ["U"] = 0x55, ["V"] = 0x56, ["W"] = 0x57, ["X"] = 0x58,
            ["Y"] = 0x59, ["Z"] = 0x5A,
            ["0"] = 0x30, ["1"] = 0x31, ["2"] = 0x32, ["3"] = 0x33,
            ["4"] = 0x34, ["5"] = 0x35, ["6"] = 0x36, ["7"] = 0x37,
            ["8"] = 0x38, ["9"] = 0x39,
            ["F1"] = 0x70, ["F2"] = 0x71, ["F3"] = 0x72, ["F4"] = 0x73,
            ["F5"] = 0x74, ["F6"] = 0x75, ["F7"] = 0x76, ["F8"] = 0x77,
            ["F9"] = 0x78, ["F10"] = 0x79, ["F11"] = 0x7A, ["F12"] = 0x7B,
            ["Space"] = 0x20, ["Tab"] = 0x09, ["Enter"] = 0x0D,
            ["Escape"] = 0x1B, ["Back"] = 0x08, ["Delete"] = 0x2E,
            ["Up"] = 0x26, ["Down"] = 0x28, ["Left"] = 0x25, ["Right"] = 0x27,
            ["Home"] = 0x24, ["End"] = 0x23, ["PageUp"] = 0x21, ["PageDown"] = 0x22,
            ["OemPeriod"] = 0xBE, [","] = 0xBC, ["."] = 0xBE, ["Oemcomma"] = 0xBC,
            ["OemSemicolon"] = 0xBA, [";"] = 0xBA,
            ["OemOpenBrackets"] = 0xDB, ["["] = 0xDB, ["OemCloseBrackets"] = 0xDD, ["]"] = 0xDD,
            ["OemQuestion"] = 0xBF, ["/"] = 0xBF, ["OemPipe"] = 0xDC, ["\\"] = 0xDC,
            ["OemMinus"] = 0xBD, ["-"] = 0xBD, ["Oemplus"] = 0xBB, ["="] = 0xBB,
            ["Oemtilde"] = 0xC0, ["`"] = 0xC0,
        };

        public HotKeyService()
        {
            _hwnd = IntPtr.Zero;
        }

        public void Initialize(IntPtr hwnd)
        {
            _hwnd = hwnd;
            _dispatcher = Dispatcher.CurrentDispatcher;
            _source = HwndSource.FromHwnd(hwnd);
            _source.AddHook(WndProc);
            Log.Information("HotKeyService 已初始化，HWnd: 0x{Hwnd:X}", hwnd);
        }

        public Task<HostApiResponse> RegisterAsync(string hotkey, Action callback)
        {
            var pluginId = Engine.PluginAccessContext.CurrentPluginId ?? "builtin";
            return RegisterAsync(hotkey, pluginId, callback);
        }

        public Task<HostApiResponse> RegisterAsync(string hotkey, string pluginId, Action callback)
        {
            if (_dispatcher != null && !_dispatcher.CheckAccess())
                return _dispatcher.InvokeAsync(() => RegisterCore(hotkey, pluginId, callback)).Task;

            return Task.FromResult(RegisterCore(hotkey, pluginId, callback));
        }

        private HostApiResponse RegisterCore(string hotkey, string pluginId, Action callback)
        {
            lock (_lock)
            {
                var normalized = Normalize(hotkey);

                if (_entries.TryGetValue(normalized, out var existing))
                {
                    return HostApiResponse.Failure(
                        ApiErrorCode.HotKeyConflict,
                        $"热键 '{hotkey}' 已被「{existing.OwnerName}」占用。");
                }

                if (!TryParseHotkey(hotkey, out var modifiers, out var vk, out var error))
                {
                    return HostApiResponse.Failure(ApiErrorCode.InvalidArgument, error);
                }

                if (_hwnd == IntPtr.Zero)
                {
                    return HostApiResponse.Failure(
                        ApiErrorCode.Unknown, "HotKeyService 未初始化，请先调用 Initialize。");
                }

                int id = _nextId++;

                if (!RegisterHotKey(_hwnd, id, modifiers, vk))
                {
                    int win32Error = Marshal.GetLastWin32Error();
                    Log.Warning("RegisterHotKey 失败: {Hotkey}, Win32Error={Error}",
                        hotkey, win32Error);
                    return HostApiResponse.Failure(
                        ApiErrorCode.HotKeyRegistrationFailed,
                        $"注册热键失败 (Win32 Error: {win32Error})。");
                }

                _entries[normalized] = new HotKeyEntry
                {
                    Id = id,
                    Original = hotkey,
                    PluginId = pluginId,
                    Modifiers = modifiers,
                    Key = vk,
                    Callback = callback,
                };

                Log.Information("热键已注册: {Hotkey} (ID={Id})", hotkey, id);
                return HostApiResponse.Success();
            }
        }

        public Task<HostApiResponse> UnregisterAsync(string hotkey)
        {
            if (_dispatcher != null && !_dispatcher.CheckAccess())
                return _dispatcher.InvokeAsync(() => UnregisterCore(hotkey)).Task;

            return Task.FromResult(UnregisterCore(hotkey));
        }

        private HostApiResponse UnregisterCore(string hotkey)
        {
            lock (_lock)
            {
                var normalized = Normalize(hotkey);

                if (!_entries.TryGetValue(normalized, out var entry))
                {
                    return HostApiResponse.Failure(
                        ApiErrorCode.NotFound, $"热键 '{hotkey}' 未注册。");
                }

                UnregisterHotKey(_hwnd, entry.Id);
                _entries.Remove(normalized);

                Log.Information("热键已注销: {Hotkey}", hotkey);
                return HostApiResponse.Success();
            }
        }

        public Task<HostApiResponse<bool>> IsConflictAsync(string hotkey)
        {
            lock (_lock)
            {
                var normalized = Normalize(hotkey);
                bool conflict = _entries.ContainsKey(normalized);
                return Task.FromResult(HostApiResponse<bool>.Success(conflict));
            }
        }

        public string? GetOwner(string hotkey)
        {
            lock (_lock)
            {
                var normalized = Normalize(hotkey);
                return _entries.TryGetValue(normalized, out var entry)
                    ? entry.PluginId : null;
            }
        }

        public IReadOnlyDictionary<string, string> GetAllHotkeys()
        {
            lock (_lock)
            {
                return _entries.ToDictionary(
                    kv => kv.Value.Original,
                    kv => kv.Value.PluginId);
            }
        }

        public async Task<HostApiResponse> ChangeHotkeyAsync(
            string oldHotkey, string newHotkey, string pluginId, Action callback)
        {
            // 检查新热键是否冲突（排除自己的旧热键）
            var conflict = await IsConflictAsync(newHotkey);
            if (conflict.IsSuccess && conflict.Data)
            {
                var owner = GetOwner(newHotkey);
                if (owner != pluginId)
                {
                    return HostApiResponse.Failure(ApiErrorCode.HotKeyConflict,
                        $"热键 '{newHotkey}' 已被其他插件占用。");
                }
            }

            // 注销旧热键
            var unreg = await UnregisterAsync(oldHotkey);
            if (!unreg.IsSuccess && unreg.ErrorMessage != null)
            {
                // 旧热键可能不存在（首次设置），忽略
            }

            // 注册新热键
            return await RegisterAsync(newHotkey, pluginId, callback);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();

                lock (_lock)
                {
                    foreach (var entry in _entries.Values)
                    {
                        if (entry.Id == id)
                        {
                            Log.Debug("热键触发: {Hotkey} (ID={Id})", entry.Original, id);
                            entry.Callback?.Invoke();
                            handled = true;
                            break;
                        }
                    }
                }
            }

            return IntPtr.Zero;
        }

        private static string Normalize(string hotkey)
        {
            return string.Join("+", hotkey.Split('+')
                .Select(p => p.Trim())
                .Where(p => p.Length > 0)
                .Select(p => char.ToUpper(p[0]) + p.Substring(1).ToLower()))
                .Replace("Control", "Ctrl", StringComparison.OrdinalIgnoreCase)
                .Replace("Windows", "Win", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryParseHotkey(string hotkey, out uint modifiers, out uint vk, out string? error)
        {
            modifiers = 0;
            vk = 0;
            error = null;

            var parts = hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .ToList();

            if (parts.Count < 2)
            {
                error = "热键格式无效。需要至少一个修饰键和一个主键，如 'Alt+M'。";
                return false;
            }

            foreach (var part in parts.Take(parts.Count - 1))
            {
                if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                    part.Equals("Control", StringComparison.OrdinalIgnoreCase))
                    modifiers |= MOD_CONTROL;
                else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                    modifiers |= MOD_ALT;
                else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                    modifiers |= MOD_SHIFT;
                else if (part.Equals("Win", StringComparison.OrdinalIgnoreCase) ||
                         part.Equals("Windows", StringComparison.OrdinalIgnoreCase))
                    modifiers |= MOD_WIN;
                else
                {
                    error = $"未知修饰键: '{part}'";
                    return false;
                }
            }

            var keyPart = parts.Last();
            if (!KeyNameMap.TryGetValue(keyPart, out vk))
            {
                error = $"未知键名: '{keyPart}'";
                return false;
            }

            return true;
        }

        public void Dispose()
        {
            lock (_lock)
            {
                foreach (var entry in _entries.Values)
                {
                    UnregisterHotKey(_hwnd, entry.Id);
                }

                _entries.Clear();

                if (_source != null)
                {
                    _source.RemoveHook(WndProc);
                    _source.Dispose();
                    _source = null;
                }
            }

            Log.Information("HotKeyService 已释放，所有热键已注销。");
        }
    }

    internal sealed class HotKeyEntry
    {
        public int Id { get; init; }
        public string Original { get; init; } = string.Empty;
        public string PluginId { get; init; } = string.Empty;
        public string OwnerName => PluginId;
        public uint Modifiers { get; init; }
        public uint Key { get; init; }
        public Action? Callback { get; init; }
    }
}
