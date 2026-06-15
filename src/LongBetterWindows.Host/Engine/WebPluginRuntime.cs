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

            // 在页面加载前注入初始化脚本（fire-and-forget，与导航并行）
            _ = _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
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
            var h = HostProvider.Instance;

            return method switch
            {
                // === long.app ===
                "app.openUrl" => Task.FromResult<object?>(AppOpenUrl(Arg(args, 0))),
                "app.openFolder" => Task.FromResult<object?>(AppOpenFolder(Arg(args, 0))),
                "app.openWithDefault" => Task.FromResult<object?>(AppOpenWithDefault(Arg(args, 0))),
                "app.showNotification" => Task.FromResult<object?>(UIToast(Arg(args, 0) + "\n" + Arg(args, 1))),
                "app.getVersion" => Task.FromResult<object?>(new { version = "0.2.0" }),

                // === long.clipboard ===
                "clipboard.getText" => Ok(h.Clipboard!.GetTextAsync()),
                "clipboard.setText" => Ok(h.Clipboard!.SetTextAsync(Arg(args, 0))),
                "clipboard.clear" => Ok(h.Clipboard!.ClearAsync()),

                // === long.shell ===
                "shell.getActiveFolder" => Ok(h.ShellSelection!.GetActiveExplorerFolderPathAsync()),
                "shell.getSelectedItems" => OkList(h.ShellSelection!.GetSelectedItemsAsync()),
                "shell.getItemScreenRect" => Ok(h.ShellSelection!.GetSelectedItemScreenRectAsync()),

                // === long.fs.ads ===
                "fs.ads.read" => Ok(h.ADS!.ReadAsync(Arg(args, 0), Arg(args, 1, "long_note"))),
                "fs.ads.write" => Ok(h.ADS!.WriteAsync(Arg(args, 0), Arg(args, 1, "long_note"), Arg(args, 2))),
                "fs.ads.delete" => Ok(h.ADS!.DeleteAsync(Arg(args, 0), Arg(args, 1, "long_note"))),
                "fs.ads.exists" => Ok(h.ADS!.ExistsAsync(Arg(args, 0), Arg(args, 1, "long_note"))),
                "fs.ads.isNTFS" => Ok(h.ADS!.IsNTFSVolumeAsync(Arg(args, 0))),

                // === long.hotkey ===
                "hotkey.register" => await HotKeyRegister(args),
                "hotkey.unregister" => await HotKeyUnregister(args),
                "hotkey.isConflict" => Ok(h.HotKey!.IsConflictAsync(Arg(args, 0))),

                // === long.registry ===
                "registry.read" => Ok(h.Registry!.ReadValueAsync(Arg(args, 0), Arg(args, 1))),
                "registry.write" => Ok(h.Registry!.WriteValueAsync(Arg(args, 0), Arg(args, 1), Arg(args, 2))),
                "registry.delete" => Ok(h.Registry!.DeleteValueAsync(Arg(args, 0), Arg(args, 1))),

                // === long.storage ===
                "storage.get" => Ok(h.Storage!.GetAsync(Arg(args, 0))),
                "storage.set" => Ok(h.Storage!.SetAsync(Arg(args, 0), Arg(args, 1))),
                "storage.delete" => Ok(h.Storage!.DeleteAsync(Arg(args, 0))),
                "storage.containsKey" => Ok(h.Storage!.ContainsKeyAsync(Arg(args, 0))),

                // === long.shell file ops ===
                "shell.listFiles" => Task.FromResult<object?>(ShellListFiles(Arg(args, 0))),
                "shell.renameFile" => Task.FromResult<object?>(ShellRenameFile(Arg(args, 0), Arg(args, 1))),

                // === long.window ===
                "window.getForeground" => Task.FromResult<object?>(WindowGetForeground()),

                // === long.ui ===
                "ui.showToast" => Task.FromResult<object?>(UIToast(Arg(args, 0))),

                _ => Task.FromResult<object?>(new { success = false, error = $"未知方法: {method}" }),
            };
        }

        // 辅助方法
        private static string Arg(object?[] args, int i, string def = "") =>
            args.Length > i ? args[i]?.ToString() ?? def : def;

        private static async Task<object> Ok<T>(Task<HostApiResponse<T>> t) { var r = await t; return new { r.IsSuccess, data = r.Data, error = r.ErrorMessage }; }

        private static async Task<object> Ok(Task<HostApiResponse> t) { var r = await t; return new { r.IsSuccess, error = r.ErrorMessage }; }

        private static async Task<object> OkList<T>(Task<HostApiResponse<List<T>>> t) { var r = await t; return new { r.IsSuccess, data = r.Data, error = r.ErrorMessage }; }

        private static object OkObj() => new { success = true };

        private object ShellListFiles(string dir)
        {
            if (!Directory.Exists(dir)) return new { success = false };
            var files = Directory.GetFiles(dir).Select(f => new { name = Path.GetFileName(f), path = f.Replace("\\", "/") }).ToList();
            return new { success = true, files };
        }

        private object ShellRenameFile(string oldPath, string newName)
        {
            try
            {
                var dir = Path.GetDirectoryName(oldPath);
                if (dir == null) return new { success = false };
                var newPath = Path.Combine(dir, newName);
                if (File.Exists(newPath)) return new { success = false, error = "目标文件已存在" };
                File.Move(oldPath, newPath);
                return new { success = true };
            }
            catch (Exception ex) { return new { success = false, error = ex.Message }; }
        }

        private async Task<object> WindowGetForeground()
        {
            var result = await HostProvider.Instance.WindowInfo!.GetForegroundWindowInfoAsync();
            return result.IsSuccess ? result.Data! : new { success = false };
        }

        private object UIToast(string msg)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                Views.FloatingHudWindow.ShowToast(msg));
            return OkObj();
        }

        private object AppOpenUrl(string url)
        {
            if (!string.IsNullOrEmpty(url))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                { FileName = url, UseShellExecute = true });
            return OkObj();
        }

        private object AppOpenFolder(string path)
        {
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                System.Diagnostics.Process.Start("explorer.exe", path);
            return OkObj();
        }

        private object AppOpenWithDefault(string path)
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                { FileName = path, UseShellExecute = true });
            return OkObj();
        }

        private async Task<object> HotKeyRegister(object?[] args)
        {
            var hotkey = Arg(args, 0);
            var r = await HostProvider.Instance.HotKey!.RegisterAsync(hotkey, () =>
            {
                _webView.CoreWebView2.PostWebMessageAsJson(
                    System.Text.Json.JsonSerializer.Serialize(new { type = "hotkey", hotkey }));
            });
            return new { r.IsSuccess, error = r.ErrorMessage };
        }

        private async Task<object> HotKeyUnregister(object?[] args)
        {
            var hotkey = Arg(args, 0);
            var r = await HostProvider.Instance.HotKey!.UnregisterAsync(hotkey);
            return new { r.IsSuccess, error = r.ErrorMessage };
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
    renameFile: function(oldPath,newName){return call('shell.renameFile',[oldPath,newName]);}
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
