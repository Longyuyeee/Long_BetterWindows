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

  LongUI.clearState = function (container) {
    if (!container) return;
    container.removeAttribute('aria-busy');
    container.replaceChildren();
  };

  LongUI.renderState = function (container, options) {
    if (!container) return null;

    const settings = options && typeof options === 'object' ? options : {};
    const supportedKinds = new Set(['empty', 'loading', 'error']);
    const kind = supportedKinds.has(settings.kind) ? settings.kind : 'empty';
    const defaultTitles = {
      empty: '暂无内容',
      loading: '正在加载',
      error: '加载失败'
    };

    const state = document.createElement('div');
    state.className = 'long-state long-state--' + kind;
    state.dataset.longState = kind;
    state.setAttribute('role', kind === 'error' ? 'alert' : 'status');
    state.setAttribute('aria-live', kind === 'error' ? 'assertive' : 'polite');

    const indicator = document.createElement('span');
    indicator.className = 'long-state__indicator';
    indicator.setAttribute('aria-hidden', 'true');
    state.appendChild(indicator);

    const title = document.createElement('div');
    title.className = 'long-state__title';
    title.textContent = typeof settings.title === 'string' && settings.title.trim()
      ? settings.title
      : defaultTitles[kind];
    state.appendChild(title);

    if (typeof settings.detail === 'string' && settings.detail.trim()) {
      const detail = document.createElement('div');
      detail.className = 'long-state__detail';
      detail.textContent = settings.detail;
      state.appendChild(detail);
    }

    if (typeof settings.actionLabel === 'string' &&
        settings.actionLabel.trim() &&
        typeof settings.onAction === 'function') {
      const action = document.createElement('button');
      action.type = 'button';
      action.className = 'long-button long-button--small long-state__action';
      action.textContent = settings.actionLabel;
      action.addEventListener('click', settings.onAction);
      state.appendChild(action);
    }

    container.toggleAttribute('aria-busy', kind === 'loading');
    container.replaceChildren(state);
    return state;
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
