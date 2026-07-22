(function () {
  const LongUI = window.LongUI || {};

  LongUI.setTheme = function (theme) {
    document.documentElement.dataset.longTheme = theme === 'light' ? 'light' : 'dark';
  };

  LongUI.setReducedMotion = function (reduced) {
    document.documentElement.dataset.longReducedMotion = reduced ? 'true' : 'false';
  };

  LongUI.setBusy = function (element, busy) {
    if (!element) return;
    element.toggleAttribute('aria-busy', !!busy);
    if ('disabled' in element) element.disabled = !!busy;
  };

  LongUI.announce = function (message) {
    let region = document.getElementById('long-ui-live-region');
    if (!region) {
      region = document.createElement('div');
      region.id = 'long-ui-live-region';
      region.setAttribute('aria-live', 'polite');
      region.style.cssText = 'position:fixed;width:1px;height:1px;overflow:hidden;clip-path:inset(50%)';
      document.body.appendChild(region);
    }
    region.textContent = '';
    requestAnimationFrame(function () { region.textContent = message || ''; });
  };

  const commandHandlers = LongUI._commandHandlers || new Set();
  LongUI._commandHandlers = commandHandlers;
  const commandQueue = LongUI._commandQueue || [];
  LongUI._commandQueue = commandQueue;

  function normalizeCommandResult(result) {
    if (typeof result === 'string') return { success: true, message: result, outputs: {} };
    if (!result || typeof result !== 'object') return { success: true, outputs: {} };
    return {
      success: result.success !== false,
      message: typeof result.message === 'string' ? result.message : undefined,
      outputs: result.outputs && typeof result.outputs === 'object' ? result.outputs : {}
    };
  }

  async function dispatchCommand(envelope) {
    LongUI.lastCommand = envelope.command;
    const results = [];
    try {
      for (const handler of Array.from(commandHandlers))
        results.push(await handler(envelope.command));
      window.dispatchEvent(new CustomEvent('long:command', { detail: envelope.command }));
      const selected = results.find(function (result) { return result !== undefined; });
      const response = normalizeCommandResult(selected);
      window.chrome.webview.postMessage(Object.assign({
        type: 'long.command-result',
        request_id: envelope.request_id
      }, response));
    } catch (error) {
      window.chrome.webview.postMessage({
        type: 'long.command-result',
        request_id: envelope.request_id,
        success: false,
        message: error && error.message ? error.message : String(error),
        outputs: {}
      });
    }
  }

  LongUI.onCommand = function (handler) {
    if (typeof handler !== 'function') return function () {};
    commandHandlers.add(handler);
    if (commandQueue.length)
      queueMicrotask(function () {
        commandQueue.splice(0).forEach(function (envelope) { void dispatchCommand(envelope); });
      });
    return function () { commandHandlers.delete(handler); };
  };

  LongUI.commandText = function (command) {
    return command && typeof command.text === 'string' ? command.text : '';
  };

  LongUI.commandPaths = function (command) {
    return command && Array.isArray(command.paths) ? command.paths.slice() : [];
  };

  if (!LongUI._commandBridgeInstalled && window.chrome && window.chrome.webview) {
    LongUI._commandBridgeInstalled = true;
    window.chrome.webview.addEventListener('message', function (event) {
      let message = event.data;
      if (typeof message === 'string') {
        try { message = JSON.parse(message); } catch (_) { return; }
      }
      if (!message || message.type !== 'long.command' || !message.command) return;
      const envelope = { request_id: message.request_id, command: message.command };
      if (!commandHandlers.size) commandQueue.push(envelope);
      else void dispatchCommand(envelope);
    });
  }

  window.LongUI = LongUI;
})();
