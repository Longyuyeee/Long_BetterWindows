export const BRIDGE_METHODS = Object.freeze([
  "app.openUrl", "app.openFolder", "app.openWithDefault",
  "app.showNotification", "app.getVersion", "app.log",
  "clipboard.getText", "clipboard.setText", "clipboard.clear",
  "clipboard.startMonitoring", "clipboard.stopMonitoring",
  "shell.getActiveFolder", "shell.getSelectedItems",
  "shell.getItemScreenRect", "shell.listFiles", "shell.renameFile",
  "shell.renameBatch", "shell.openUrl", "shell.openFolder",
  "shell.openWithDefault",
  "fs.ads.read", "fs.ads.write", "fs.ads.delete", "fs.ads.exists",
  "fs.ads.isNTFS",
  "hotkey.register", "hotkey.unregister", "hotkey.isConflict",
  "registry.read", "registry.write", "registry.delete",
  "storage.get", "storage.set", "storage.compareExchange",
  "storage.delete", "storage.containsKey",
  "process.start", "process.getList", "process.kill", "process.killVerified",
  "fileOps.copy", "fileOps.move", "fileOps.delete", "fileOps.exists",
  "performance.getCpuUsage", "performance.getMemoryInfo",
  "performance.getDiskInfo", "performance.getSystemInfo",
  "performance.getTopByCpu", "performance.getTopByMemory",
  "networkPort.getTcpConnections", "networkPort.getTcpListeners",
  "networkPort.getUdpEndpoints", "networkPort.findOwner",
  "networkPort.isInUse", "networkPort.getSummary",
  "network.getStats", "network.getSpeed", "network.getInterfaces",
  "audio.getVolume", "audio.setVolume", "audio.getMute",
  "audio.setMute", "audio.increase", "audio.decrease",
  "audio.getDevices", "audio.setDefaultDevice",
  "power.getStatus", "power.lock", "power.sleep", "power.hibernate",
  "power.shutdown", "power.reboot", "power.preventSleep",
  "theme.get", "theme.set", "theme.toggle", "theme.getAccentColor",
  "theme.setAccentColor",
  "wallpaper.get", "wallpaper.set", "wallpaper.getStyle",
  "brightness.get", "brightness.set", "brightness.increase",
  "brightness.decrease",
  "pinyin.get", "pinyin.getInitials", "pinyin.match", "pinyin.filter",
  "input.keyPress", "input.mouseClick", "input.moveCursor",
  "fileSystem.enumerate", "fileSystem.hash", "fileSystem.metadata",
  "fileSystem.findDuplicates", "fileSystem.batchRename",
  "fileSystem.classify", "fileSystem.findLarge",
  "fileSystem.searchContent", "fileSystem.planOrganization",
  "fileSystem.executeOrganization",
  "cache.cleanTemp", "cache.cleanWindowsUpdate", "cache.cleanBrowser",
  "cache.emptyRecycleBin", "cache.getStatistics", "cache.cleanAll",
  "schedule.create", "schedule.delete", "schedule.getAll",
  "schedule.setEnabled", "schedule.runNow",
  "ui.showToast", "ui.createWindow", "ui.confirm", "ui.prompt",
  "ui.select", "ui.closeWindow", "ui.sendMessage",
  "screenshot.captureFull", "screenshot.captureRegion",
  "http.get", "http.post", "http.download",
  "window.getForeground", "window.getVisible"
]);

const API_TO_BRIDGE = Object.freeze({
  "app.openUrl": "app.openUrl",
  "app.openFolder": "app.openFolder",
  "app.openWithDefault": "app.openWithDefault",
  "app.showNotification": "app.showNotification",
  "app.getVersion": "app.getVersion",
  "app.log": "app.log",
  "clipboard.getText": "clipboard.getText",
  "clipboard.setText": "clipboard.setText",
  "clipboard.clear": "clipboard.clear",
  "clipboard.startMonitoring": "clipboard.startMonitoring",
  "clipboard.stopMonitoring": "clipboard.stopMonitoring",
  "shell.getActiveFolder": "shell.getActiveFolder",
  "shell.getSelectedItems": "shell.getSelectedItems",
  "shell.getItemScreenRect": "shell.getItemScreenRect",
  "shell.listFiles": "shell.listFiles",
  "shell.renameFile": "shell.renameFile",
  "shell.renameBatch": "shell.renameBatch",
  "shell.openUrl": "shell.openUrl",
  "shell.openFolder": "shell.openFolder",
  "shell.openWithDefault": "shell.openWithDefault",
  "fs.ads.read": "fs.ads.read",
  "fs.ads.write": "fs.ads.write",
  "fs.ads.delete": "fs.ads.delete",
  "fs.ads.exists": "fs.ads.exists",
  "fs.ads.isNTFS": "fs.ads.isNTFS",
  "hotkey.register": "hotkey.register",
  "hotkey.unregister": "hotkey.unregister",
  "hotkey.isConflict": "hotkey.isConflict",
  "registry.read": "registry.read",
  "registry.write": "registry.write",
  "registry.delete": "registry.delete",
  "storage.get": "storage.get",
  "storage.set": "storage.set",
  "storage.compareExchange": "storage.compareExchange",
  "storage.delete": "storage.delete",
  "storage.containsKey": "storage.containsKey",
  "process.start": "process.start",
  "process.getList": "process.getList",
  "process.kill": "process.kill",
  "process.killVerified": "process.killVerified",
  "fileOps.copy": "fileOps.copy",
  "fileOps.move": "fileOps.move",
  "fileOps.delete": "fileOps.delete",
  "fileOps.exists": "fileOps.exists",
  "performance.getCpuUsage": "performance.getCpuUsage",
  "performance.getMemoryInfo": "performance.getMemoryInfo",
  "performance.getDiskInfo": "performance.getDiskInfo",
  "performance.getSystemInfo": "performance.getSystemInfo",
  "performance.getTopByCpu": "performance.getTopByCpu",
  "performance.getTopByMemory": "performance.getTopByMemory",
  "networkPort.getTcpConnections": "networkPort.getTcpConnections",
  "networkPort.getTcpListeners": "networkPort.getTcpListeners",
  "networkPort.getUdpEndpoints": "networkPort.getUdpEndpoints",
  "networkPort.findPortOwner": "networkPort.findOwner",
  "networkPort.isPortInUse": "networkPort.isInUse",
  "networkPort.getSummary": "networkPort.getSummary",
  "network.getStats": "network.getStats",
  "network.getSpeed": "network.getSpeed",
  "network.getInterfaces": "network.getInterfaces",
  "audio.getVolume": "audio.getVolume",
  "audio.setVolume": "audio.setVolume",
  "audio.getMute": "audio.getMute",
  "audio.setMute": "audio.setMute",
  "audio.increase": "audio.increase",
  "audio.decrease": "audio.decrease",
  "audio.getDevices": "audio.getDevices",
  "audio.setDefaultDevice": "audio.setDefaultDevice",
  "power.getStatus": "power.getStatus",
  "power.getBatteryStatus": "power.getStatus",
  "power.lock": "power.lock",
  "power.sleep": "power.sleep",
  "power.hibernate": "power.hibernate",
  "power.shutdown": "power.shutdown",
  "power.reboot": "power.reboot",
  "power.preventSleep": "power.preventSleep",
  "theme.get": "theme.get",
  "theme.set": "theme.set",
  "theme.toggle": "theme.toggle",
  "theme.getAccentColor": "theme.getAccentColor",
  "theme.setAccentColor": "theme.setAccentColor",
  "wallpaper.get": "wallpaper.get",
  "wallpaper.set": "wallpaper.set",
  "wallpaper.getStyle": "wallpaper.getStyle",
  "brightness.get": "brightness.get",
  "brightness.set": "brightness.set",
  "brightness.increase": "brightness.increase",
  "brightness.decrease": "brightness.decrease",
  "pinyin.get": "pinyin.get",
  "pinyin.getInitials": "pinyin.getInitials",
  "pinyin.match": "pinyin.match",
  "pinyin.filter": "pinyin.filter",
  "input.keyPress": "input.keyPress",
  "input.mouseClick": "input.mouseClick",
  "input.moveCursor": "input.moveCursor",
  "fileSystem.enumerate": "fileSystem.enumerate",
  "fileSystem.hash": "fileSystem.hash",
  "fileSystem.metadata": "fileSystem.metadata",
  "fileSystem.findDuplicates": "fileSystem.findDuplicates",
  "fileSystem.batchRename": "fileSystem.batchRename",
  "fileSystem.classify": "fileSystem.classify",
  "fileSystem.findLarge": "fileSystem.findLarge",
  "fileSystem.searchContent": "fileSystem.searchContent",
  "fileSystem.planOrganization": "fileSystem.planOrganization",
  "fileSystem.executeOrganization": "fileSystem.executeOrganization",
  "cache.cleanTemp": "cache.cleanTemp",
  "cache.cleanWindowsUpdate": "cache.cleanWindowsUpdate",
  "cache.cleanBrowser": "cache.cleanBrowser",
  "cache.emptyRecycleBin": "cache.emptyRecycleBin",
  "cache.getStatistics": "cache.getStatistics",
  "cache.cleanAll": "cache.cleanAll",
  "schedule.create": "schedule.create",
  "schedule.delete": "schedule.delete",
  "schedule.getAll": "schedule.getAll",
  "schedule.setEnabled": "schedule.setEnabled",
  "schedule.runNow": "schedule.runNow",
  "ui.showToast": "ui.showToast",
  "ui.createWindow": "ui.createWindow",
  "ui.confirm": "ui.confirm",
  "ui.prompt": "ui.prompt",
  "ui.select": "ui.select",
  "ui.closeWindow": "ui.closeWindow",
  "ui.sendMessage": "ui.sendMessage",
  "screenshot.captureFull": "screenshot.captureFull",
  "screenshot.captureRegion": "screenshot.captureRegion",
  "http.get": "http.get",
  "http.post": "http.post",
  "http.download": "http.download",
  "window.getForeground": "window.getForeground",
  "window.getVisible": "window.getVisible"
});

for (const method of BRIDGE_METHODS) {
  if (!Object.values(API_TO_BRIDGE).includes(method)) {
    throw new Error(`Long Mock API mapping is missing: ${method}`);
  }
}

export function ok(data) {
  return data === undefined ? { success: true } : { success: true, data };
}

export function fail(error) {
  return { success: false, error };
}

function setPath(root, path, value) {
  const segments = path.split(".");
  const name = segments.pop();
  let target = root;
  for (const segment of segments) {
    target[segment] ??= {};
    target = target[segment];
  }
  target[name] = value;
}

export function createLongMock(options = {}) {
  const calls = [];
  const handlers = new Map(Object.entries(options.handlers ?? {}));
  const storage = new Map(Object.entries(options.storage ?? {}));
  const hotkeys = new Map();
  let clipboardCallback = null;
  let clipboardText = options.clipboardText ?? "";

  async function invoke(method, args) {
    calls.push(Object.freeze({
      method,
      args: Object.freeze([...args])
    }));
    const handler = handlers.get(method);
    if (handler) return await handler(...args);

    switch (method) {
      case "app.getVersion":
        return ok(options.version ?? "1.0.0");
      case "clipboard.getText":
        return ok(clipboardText);
      case "clipboard.setText":
        clipboardText = args[0] ?? "";
        return ok();
      case "clipboard.clear":
        clipboardText = "";
        return ok();
      case "storage.get":
        return ok(storage.has(args[0]) ? storage.get(args[0]) : null);
      case "storage.set":
        storage.set(args[0], args[1]);
        return ok();
      case "storage.compareExchange": {
        const current = storage.has(args[0]) ? storage.get(args[0]) : null;
        if (current !== args[1]) return ok(false);
        storage.set(args[0], args[2]);
        return ok(true);
      }
      case "storage.delete":
        storage.delete(args[0]);
        return ok();
      case "storage.containsKey":
        return ok(storage.has(args[0]));
      default:
        return ok();
    }
  }

  const long = {};
  for (const [apiPath, bridgeMethod] of Object.entries(API_TO_BRIDGE)) {
    setPath(long, apiPath, (...args) => invoke(bridgeMethod, args));
  }

  long.hotkey.register = (hotkey, callback) => {
    if (typeof callback === "function") hotkeys.set(hotkey, callback);
    return invoke("hotkey.register", [hotkey]);
  };
  long.hotkey.unregister = (hotkey) => {
    hotkeys.delete(hotkey);
    return invoke("hotkey.unregister", [hotkey]);
  };
  long.clipboard.startMonitoring = (callback) => {
    if (typeof callback === "function") clipboardCallback = callback;
    return invoke("clipboard.startMonitoring", []);
  };
  long.clipboard.stopMonitoring = () => {
    clipboardCallback = null;
    return invoke("clipboard.stopMonitoring", []);
  };

  const controller = {
    long,
    calls,
    getCalls(method) {
      return calls
        .filter(call => method === undefined || call.method === method)
        .map(call => ({ method: call.method, args: [...call.args] }));
    },
    setHandler(method, handler) {
      if (!BRIDGE_METHODS.includes(method)) {
        throw new Error(`Unknown Long bridge method: ${method}`);
      }
      if (typeof handler !== "function") {
        throw new TypeError("Mock handler must be a function.");
      }
      handlers.set(method, handler);
    },
    clearHandler(method) {
      handlers.delete(method);
    },
    reset() {
      calls.length = 0;
      handlers.clear();
      for (const [method, handler] of Object.entries(options.handlers ?? {})) {
        handlers.set(method, handler);
      }
      hotkeys.clear();
      clipboardCallback = null;
      storage.clear();
      for (const [key, value] of Object.entries(options.storage ?? {})) {
        storage.set(key, value);
      }
      clipboardText = options.clipboardText ?? "";
    },
    emitHotkey(hotkey) {
      const callback = hotkeys.get(hotkey);
      if (!callback) return false;
      callback();
      return true;
    },
    emitClipboardChanged(event = {}) {
      if (!clipboardCallback) return false;
      clipboardCallback({
        type: "clipboard.changed",
        content_type: "text",
        text: clipboardText,
        timestamp: new Date(0).toISOString(),
        ...event
      });
      return true;
    },
    install(target = globalThis) {
      target.long = long;
      return long;
    }
  };

  return controller;
}

export function installLongMock(options = {}, target = globalThis) {
  const controller = createLongMock(options);
  controller.install(target);
  return controller;
}
