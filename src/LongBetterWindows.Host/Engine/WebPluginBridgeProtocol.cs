using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using System.Text;
using System.Text.Json.Serialization;

namespace LongBetterWindows.Host.Engine
{
    internal static class WebPluginBridgeProtocol
    {
        private static readonly System.Text.Json.JsonSerializerOptions MessageJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        internal static WebBridgeRequest? ParseRequest(string json) =>
            System.Text.Json.JsonSerializer.Deserialize<WebBridgeRequest>(json, MessageJsonOptions);

        internal static bool IsWithinBridgeMessageLimit(string json) =>
            Encoding.UTF8.GetByteCount(json) <= WebPluginBridgeContext.BridgeMessageLimitBytes;

        internal static string SerializeResult(int id, object? result) =>
            System.Text.Json.JsonSerializer.Serialize(new { id, result });

        internal static string SerializeError(int id, string error) =>
            System.Text.Json.JsonSerializer.Serialize(new { id, error });

        internal static string SerializeCommand(
            string requestId,
            PluginCommandInvocation command) =>
            System.Text.Json.JsonSerializer.Serialize(new
            {
                type = "long.command",
                request_id = requestId,
                command,
            });

        internal static string SerializeLanguageChanged(
            PluginLanguageContext context) =>
            System.Text.Json.JsonSerializer.Serialize(new
            {
                type = "long.language-changed",
                requested_language = context.RequestedLanguage,
                resolved_language = context.ResolvedLanguage,
                resources = context.Resources,
            });

        internal static string SerializeHostVisibilityChanged(bool visible) =>
            System.Text.Json.JsonSerializer.Serialize(new
            {
                type = "long.host-visibility-changed",
                visible,
            });

        internal static WebCommandResultMessage? ParseCommandResult(string json)
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("type", out var type)
                || !string.Equals(
                    type.GetString(),
                    "long.command-result",
                    StringComparison.Ordinal)) return null;
            return System.Text.Json.JsonSerializer.Deserialize<WebCommandResultMessage>(
                json,
                MessageJsonOptions);
        }

        internal static bool TryCreateCommandResult(
            WebCommandResultMessage message,
            out PluginCommandResult result,
            out string? error)
        {
            if (string.IsNullOrWhiteSpace(message.RequestId) || message.RequestId.Length > 64)
            {
                result = PluginCommandResult.Failure("Web command result request id is missing.");
                error = result.Message;
                return false;
            }
            if (message.Message?.Length > 4_096)
            {
                result = PluginCommandResult.Failure("Web command result message is too long.");
                error = result.Message;
                return false;
            }
            if (!message.Success)
            {
                result = PluginCommandResult.Failure(
                    string.IsNullOrWhiteSpace(message.Message)
                        ? "Web command failed."
                        : message.Message);
                error = null;
                return true;
            }
            if (message.Outputs is null || message.Outputs.Count > 64)
            {
                result = PluginCommandResult.Failure("Web command returned more than 64 outputs.");
                error = result.Message;
                return false;
            }

            var outputs = new Dictionary<string, PluginCommandOutput>(StringComparer.Ordinal);
            foreach (var entry in message.Outputs)
            {
                if (!IsIdentifier(entry.Key)
                    || entry.Value is null
                    || entry.Value.Value is null
                    || entry.Value.Value.Length > 65_536)
                {
                    result = PluginCommandResult.Failure("Web command returned an invalid output.");
                    error = result.Message;
                    return false;
                }
                if (!Enum.TryParse<PluginCommandOutputType>(
                    entry.Value.Type,
                    ignoreCase: true,
                    out var outputType)
                    || !Enum.IsDefined(outputType)
                    || (outputType == PluginCommandOutputType.Path
                        && string.IsNullOrWhiteSpace(entry.Value.Value)))
                {
                    result = PluginCommandResult.Failure("Web command returned an invalid output type or value.");
                    error = result.Message;
                    return false;
                }
                outputs.Add(entry.Key, new PluginCommandOutput(outputType, entry.Value.Value));
            }
            result = PluginCommandResult.Success(message.Message, outputs: outputs);
            error = null;
            return true;
        }

        internal static string SerializeHotkey(string hotkey) =>
            System.Text.Json.JsonSerializer.Serialize(new { type = "hotkey", hotkey });

        internal static string SerializeClipboardChanged(ClipboardChangedEventArgs args) =>
            System.Text.Json.JsonSerializer.Serialize(new
            {
                type = "clipboard.changed",
                content_type = args.ContentType.ToString().ToLowerInvariant(),
                text = args.Text,
                timestamp = args.Timestamp,
            });

        internal static string SerializeWidgetEvent(
            WebPluginBridgeContext context,
            string eventName,
            long sequence,
            object? payload)
        {
            if (!IsWidgetEventName(eventName))
                throw new ArgumentException("Unknown Long Widget event.", nameof(eventName));

            return System.Text.Json.JsonSerializer.Serialize(new
            {
                type = eventName,
                detail = new
                {
                    protocol_version = WebPluginBridgeContext.ProtocolVersion,
                    plugin_id = context.PluginId,
                    widget_id = context.WidgetId,
                    instance_id = context.InstanceId,
                    sequence,
                    payload = payload ?? new { },
                },
            });
        }

        internal static bool IsWidgetEventName(string eventName) =>
            eventName is "long.widget-mounted"
                or "long.widget-visibility-changed"
                or "long.widget-resized"
                or "long.widget-theme-changed"
                or "long.widget-locale-changed"
                or "long.widget-settings-changed"
                or "long.widget-suspend"
                or "long.widget-resume"
                or "long.widget-unmount";

        internal static string? GetRequiredCapability(string method) => method switch
        {
            // 文件系统 ADS
            "fs.ads.read" or "fs.ads.write" or "fs.ads.delete" or "fs.ads.exists" or "fs.ads.isNTFS"
                => "fs.ads.access",

            // 注册表
            "registry.read" or "registry.write" or "registry.delete"
                => "system.registry.write",

            // 热键
            "hotkey.register" or "hotkey.unregister" or "hotkey.isConflict"
                => "system.hotkey",

            // 剪贴板
            "clipboard.getText" or "clipboard.setText" or "clipboard.clear"
                => "system.clipboard",
            "clipboard.startMonitoring" or "clipboard.stopMonitoring"
                => "system.clipboard.monitor",

            // Shell 选择
            "shell.getActiveFolder" or "shell.getSelectedItems" or "shell.getItemScreenRect"
                => "shell.selection",

            // 网络 HTTP
            "http.get" or "http.post" or "http.download"
                => "network.http",

            // 本地存储（无需声明，所有插件都可用）
            "storage.get" or "storage.set" or "storage.compareExchange"
                or "storage.delete" or "storage.containsKey"
                => null,

            // 通知
            "app.showNotification" or "ui.showToast"
                => "system.notification",

            // 截图
            "screenshot.captureFull" or "screenshot.captureRegion"
                => "system.screenshot",

            // Shell 执行
            "app.openUrl" or "shell.openUrl" or "app.openFolder" or "shell.openFolder" or
            "app.openWithDefault" or "shell.openWithDefault"
                => "shell.execute",

            // 文件操作
            "shell.listFiles" or "shell.renameFile" or "shell.renameBatch"
                => "file.ops",

            // 窗口信息
            "window.getForeground"
                => "window.info",
            "window.getVisible"
                => "window.info",

            "process.start" or "process.getList" or "process.kill" or "process.killVerified" or
            "process.killPortOwnerVerified"
                => "system.process",
            "fileOps.copy" or "fileOps.move" or "fileOps.delete" or "fileOps.exists"
                => "file.ops",
            "performance.getCpuUsage" or "performance.getMemoryInfo" or "performance.getDiskInfo" or
            "performance.getSystemInfo" or "performance.getTopByCpu" or "performance.getTopByMemory"
                => "system.performance",
            "networkPort.getTcpConnections" or "networkPort.getTcpListeners" or "networkPort.getUdpEndpoints" or
            "networkPort.findOwner" or "networkPort.isInUse" or "networkPort.getSummary"
                => "network.ports",
            "network.getStats" or "network.getSpeed" or "network.getInterfaces"
                => "network.monitor",
            "audio.getVolume" or "audio.setVolume" or "audio.getMute" or "audio.setMute" or
            "audio.increase" or "audio.decrease" or "audio.getDevices" or "audio.setDefaultDevice"
                => "system.audio",
            "power.getStatus" or "power.lock" or "power.sleep" or "power.hibernate" or
            "power.shutdown" or "power.reboot" or "power.preventSleep"
                => "system.power",
            "theme.get" or "theme.set" or "theme.toggle" or "theme.getAccentColor" or "theme.setAccentColor"
                => "system.theme",
            "wallpaper.get" or "wallpaper.set" or "wallpaper.getStyle"
                => "system.wallpaper",
            "brightness.get" or "brightness.set" or "brightness.increase" or "brightness.decrease"
                => "display.brightness",
            "pinyin.get" or "pinyin.getInitials" or "pinyin.match"
                => "text.pinyin",
            "pinyin.filter"
                => "text.pinyin",
            "input.keyPress" or "input.mouseClick" or "input.moveCursor"
                => "system.input",
            "fileSystem.enumerate" or "fileSystem.hash" or "fileSystem.metadata" or
            "fileSystem.findDuplicates" or "fileSystem.batchRename" or "fileSystem.classify" or
            "fileSystem.findLarge" or "fileSystem.searchContent" or
            "fileSystem.planOrganization" or "fileSystem.executeOrganization"
                => "filesystem.advanced",
            "cache.cleanTemp" or "cache.cleanWindowsUpdate" or "cache.cleanBrowser" or
            "cache.emptyRecycleBin" or "cache.getStatistics" or "cache.cleanAll"
                => "system.cache",
            "schedule.create" or "schedule.delete" or "schedule.getAll" or
            "schedule.setEnabled" or "schedule.runNow"
                => "system.schedule",

            // UI 窗口
            "ui.createWindow" or "ui.confirm" or "ui.prompt" or "ui.select" or
            "ui.closeWindow" or "ui.sendMessage"
                => "ui.window",

            // 应用信息（无需权限）
            "app.getVersion" or "app.log" or "host.getInfo"
                or "widget.ready" or "widget.getInstanceState" or "widget.setInstanceState"
                or "widget.openSettings" or "widget.invalidate" or "widget.setBadge"
                => null,

            _ => null
        };

        internal static string BuildInjectionScript(string pluginId)
        {
            var js = @"
(function() {
var _id=0,_pending={},_hotkeys={},_clipboardChanged=null,_hostVisible=true;
function call(method,args){
  return new Promise(function(resolve,reject){
    var id=++_id;
    _pending[id]={resolve:resolve,reject:reject};
    window.chrome.webview.postMessage({id:id,method:method,args:args||[]});
  });
}
window.long = {
  app: {
    openUrl: function(url){return call('app.openUrl',[url]);},
    openFolder: function(path){return call('app.openFolder',[path]);},
    openWithDefault: function(path){return call('app.openWithDefault',[path]);},
    showNotification: function(title,body){return call('app.showNotification',[title,body]);},
    getVersion: function(){return call('app.getVersion',[]);},
    log: function(){return call('app.log',Array.prototype.slice.call(arguments));}
  },
  host: {
    getInfo: function(){return call('host.getInfo',[]);},
    isVisible: function(){return _hostVisible;}
  },
  clipboard: {
    getText: function(){return call('clipboard.getText',[]);},
    setText: function(t){return call('clipboard.setText',[t]);},
    clear: function(){return call('clipboard.clear',[]);},
    startMonitoring: function(callback){if(typeof callback==='function')_clipboardChanged=callback;return call('clipboard.startMonitoring',[]);},
    stopMonitoring: function(){_clipboardChanged=null;return call('clipboard.stopMonitoring',[]);}
  },
  shell: {
    getActiveFolder: function(){return call('shell.getActiveFolder',[]);},
    getSelectedItems: function(){return call('shell.getSelectedItems',[]);},
    getItemScreenRect: function(){return call('shell.getItemScreenRect',[]);},
    listFiles: function(dir){return call('shell.listFiles',[dir]);},
    renameFile: function(oldPath,newName){return call('shell.renameFile',[oldPath,newName]);},
    renameBatch: function(operations){return call('shell.renameBatch',[operations||[]]);},
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
    register: function(h,callback){if(typeof callback==='function')_hotkeys[h]=callback;return call('hotkey.register',[h]);},
    unregister: function(h){delete _hotkeys[h];return call('hotkey.unregister',[h]);},
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
    compareExchange: function(k,e,v){return call('storage.compareExchange',[k,e,v]);},
    delete: function(k){return call('storage.delete',[k]);},
    containsKey: function(k){return call('storage.containsKey',[k]);}
  },
  process: {
    start: function(path,args){return call('process.start',[path,args]);},
    getList: function(filter){return call('process.getList',[filter]);},
    kill: function(id){return call('process.kill',[id]);},
    killVerified: function(id,name,identity){return call('process.killVerified',[id,name,identity]);},
    killPortOwnerVerified: function(port){return call('process.killPortOwnerVerified',[
      port.ProcessId,port.ProcessName,port.ProcessIdentity,
      port.LocalPort,port.LocalAddress,port.RemotePort,port.RemoteAddress,
      port.Protocol,port.State
    ]);}
  },
  fileOps: {
    copy: function(source,dest){return call('fileOps.copy',[source,dest]);},
    move: function(source,dest){return call('fileOps.move',[source,dest]);},
    delete: function(path){return call('fileOps.delete',[path]);},
    exists: function(path){return call('fileOps.exists',[path]);}
  },
  performance: {
    getCpuUsage: function(){return call('performance.getCpuUsage',[]);},
    getMemoryInfo: function(){return call('performance.getMemoryInfo',[]);},
    getDiskInfo: function(){return call('performance.getDiskInfo',[]);},
    getSystemInfo: function(){return call('performance.getSystemInfo',[]);},
    getTopByCpu: function(count){return call('performance.getTopByCpu',[count||10]);},
    getTopByMemory: function(count){return call('performance.getTopByMemory',[count||10]);}
  },
  networkPort: {
    getTcpConnections: function(){return call('networkPort.getTcpConnections',[]);},
    getTcpListeners: function(){return call('networkPort.getTcpListeners',[]);},
    getUdpEndpoints: function(){return call('networkPort.getUdpEndpoints',[]);},
    findPortOwner: function(port,protocol){return call('networkPort.findOwner',[port,protocol||'tcp']);},
    isPortInUse: function(port,protocol){return call('networkPort.isInUse',[port,protocol||'tcp']);},
    getSummary: function(){return call('networkPort.getSummary',[]);}
  },
  network: {
    getStats: function(){return call('network.getStats',[]);},
    getSpeed: function(){return call('network.getSpeed',[]);},
    getInterfaces: function(){return call('network.getInterfaces',[]);}
  },
  audio: {
    getVolume: function(){return call('audio.getVolume',[]);},
    setVolume: function(volume){return call('audio.setVolume',[volume]);},
    getMute: function(){return call('audio.getMute',[]);},
    setMute: function(mute){return call('audio.setMute',[mute]);},
    increase: function(step){return call('audio.increase',[step||5]);},
    decrease: function(step){return call('audio.decrease',[step||5]);},
    getDevices: function(){return call('audio.getDevices',[]);},
    setDefaultDevice: function(id){return call('audio.setDefaultDevice',[id]);}
  },
  power: {
    getStatus: function(){return call('power.getStatus',[]);},
    getBatteryStatus: function(){return call('power.getStatus',[]);},
    lock: function(){return call('power.lock',[]);},
    sleep: function(){return call('power.sleep',[]);},
    hibernate: function(){return call('power.hibernate',[]);},
    shutdown: function(delay){return call('power.shutdown',[delay||0]);},
    reboot: function(delay){return call('power.reboot',[delay||0]);},
    preventSleep: function(prevent){return call('power.preventSleep',[prevent]);}
  },
  theme: {
    get: function(){return call('theme.get',[]);},
    set: function(theme){return call('theme.set',[theme]);},
    toggle: function(){return call('theme.toggle',[]);},
    getAccentColor: function(){return call('theme.getAccentColor',[]);},
    setAccentColor: function(color){return call('theme.setAccentColor',[color]);}
  },
  wallpaper: {
    get: function(){return call('wallpaper.get',[]);},
    set: function(path,style){return call('wallpaper.set',[path,style||'fill']);},
    getStyle: function(){return call('wallpaper.getStyle',[]);}
  },
  brightness: {
    get: function(){return call('brightness.get',[]);},
    set: function(value){return call('brightness.set',[value]);},
    increase: function(step){return call('brightness.increase',[step||10]);},
    decrease: function(step){return call('brightness.decrease',[step||10]);}
  },
  pinyin: {
    get: function(text){return call('pinyin.get',[text]);},
    getInitials: function(text){return call('pinyin.getInitials',[text]);},
    match: function(text,query){return call('pinyin.match',[text,query]);},
    filter: function(items,query){return call('pinyin.filter',[items,query]);}
  },
  input: {
    keyPress: function(vkCode){return call('input.keyPress',[vkCode]);},
    mouseClick: function(x,y,rightButton){return call('input.mouseClick',[x,y,!!rightButton]);},
    moveCursor: function(x,y){return call('input.moveCursor',[x,y]);}
  },
  fileSystem: {
    enumerate: function(path,pattern,recursive){return call('fileSystem.enumerate',[path,pattern||'*.*',recursive!==false]);},
    hash: function(path){return call('fileSystem.hash',[path]);},
    metadata: function(path){return call('fileSystem.metadata',[path]);},
    findDuplicates: function(path){return call('fileSystem.findDuplicates',[path]);},
    batchRename: function(operations){return call('fileSystem.batchRename',[operations||[]]);},
    classify: function(path,mode){return call('fileSystem.classify',[path,mode||'ByExtension']);},
    findLarge: function(path,minSizeBytes){return call('fileSystem.findLarge',[path,minSizeBytes]);},
    searchContent: function(path,keyword,extensions){return call('fileSystem.searchContent',[path,keyword,extensions||[]]);},
    planOrganization: function(path,mode){return call('fileSystem.planOrganization',[path,mode||'ByExtension']);},
    executeOrganization: function(path,mode,items){return call('fileSystem.executeOrganization',[path,mode||'ByExtension',items||[]]);}
  },
  cache: {
    cleanTemp: function(){return call('cache.cleanTemp',[]);},
    cleanWindowsUpdate: function(){return call('cache.cleanWindowsUpdate',[]);},
    cleanBrowser: function(browser){return call('cache.cleanBrowser',[browser]);},
    emptyRecycleBin: function(){return call('cache.emptyRecycleBin',[]);},
    getStatistics: function(){return call('cache.getStatistics',[]);},
    cleanAll: function(){return call('cache.cleanAll',[]);}
  },
  schedule: {
    create: function(task){return call('schedule.create',[task]);},
    delete: function(taskId){return call('schedule.delete',[taskId]);},
    getAll: function(){return call('schedule.getAll',[]);},
    setEnabled: function(taskId,enabled){return call('schedule.setEnabled',[taskId,enabled]);},
    runNow: function(taskId){return call('schedule.runNow',[taskId]);}
  },
  ui: {
    showToast: function(m){return call('ui.showToast',[m]);},
    createWindow: function(title,htmlContent,width,height,resizable){return call('ui.createWindow',[title,htmlContent,width,height,resizable]);},
    confirm: function(message,title){return call('ui.confirm',[message,title||'确认']);},
    prompt: function(message,title,defaultValue){return call('ui.prompt',[message,title||'输入',defaultValue||'']);},
    select: function(message,options,title){return call('ui.select',[message,options||[],title||'选择']);},
    closeWindow: function(windowId){return call('ui.closeWindow',[windowId]);},
    sendMessage: function(windowId,message){return call('ui.sendMessage',[windowId,message]);}
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
    getForeground: function(){return call('window.getForeground',[]);},
    getVisible: function(){return call('window.getVisible',[]);}
  },
  widget: {
    ready: function(contentVersion){return call('widget.ready',[{content_version:contentVersion||1}]);},
    getInstanceState: function(){return call('widget.getInstanceState',[]);},
    setInstanceState: function(state){return call('widget.setInstanceState',[{state:state}]);},
    openSettings: function(){return call('widget.openSettings',[]);},
    invalidate: function(reason){return call('widget.invalidate',[{reason:reason||'manual'}]);},
    setBadge: function(badge){return call('widget.setBadge',[badge||{}]);}
  }
};
window.chrome.webview.addEventListener('message',function(e){
  try{
    var m=typeof e.data==='string'?JSON.parse(e.data):e.data;
    if(m.id&&_pending[m.id]){
      if(m.error)_pending[m.id].reject(new Error(m.error));
      else _pending[m.id].resolve(m.result);
      delete _pending[m.id];
    }
    if(m.type==='hotkey'){
      if(typeof _hotkeys[m.hotkey]==='function')_hotkeys[m.hotkey]();
      else console.log('[Long] key:',m.hotkey);
    }
    if(m.type==='clipboard.changed'&&typeof _clipboardChanged==='function')_clipboardChanged(m);
    if(m.type==='long.host-visibility-changed'){
      _hostVisible=m.visible===true;
      window.dispatchEvent(new CustomEvent('long-host-visibilitychange',{detail:{visible:_hostVisible}}));
    }
    if(typeof m.type==='string'&&m.type.indexOf('long.widget-')===0&&m.detail){
      window.dispatchEvent(new CustomEvent(m.type,{detail:m.detail}));
    }
  }catch(ex){}
});
console.log('[Long] Bridge ready · __PLUGIN_ID__');
})();";

            return js.Replace("__PLUGIN_ID__", pluginId);
        }

        private static bool IsIdentifier(string value)
            => !string.IsNullOrWhiteSpace(value)
                && value.Length <= 64
                && (char.IsLetter(value[0]) || value[0] == '_')
                && value.All(character => char.IsLetterOrDigit(character)
                    || character is '_' or '-' or '.');
    }

    internal sealed class WebPluginBridgeContext
    {
        internal const string ProtocolVersion = "1.0";
        internal const string ApiVersion = "1.1.0";
        internal const int InstanceStateLimitBytes = 256 * 1024;
        internal const int BridgeMessageLimitBytes = 1024 * 1024;

        internal WebPluginBridgeContext(
            string pluginId,
            string surface = "plugin",
            string? widgetId = null,
            string? instanceId = null,
            string hostId = "long-assistant",
            string? hostVersion = null)
        {
            PluginId = pluginId;
            Surface = surface;
            WidgetId = widgetId;
            InstanceId = instanceId;
            HostId = hostId;
            HostVersion = string.IsNullOrWhiteSpace(hostVersion)
                ? App.ProductVersion
                : hostVersion;
        }

        internal string PluginId { get; }
        internal string Surface { get; }
        internal string? WidgetId { get; }
        internal string? InstanceId { get; }
        internal string HostId { get; }
        internal string HostVersion { get; }
        internal bool IsWidget => string.Equals(Surface, "widget", StringComparison.Ordinal);

        internal object ToHostInfo() => new
        {
            protocol_version = ProtocolVersion,
            api_version = ApiVersion,
            host = new
            {
                id = HostId,
                version = HostVersion,
            },
            plugin_id = PluginId,
            surface = Surface,
            widget_id = WidgetId,
            instance_id = InstanceId,
            surfaces = new[] { "plugin", "action-card", "widget" },
            features = IsWidget
                ? new[]
                {
                    "widget.instance-state",
                    "widget.visibility",
                    "widget.resize",
                    "theme.v1",
                    "locale.v1",
                }
                : new[]
                {
                    "theme.v1",
                    "locale.v1",
                },
            limits = new
            {
                instance_state_bytes = InstanceStateLimitBytes,
                bridge_message_bytes = BridgeMessageLimitBytes,
            },
        };
    }

    internal sealed class WebBridgeRequest
    {
        public int Id { get; set; }
        public string Method { get; set; } = string.Empty;
        public object?[] Args { get; set; } = Array.Empty<object?>();
    }

    internal sealed class WebCommandResultMessage
    {
        [JsonPropertyName("request_id")]
        public string RequestId { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string? Message { get; set; }
        public Dictionary<string, WebCommandOutputMessage>? Outputs { get; set; } = new();
    }

    internal sealed class WebCommandOutputMessage
    {
        public string? Type { get; set; } = string.Empty;
        public string? Value { get; set; } = string.Empty;
    }
}
