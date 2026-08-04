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

  LongUI.setHighContrast = function (enabled) {
    document.documentElement.dataset.longHighContrast = enabled ? 'true' : 'false';
  };

  const languageHandlers = LongUI._languageHandlers || new Set();
  LongUI._languageHandlers = languageHandlers;
  const viewportHandlers = LongUI._viewportHandlers || new Set();
  LongUI._viewportHandlers = viewportHandlers;
  const initialLanguage = typeof LongUI._initialLanguage === 'string'
    ? LongUI._initialLanguage : (navigator.language || 'zh-CN');
  LongUI.language = LongUI.language || Object.freeze({
    requestedLanguage: initialLanguage,
    resolvedLanguage: initialLanguage,
    resources: Object.freeze({})
  });
  delete LongUI._initialLanguage;
  LongUI.viewport = LongUI.viewport || readViewport();

  function publishLanguage(message) {
    const source = message && typeof message === 'object' ? message : {};
    const resources = Object.create(null);
    if (source.resources && typeof source.resources === 'object')
      Object.entries(source.resources).forEach(function (entry) {
        if (typeof entry[1] === 'string') resources[entry[0]] = entry[1];
      });
    const fallback = document.documentElement.lang || navigator.language || 'zh-CN';
    const context = Object.freeze({
      requestedLanguage: typeof source.requested_language === 'string'
        ? source.requested_language : fallback,
      resolvedLanguage: typeof source.resolved_language === 'string'
        ? source.resolved_language : fallback,
      resources: Object.freeze(resources)
    });
    LongUI.language = context;
    document.documentElement.lang = context.resolvedLanguage;
    languageHandlers.forEach(function (handler) {
      try { handler(context); } catch (error) { console.error(error); }
    });
    window.dispatchEvent(new CustomEvent('long:language-changed', { detail: context }));
  }

  LongUI._setHostLanguage = function (language) {
    if (typeof language !== 'string' || !language.trim()) return;
    if (LongUI.language &&
        LongUI.language.requestedLanguage.toLowerCase() === language.toLowerCase()) return;
    publishLanguage({ requested_language: language, resolved_language: language, resources: {} });
  };

  function readViewport() {
    const viewport = window.visualViewport;
    return Object.freeze({
      width: Math.max(0, Math.round(viewport ? viewport.width : window.innerWidth)),
      height: Math.max(0, Math.round(viewport ? viewport.height : window.innerHeight))
    });
  }

  function publishViewport() {
    const next = readViewport();
    if (LongUI.viewport.width === next.width && LongUI.viewport.height === next.height) return;
    LongUI.viewport = next;
    viewportHandlers.forEach(function (handler) {
      try { handler(next); } catch (error) { console.error(error); }
    });
    window.dispatchEvent(new CustomEvent('long:viewport-changed', { detail: next }));
  }

  LongUI.onLanguageChanged = function (handler) {
    if (typeof handler !== 'function') return function () {};
    languageHandlers.add(handler);
    if (LongUI.language)
      queueMicrotask(function () {
        if (languageHandlers.has(handler)) handler(LongUI.language);
      });
    return function () { languageHandlers.delete(handler); };
  };

  LongUI.onViewportChanged = function (handler) {
    if (typeof handler !== 'function') return function () {};
    viewportHandlers.add(handler);
    queueMicrotask(function () {
      if (viewportHandlers.has(handler)) handler(LongUI.viewport);
    });
    return function () { viewportHandlers.delete(handler); };
  };

  let viewportFrame = 0;
  function scheduleViewport() {
    if (viewportFrame) cancelAnimationFrame(viewportFrame);
    viewportFrame = requestAnimationFrame(function () {
      viewportFrame = 0;
      publishViewport();
    });
  }
  window.addEventListener('resize', scheduleViewport);
  window.visualViewport?.addEventListener('resize', scheduleViewport);
  document.addEventListener('DOMContentLoaded', function () {
    document.documentElement.lang = LongUI.language.resolvedLanguage;
    publishViewport();
  }, { once: true });

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

  if (!LongUI._environmentBridgeInstalled && window.chrome && window.chrome.webview) {
    LongUI._environmentBridgeInstalled = true;
    window.chrome.webview.addEventListener('message', function (event) {
      let message = event.data;
      if (typeof message === 'string') {
        try { message = JSON.parse(message); } catch (_) { return; }
      }
      if (message && message.type === 'long.language-changed') publishLanguage(message);
    });
  }

  window.LongUI = LongUI;
})();
