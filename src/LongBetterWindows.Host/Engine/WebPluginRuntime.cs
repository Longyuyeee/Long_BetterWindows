using System.IO;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Web.WebView2.Core;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using Serilog;

namespace LongBetterWindows.Host.Engine
{
    /// <summary>
    /// WebView2 插件运行时——支持 HTML/JS/CSS 开发插件。
    ///
    /// manifest.json 中 "runtime": "webview" 时使用。
    /// 插件是标准的 HTML 文件，通过 long.* JS API 调用宿主能力。
    ///
    /// JS API 用法 (uTools 风格):
    ///   await long.hotkey.register("Alt+X", callback)
    ///   let text = await long.clipboard.getText()
    ///   await long.clipboard.setText("hello")
    ///   let path = await long.shell.getActiveFolder()
    ///   let note = await long.ads.read(path, "long_note")
    ///   await long.storage.set("key", "value")
    /// </summary>
    public class WebPluginRuntime
    {
        private readonly WebView2 _webView;
        private readonly string _pluginDir;
        private readonly PluginManifest _manifest;

        public WebView2 WebView => _webView;

        public WebPluginRuntime(PluginManifest manifest, string pluginDir)
        {
            _manifest = manifest;
            _pluginDir = pluginDir;
            _webView = new WebView2();
        }

        public async Task<bool> InitializeAsync()
        {
            var env = await CoreWebView2Environment.CreateAsync();
            await _webView.EnsureCoreWebView2Async(env);

            var host = HostProvider.Instance;

            // 注入 long.* JS API 桥接
            _webView.CoreWebView2.WebMessageReceived += (s, e) =>
            {
                HandleJsMessage(e.WebMessageAsJson);
            };

            // 在页面加载前注入初始化脚本
            _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                BuildJsBridge(host, _manifest.Id));

            // 加载插件 HTML
            var indexPath = Path.Combine(_pluginDir, _manifest.EntryPoint);
            if (File.Exists(indexPath))
            {
                _webView.CoreWebView2.Navigate(
                    new Uri(indexPath).AbsoluteUri);
            }
            else
            {
                // 默认空白页（纯 JS 插件无需 HTML）
                _webView.CoreWebView2.NavigateToString(
                    "<html><body><p>JS Plugin Ready</p></body></html>");
            }

            Log.Information("WebPlugin {PluginId} 已初始化", _manifest.Id);
            return true;
        }

        private void HandleJsMessage(string json)
        {
            // 处理来自 JS 的异步消息
            try
            {
                var msg = System.Text.Json.JsonSerializer.Deserialize<JsMessage>(json);
                if (msg == null) return;

                _ = HandleJsRequestAsync(msg);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "WebPlugin JS 消息处理失败");
            }
        }

        private async Task HandleJsRequestAsync(JsMessage msg)
        {
            try
            {
                var result = await DispatchJsCall(msg.Method, msg.Args);
                var response = System.Text.Json.JsonSerializer.Serialize(new
                {
                    id = msg.Id,
                    result,
                });

                _webView.CoreWebView2.PostWebMessageAsJson(response);
            }
            catch (Exception ex)
            {
                var error = System.Text.Json.JsonSerializer.Serialize(new
                {
                    id = msg.Id,
                    error = ex.Message,
                });
                _webView.CoreWebView2.PostWebMessageAsJson(error);
            }
        }

        private async Task<object?> DispatchJsCall(string method, object?[] args)
        {
            var host = HostProvider.Instance;

            return method switch
            {
                "hotkey.register" => await HotKeyRegister(args),
                "hotkey.unregister" => await HotKeyUnregister(args),
                "clipboard.getText" => await ClipboardGetText(),
                "clipboard.setText" => await ClipboardSetText(args),
                "shell.getActiveFolder" => await ShellGetFolder(),
                "storage.get" => await StorageGet(args),
                "storage.set" => await StorageSet(args),
                "ads.read" => await AdsRead(args),
                "ads.write" => await AdsWrite(args),
                _ => null,
            };
        }

        private async Task<object> HotKeyRegister(object?[] args)
        {
            var hotkey = args[0]?.ToString() ?? "";
            var id = _manifest.Id;
            var host = HostProvider.Instance;

            var result = await host.HotKey!.RegisterAsync(hotkey, () =>
            {
                _webView.CoreWebView2.PostWebMessageAsJson(
                    System.Text.Json.JsonSerializer.Serialize(new
                    {
                        type = "hotkey",
                        hotkey,
                    }));
            });

            return new { success = result.IsSuccess, error = result.ErrorMessage };
        }

        private Task<object> HotKeyUnregister(object?[] args)
        {
            var hotkey = args[0]?.ToString() ?? "";
            return Task.FromResult<object>(new { unregistered = hotkey });
        }

        private async Task<object> ClipboardGetText()
        {
            var host = HostProvider.Instance;
            var result = await host.Clipboard!.GetTextAsync();
            return new { success = result.IsSuccess, text = result.Data };
        }

        private async Task<object> ClipboardSetText(object?[] args)
        {
            var text = args[0]?.ToString() ?? "";
            var host = HostProvider.Instance;
            var result = await host.Clipboard!.SetTextAsync(text);
            return new { success = result.IsSuccess };
        }

        private async Task<object> ShellGetFolder()
        {
            var host = HostProvider.Instance;
            var result = await host.ShellSelection!.GetActiveExplorerFolderPathAsync();
            return new { success = result.IsSuccess, path = result.Data };
        }

        private async Task<object> StorageGet(object?[] args)
        {
            var key = args[0]?.ToString() ?? "";
            var host = HostProvider.Instance;
            var result = await host.Storage!.GetAsync(key);
            return new { success = result.IsSuccess, value = result.Data };
        }

        private async Task<object> StorageSet(object?[] args)
        {
            var key = args[0]?.ToString() ?? "";
            var value = args[1]?.ToString() ?? "";
            var host = HostProvider.Instance;
            var result = await host.Storage!.SetAsync(key, value);
            return new { success = result.IsSuccess };
        }

        private async Task<object> AdsRead(object?[] args)
        {
            var path = args[0]?.ToString() ?? "";
            var stream = args[1]?.ToString() ?? "long_note";
            var host = HostProvider.Instance;
            var result = await host.ADS!.ReadAsync(path, stream);
            return new { success = result.IsSuccess, content = result.Data };
        }

        private async Task<object> AdsWrite(object?[] args)
        {
            var path = args[0]?.ToString() ?? "";
            var content = args[1]?.ToString() ?? "";
            var stream = args[2]?.ToString() ?? "long_note";
            var host = HostProvider.Instance;
            var result = await host.ADS!.WriteAsync(path, stream, content);
            return new { success = result.IsSuccess };
        }

        private static string BuildJsBridge(IHostApi host, string pluginId)
        {
            return $@"
// Long窗口 WebView2 JS Bridge
window.long = {{
    _callId: 0,
    _pending: {{}},

    _call: function(method, args) {{
        var id = ++window.long._callId;
        return new Promise(function(resolve, reject) {{
            window.long._pending[id] = {{ resolve, reject }};
            window.chrome.webview.postMessage(JSON.stringify({{
                id: id,
                method: method,
                args: args || [],
            }}));
        }});
    }},

    hotkey: {{
        register: function(hotkey, callback) {{
            return window.long._call('hotkey.register', [hotkey]);
        }},
        unregister: function(hotkey) {{
            return window.long._call('hotkey.unregister', [hotkey]);
        }},
    }},

    clipboard: {{
        getText: function() {{ return window.long._call('clipboard.getText', []); }},
        setText: function(text) {{ return window.long._call('clipboard.setText', [text]); }},
    }},

    shell: {{
        getActiveFolder: function() {{ return window.long._call('shell.getActiveFolder', []); }},
    }},

    storage: {{
        get: function(key) {{ return window.long._call('storage.get', [key]); }},
        set: function(key, value) {{ return window.long._call('storage.set', [key, value]); }},
    }},

    ads: {{
        read: function(path, stream) {{ return window.long._call('ads.read', [path, stream || 'long_note']); }},
        write: function(path, content, stream) {{ return window.long._call('ads.write', [path, content, stream || 'long_note']); }},
    }},
}};

// 接收来自 .NET 的响应和热键事件
window.chrome.webview.addEventListener('message', function(e) {{
    try {{
        var msg = JSON.parse(e.data);
        if (msg.id && window.long._pending[msg.id]) {{
            if (msg.error) {{
                window.long._pending[msg.id].reject(new Error(msg.error));
            }} else {{
                window.long._pending[msg.id].resolve(msg.result);
            }}
            delete window.long._pending[msg.id];
        }}
        if (msg.type === 'hotkey') {{
            console.log('[Long] 热键触发:', msg.hotkey);
        }}
    }} catch(ex) {{ }}
}});

console.log('[Long] JS Bridge 就绪, 插件ID: {pluginId}');
";
        }
    }

    internal class JsMessage
    {
        public int Id { get; set; }
        public string Method { get; set; } = string.Empty;
        public object?[] Args { get; set; } = Array.Empty<object?>();
    }
}
