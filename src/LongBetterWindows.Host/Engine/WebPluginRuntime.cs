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
    public class WebPluginRuntime : IDisposable
    {
        private WebView2? _webView;
        private readonly PluginManifest _manifest;
        private readonly WebPluginNavigationPolicy _navigationPolicy;
        private bool _themeSubscribed;

        public WebView2? WebView => _webView;
        public PluginManifest Manifest => _manifest;

        public WebPluginRuntime(PluginManifest manifest, string pluginDir)
        {
            _manifest = manifest;
            _navigationPolicy = new WebPluginNavigationPolicy(pluginDir);
            // WebView2 延迟到 UI 线程创建（InitializeAsync）
        }

        public WebView2 EnsureView()
        {
            var dispatcher = System.Windows.Application.Current.Dispatcher;
            if (!dispatcher.CheckAccess())
                return dispatcher.Invoke(EnsureView);
            return _webView ??= new WebView2();
        }

        public async Task<bool> InitializeAsync()
        {
            // WebView2 必须在 STA 线程（UI 线程）初始化和使用
            var initialization = System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                var webView = EnsureView();

                var env = await CoreWebView2Environment.CreateAsync();
                await webView.EnsureCoreWebView2Async(env);

#if DEBUG
                webView.CoreWebView2.Settings.AreDevToolsEnabled = true;
#else
                webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
#endif
                webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                webView.CoreWebView2.NavigationStarting += OnNavigationStarting;
                webView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
                webView.CoreWebView2.DownloadStarting += OnDownloadStarting;
                webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

                var host = HostProvider.Instance;

                var uiKitScript = BuildUiKitInjectionScript();
                if (!string.IsNullOrEmpty(uiKitScript))
                    await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(uiKitScript);

                // 注入 long.* JS API 桥接
                // 在页面加载前注入初始化脚本（与导航并行执行）
                _ = webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                    BuildJsBridge(host, _manifest.Id))
                    .ContinueWith(t =>
                    {
                        if (t.IsFaulted)
                            Log.Error(t.Exception, "[Web:{Id}] JS Bridge 注入失败", _manifest.Id);
                        else
                            Log.Debug("[Web:{Id}] JS Bridge 注入完成", _manifest.Id);
                    }, TaskScheduler.Default);

                if (!_themeSubscribed)
                {
                    App.ThemeChanged += OnThemeChanged;
                    _themeSubscribed = true;
                }

                // 加载插件 HTML
                if (!_navigationPolicy.TryResolveEntryPoint(_manifest.EntryPoint, out var entryUri))
                    throw new InvalidDataException(
                        $"Web 插件入口不存在或越出插件目录：{_manifest.EntryPoint}");

                webView.CoreWebView2.Navigate(entryUri!.AbsoluteUri);
            });
            try
            {
                await await initialization;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "WebPlugin {PluginId} 初始化失败", _manifest.Id);
                return false;
            }

            Log.Information("WebPlugin {PluginId} 已初始化", _manifest.Id);
            return true;
        }

        private string BuildUiKitInjectionScript()
        {
            try
            {
                var assetsDirectory = Path.Combine(AppContext.BaseDirectory, "WebAssets");
                var cssPath = Path.Combine(assetsDirectory, "long-ui.css");
                var jsPath = Path.Combine(assetsDirectory, "long-ui.js");
                if (!File.Exists(cssPath))
                {
                    Log.Warning("[Web:{Id}] Long Web UI Kit 未找到: {Path}", _manifest.Id, cssPath);
                    return string.Empty;
                }

                var css = File.ReadAllText(cssPath);
                var helpers = File.Exists(jsPath) ? File.ReadAllText(jsPath) : string.Empty;
                var isLight = Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme()
                    == Wpf.Ui.Appearance.ApplicationTheme.Light;
                var cssJson = System.Text.Json.JsonSerializer.Serialize(css);
                var themeJson = System.Text.Json.JsonSerializer.Serialize(isLight ? "light" : "dark");

                return $$"""
                    (function () {
                      const installLongUi = function () {
                        document.documentElement.dataset.longTheme = {{themeJson}};
                        if (!document.getElementById('long-ui-kit')) {
                          const style = document.createElement('style');
                          style.id = 'long-ui-kit';
                          style.textContent = {{cssJson}};
                          (document.head || document.documentElement).appendChild(style);
                        }
                      };
                      if (document.readyState === 'loading')
                        document.addEventListener('DOMContentLoaded', installLongUi, { once: true });
                      else
                        installLongUi();
                      {{helpers}}
                    })();
                    """;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Web:{Id}] Long Web UI Kit 注入准备失败", _manifest.Id);
                return string.Empty;
            }
        }

        private void OnThemeChanged(bool isLight)
        {
            var webView = _webView;
            if (webView?.CoreWebView2 == null) return;

            _ = System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    var theme = isLight ? "light" : "dark";
                    await webView.CoreWebView2.ExecuteScriptAsync(
                        $"window.LongUI?.setTheme('{theme}');");
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "[Web:{Id}] 同步主题失败", _manifest.Id);
                }
            });
        }

        private void OnNavigationStarting(
            object? sender,
            CoreWebView2NavigationStartingEventArgs args)
        {
            if (_navigationPolicy.IsTrustedLocalUri(args.Uri)) return;

            args.Cancel = true;
            Log.Warning("[Web:{Id}] 已阻止越界页面导航：{Uri}", _manifest.Id, args.Uri);
        }

        private void OnNewWindowRequested(
            object? sender,
            CoreWebView2NewWindowRequestedEventArgs args)
        {
            args.Handled = true;
            Log.Warning("[Web:{Id}] 已阻止新窗口请求：{Uri}", _manifest.Id, args.Uri);
        }

        private void OnDownloadStarting(
            object? sender,
            CoreWebView2DownloadStartingEventArgs args)
        {
            args.Cancel = true;
            args.Handled = true;
            Log.Warning("[Web:{Id}] 已阻止浏览器下载；插件应使用 long.http.download", _manifest.Id);
        }

        private void OnWebMessageReceived(
            object? sender,
            CoreWebView2WebMessageReceivedEventArgs args)
        {
            if (!_navigationPolicy.IsTrustedLocalUri(args.Source))
            {
                Log.Warning("[Web:{Id}] 已拒绝非插件页面的 Bridge 消息：{Source}",
                    _manifest.Id, args.Source);
                return;
            }

            HandleJsMessage(args.WebMessageAsJson);
        }

        public void Dispose()
        {
            if (_themeSubscribed)
            {
                App.ThemeChanged -= OnThemeChanged;
                _themeSubscribed = false;
            }

            if (_webView?.CoreWebView2 != null)
            {
                _webView.CoreWebView2.NavigationStarting -= OnNavigationStarting;
                _webView.CoreWebView2.NewWindowRequested -= OnNewWindowRequested;
                _webView.CoreWebView2.DownloadStarting -= OnDownloadStarting;
                _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
            }

            _webView?.Dispose();
            _webView = null;
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
                object? result;
                using (PluginAccessContext.Enter(_manifest.Id))
                    result = await DispatchJsCall(msg.Method, msg.Args);
                var response = System.Text.Json.JsonSerializer.Serialize(new
                {
                    id = msg.Id,
                    result,
                });

                PostWebMessage(response);
            }
            catch (Exception ex)
            {
                var error = System.Text.Json.JsonSerializer.Serialize(new
                {
                    id = msg.Id,
                    error = ex.Message,
                });
                PostWebMessage(error);
            }
        }

        private void PostWebMessage(string json)
        {
            var coreWebView = _webView?.CoreWebView2;
            if (coreWebView == null)
            {
                Log.Warning("[Web:{Id}] WebView 尚未就绪，消息已忽略", _manifest.Id);
                return;
            }

            coreWebView.PostWebMessageAsJson(json);
        }

        public async Task SendCommandAsync(PluginCommandInvocation invocation)
        {
            var webView = _webView;
            if (webView?.CoreWebView2 == null) return;

            var json = System.Text.Json.JsonSerializer.Serialize(new
            {
                type = "long.command",
                command = invocation,
            });
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(
                () => webView.CoreWebView2.PostWebMessageAsJson(json));
        }

        private async Task<object?> DispatchJsCall(string method, object?[] args)
        {
            var h = HostProvider.Instance;

            return method switch
            {
                // === long.app ===
                "app.openUrl" => Ok(h.ShellExecute.OpenUrlAsync(Arg(args, 0))),
                "app.openFolder" => Ok(h.ShellExecute.OpenFolderAsync(Arg(args, 0))),
                "app.openWithDefault" => Ok(h.ShellExecute.OpenWithDefaultAsync(Arg(args, 0))),
                "app.showNotification" => Task.FromResult<object?>(UIToast(Arg(args, 0) + "\n" + Arg(args, 1))),
                "app.getVersion" => Task.FromResult<object?>(new { version = App.ProductVersion }),

                // === long.clipboard ===
                "clipboard.getText" => Ok(h.Clipboard.GetTextAsync()),
                "clipboard.setText" => Ok(h.Clipboard.SetTextAsync(Arg(args, 0))),
                "clipboard.clear" => Ok(h.Clipboard.ClearAsync()),

                // === long.shell ===
                "shell.getActiveFolder" => Ok(h.ShellSelection.GetActiveExplorerFolderPathAsync()),
                "shell.getSelectedItems" => OkList(h.ShellSelection.GetSelectedItemsAsync()),
                "shell.getItemScreenRect" => Ok(h.ShellSelection.GetSelectedItemScreenRectAsync()),

                // === long.shell execute ===
                "shell.openUrl" => Ok(h.ShellExecute.OpenUrlAsync(Arg(args, 0))),
                "shell.openFolder" => Ok(h.ShellExecute.OpenFolderAsync(Arg(args, 0))),
                "shell.openWithDefault" => Ok(h.ShellExecute.OpenWithDefaultAsync(Arg(args, 0))),

                // === long.http ===
                "http.get" => Ok(h.Http.GetAsync(Arg(args, 0), ParseHeaders(args, 1))),
                "http.post" => await HttpPost(args),
                "http.download" => await HttpDownload(args),

                // === long.fs.ads ===
                "fs.ads.read" => Ok(h.ADS.ReadAsync(Arg(args, 0), Arg(args, 1, "long_note"))),
                "fs.ads.write" => Ok(h.ADS.WriteAsync(Arg(args, 0), Arg(args, 1, "long_note"), Arg(args, 2))),
                "fs.ads.delete" => Ok(h.ADS.DeleteAsync(Arg(args, 0), Arg(args, 1, "long_note"))),
                "fs.ads.exists" => Ok(h.ADS.ExistsAsync(Arg(args, 0), Arg(args, 1, "long_note"))),
                "fs.ads.isNTFS" => Ok(h.ADS.IsNTFSVolumeAsync(Arg(args, 0))),

                // === long.hotkey ===
                "hotkey.register" => await HotKeyRegister(args),
                "hotkey.unregister" => await HotKeyUnregister(args),
                "hotkey.isConflict" => Ok(h.HotKey.IsConflictAsync(Arg(args, 0))),

                // === long.registry ===
                "registry.read" => Ok(h.Registry.ReadValueAsync(Arg(args, 0), Arg(args, 1))),
                "registry.write" => Ok(h.Registry.WriteValueAsync(Arg(args, 0), Arg(args, 1), Arg(args, 2))),
                "registry.delete" => Ok(h.Registry.DeleteValueAsync(Arg(args, 0), Arg(args, 1))),

                // === long.storage ===
                "storage.get" => Ok(h.Storage.GetAsync(Arg(args, 0))),
                "storage.set" => Ok(h.Storage.SetAsync(Arg(args, 0), Arg(args, 1))),
                "storage.delete" => Ok(h.Storage.DeleteAsync(Arg(args, 0))),
                "storage.containsKey" => Ok(h.Storage.ContainsKeyAsync(Arg(args, 0))),

                // === long.shell file ops ===
                "shell.listFiles" => Task.FromResult<object?>(ShellListFiles(Arg(args, 0))),
                "shell.renameFile" => await ShellRenameFileAsync(Arg(args, 0), Arg(args, 1)),

                // === long.window ===
                "window.getForeground" => Task.FromResult<object?>(WindowGetForeground()),

                // === long.ui ===
                "ui.showToast" => Task.FromResult<object?>(UIToast(Arg(args, 0))),

                // === long.screenshot ===
                "screenshot.captureFull" => Ok(h.ScreenCapture.CaptureFullScreenAsync()),
                "screenshot.captureRegion" => await CaptureRegionToFile(args),

                _ => Task.FromResult<object?>(new { success = false, error = $"未知方法: {method}" }),
            };
        }

        // 辅助方法
        private static string Arg(object?[] args, int i, string def = "") =>
            args.Length > i ? args[i]?.ToString() ?? def : def;

        private static async Task<object> Ok<T>(Task<HostApiResponse<T>> t) { var r = await t; return new { success = r.IsSuccess, data = r.Data, error = r.ErrorMessage }; }

        private static async Task<object> Ok(Task<HostApiResponse> t) { var r = await t; return new { success = r.IsSuccess, error = r.ErrorMessage }; }

        private static async Task<object> OkList<T>(Task<HostApiResponse<List<T>>> t) { var r = await t; return new { success = r.IsSuccess, data = r.Data, error = r.ErrorMessage }; }

        private static object OkObj() => new { success = true };

        private async Task<object> CaptureRegionToFile(object?[] args)
        {
            var x = int.Parse(Arg(args, 0, "0"));
            var y = int.Parse(Arg(args, 1, "0"));
            var w = int.Parse(Arg(args, 2, "0"));
            var h = int.Parse(Arg(args, 3, "0"));
            var result = await HostProvider.Instance.ScreenCapture.CaptureRegionAsync(x, y, w, h);
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
            if (!HostProvider.Instance.HasCapability("file.ops"))
                return new { success = false, error = "插件未声明 file.ops 能力" };
            if (!Directory.Exists(dir)) return new { success = false };
            var files = Directory.GetFiles(dir).Select(f => new { name = Path.GetFileName(f), path = f.Replace("\\", "/") }).ToList();
            return new { success = true, files };
        }

        private async Task<object> ShellRenameFileAsync(string oldPath, string newName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(newName)
                    || newName != Path.GetFileName(newName)
                    || newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    return new { success = false, error = "新文件名无效" };

                var dir = Path.GetDirectoryName(oldPath);
                if (dir == null) return new { success = false, error = "源文件路径无效" };
                var newPath = Path.Combine(dir, newName);
                if (File.Exists(newPath)) return new { success = false, error = "目标文件已存在" };
                var result = await HostProvider.Instance.FileOps.MoveAsync(oldPath, newPath);
                return new { success = result.IsSuccess, error = result.ErrorMessage };
            }
            catch (Exception ex) { return new { success = false, error = ex.Message }; }
        }

        private async Task<object> WindowGetForeground()
        {
            var result = await HostProvider.Instance.WindowInfo.GetForegroundWindowInfoAsync();
            return result.IsSuccess ? result.Data! : new { success = false };
        }

        private object UIToast(string msg)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                Views.FloatingHudWindow.ShowToast(msg));
            return OkObj();
        }

        // === HTTP 辅助方法 ===

        private static Dictionary<string, string>? ParseHeaders(object?[] args, int index)
        {
            if (args.Length <= index || args[index] == null) return null;
            try
            {
                var json = args[index]?.ToString();
                if (string.IsNullOrEmpty(json)) return null;
                return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json!);
            }
            catch { return null; }
        }

        private async Task<object> HttpPost(object?[] args)
        {
            var url = Arg(args, 0);
            var body = Arg(args, 1);
            var contentType = Arg(args, 2, "application/json");
            var headers = ParseHeaders(args, 3);
            var r = await HostProvider.Instance.Http.PostAsync(url, body, contentType, headers);
            return new { success = r.IsSuccess, data = r.Data, error = r.ErrorMessage };
        }

        private async Task<object> HttpDownload(object?[] args)
        {
            var url = Arg(args, 0);
            var r = await HostProvider.Instance.Http.DownloadAsync(url);
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

        private async Task<object> HotKeyRegister(object?[] args)
        {
            var hotkey = Arg(args, 0);
            var r = await HostProvider.Instance.HotKey.RegisterAsync(hotkey, () =>
            {
                PostWebMessage(
                    System.Text.Json.JsonSerializer.Serialize(new { type = "hotkey", hotkey }));
            });
            return new { success = r.IsSuccess, error = r.ErrorMessage };
        }

        private async Task<object> HotKeyUnregister(object?[] args)
        {
            var hotkey = Arg(args, 0);
            var r = await HostProvider.Instance.HotKey.UnregisterAsync(hotkey);
            return new { success = r.IsSuccess, error = r.ErrorMessage };
        }

        private static string BuildJsBridge(IHostApi host, string pluginId)
        {
            var js = @"
(function() {
var _id=0,_pending={};
function call(method,args){
  return new Promise(function(resolve,reject){
    var id=++_id;
    _pending[id]={resolve:resolve,reject:reject};
    window.chrome.webview.postMessage(JSON.stringify({id:id,method:method,args:args||[]}));
  });
}
window.long = {
  app: {
    openUrl: function(url){return call('app.openUrl',[url]);},
    openFolder: function(path){return call('app.openFolder',[path]);},
    openWithDefault: function(path){return call('app.openWithDefault',[path]);},
    showNotification: function(title,body){return call('app.showNotification',[title,body]);},
    getVersion: function(){return call('app.getVersion',[]);}
  },
  clipboard: {
    getText: function(){return call('clipboard.getText',[]);},
    setText: function(t){return call('clipboard.setText',[t]);},
    clear: function(){return call('clipboard.clear',[]);}
  },
  shell: {
    getActiveFolder: function(){return call('shell.getActiveFolder',[]);},
    getSelectedItems: function(){return call('shell.getSelectedItems',[]);},
    getItemScreenRect: function(){return call('shell.getItemScreenRect',[]);},
    listFiles: function(dir){return call('shell.listFiles',[dir]);},
    renameFile: function(oldPath,newName){return call('shell.renameFile',[oldPath,newName]);},
    openUrl: function(url){return call('shell.openUrl',[url]);},
    openFolder: function(path){return call('shell.openFolder',[path]);},
    openWithDefault: function(path){return call('shell.openWithDefault',[path]);}
  },
  fs: { ads: {
    read: function(p,s){return call('fs.ads.read',[p,s||'long_note']);},
    write: function(p,c,s){return call('fs.ads.write',[p,c,s||'long_note']);},
    delete: function(p,s){return call('fs.ads.delete',[p,s||'long_note']);},
    exists: function(p,s){return call('fs.ads.exists',[p,s||'long_note']);},
    isNTFS: function(p){return call('fs.ads.isNTFS',[p]);}
  }},
  hotkey: {
    register: function(h){return call('hotkey.register',[h]);},
    unregister: function(h){return call('hotkey.unregister',[h]);},
    isConflict: function(h){return call('hotkey.isConflict',[h]);}
  },
  registry: {
    read: function(k,v){return call('registry.read',[k,v]);},
    write: function(k,n,v){return call('registry.write',[k,n,v]);},
    delete: function(k,v){return call('registry.delete',[k,v]);}
  },
  storage: {
    get: function(k){return call('storage.get',[k]);},
    set: function(k,v){return call('storage.set',[k,v]);},
    delete: function(k){return call('storage.delete',[k]);},
    containsKey: function(k){return call('storage.containsKey',[k]);}
  },
  ui: {
    showToast: function(m){return call('ui.showToast',[m]);}
  },
  screenshot: {
    captureFull: function(){return call('screenshot.captureFull',[]);},
    captureRegion: function(x,y,w,h){return call('screenshot.captureRegion',[x,y,w,h]);}
  },
  http: {
    get: function(url,headers){return call('http.get',[url,headers]);},
    post: function(url,body,contentType,headers){return call('http.post',[url,body,contentType||'application/json',headers]);},
    download: function(url){return call('http.download',[url]);}
  },
  window: {
    getForeground: function(){return call('window.getForeground',[]);}
  }
};
window.chrome.webview.addEventListener('message',function(e){
  try{
    var m=JSON.parse(e.data);
    if(m.id&&window.long._pending[m.id]){
      if(m.error)window.long._pending[m.id].reject(new Error(m.error));
      else window.long._pending[m.id].resolve(m.result);
      delete window.long._pending[m.id];
    }
    if(m.type==='hotkey')console.log('[Long] key:',m.hotkey);
  }catch(ex){}
});
console.log('[Long] Bridge ready · __PLUGIN_ID__');
})();";

            return js.Replace("__PLUGIN_ID__", pluginId);
        }
    }

    internal class JsMessage
    {
        public int Id { get; set; }
        public string Method { get; set; } = string.Empty;
        public object?[] Args { get; set; } = Array.Empty<object?>();
    }
}
