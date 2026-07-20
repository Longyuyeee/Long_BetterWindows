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

  LongUI.onCommand = function (handler) {
    if (typeof handler !== 'function') return function () {};
    commandHandlers.add(handler);
    if (LongUI.lastCommand)
      queueMicrotask(function () { handler(LongUI.lastCommand); });
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
      LongUI.lastCommand = message.command;
      commandHandlers.forEach(function (handler) {
        try { handler(message.command); } catch (error) { console.error(error); }
      });
      window.dispatchEvent(new CustomEvent('long:command', { detail: message.command }));
    });
  }

  window.LongUI = LongUI;
})();
