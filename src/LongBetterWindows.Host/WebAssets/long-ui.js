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

  function appendSurface(element) {
    (document.body || document.documentElement).appendChild(element);
  }

  function getToastRegion() {
    let region = document.querySelector('[data-long-toast-region]');
    if (region) return region;
    region = document.createElement('div');
    region.className = 'long-toast-region';
    region.dataset.longToastRegion = '';
    region.setAttribute('role', 'region');
    region.setAttribute('aria-label', 'Notifications');
    appendSurface(region);
    return region;
  }

  LongUI.showToast = function (options) {
    const settings = typeof options === 'string' ? { message: options } : (options || {});
    if (typeof settings.message !== 'string' || !settings.message.trim()) return function () {};
    const supportedKinds = new Set(['info', 'success', 'warning', 'error']);
    const kind = supportedKinds.has(settings.kind) ? settings.kind : 'info';
    const region = getToastRegion();
    const toast = document.createElement('div');
    toast.className = 'long-toast long-toast--' + kind;
    toast.setAttribute('role', kind === 'error' ? 'alert' : 'status');

    const message = document.createElement('div');
    message.className = 'long-toast__message';
    message.textContent = settings.message;
    toast.appendChild(message);

    const requestedDuration = Number(settings.duration);
    const duration = settings.duration === 0 ? 0
      : (Number.isFinite(requestedDuration) ? Math.min(30000, Math.max(1000, requestedDuration)) : 4000);
    let remaining = duration;
    let timer = 0;
    let timerStartedAt = 0;
    let dismissed = false;
    function dismiss(immediate) {
      if (dismissed) return;
      dismissed = true;
      if (timer) window.clearTimeout(timer);
      if (immediate === true) {
        toast.remove();
        return;
      }
      toast.classList.add('long-toast--closing');
      window.setTimeout(function () {
        toast.remove();
        if (!region.childElementCount) region.remove();
      }, 200);
    }
    toast._longDismiss = dismiss;

    function startTimer() {
      if (!remaining || timer || dismissed) return;
      timerStartedAt = performance.now();
      timer = window.setTimeout(dismiss, remaining);
    }

    function pauseTimer() {
      if (!timer) return;
      window.clearTimeout(timer);
      timer = 0;
      remaining = Math.max(0, remaining - (performance.now() - timerStartedAt));
    }

    if (typeof settings.actionLabel === 'string' &&
        settings.actionLabel.trim() &&
        typeof settings.onAction === 'function') {
      const action = document.createElement('button');
      action.type = 'button';
      action.className = 'long-button long-button--small long-toast__action';
      action.textContent = settings.actionLabel;
      action.addEventListener('click', function () {
        try { settings.onAction(); } finally { dismiss(); }
      });
      toast.appendChild(action);
    }

    const close = document.createElement('button');
    close.type = 'button';
    close.className = 'long-toast__close';
    close.setAttribute('aria-label', typeof settings.dismissLabel === 'string'
      ? settings.dismissLabel : 'Dismiss');
    close.textContent = '\u00d7';
    close.addEventListener('click', dismiss);
    toast.appendChild(close);
    toast.addEventListener('pointerenter', pauseTimer);
    toast.addEventListener('pointerleave', startTimer);
    toast.addEventListener('focusin', pauseTimer);
    toast.addEventListener('focusout', function (event) {
      if (!toast.contains(event.relatedTarget)) startTimer();
    });
    region.appendChild(toast);

    while (region.childElementCount > 4) {
      const oldest = region.firstElementChild;
      if (oldest && typeof oldest._longDismiss === 'function') oldest._longDismiss(true);
      else oldest?.remove();
    }

    startTimer();
    return dismiss;
  };

  let confirmQueue = Promise.resolve();
  let confirmId = 0;

  function notifyHostModalState(open) {
    if (window.chrome && window.chrome.webview) {
      window.chrome.webview.postMessage({ type: 'long.ui-modal-state', open: !!open });
    }
  }

  function showConfirm(options) {
    const settings = typeof options === 'string' ? { message: options } : (options || {});
    if (typeof settings.message !== 'string' || !settings.message.trim()) return Promise.resolve(false);
    return new Promise(function (resolve) {
      const previousFocus = document.activeElement;
      const dialog = document.createElement('dialog');
      const titleId = 'long-confirm-title-' + (++confirmId);
      dialog.className = 'long-dialog';
      dialog.setAttribute('aria-labelledby', titleId);

      const header = document.createElement('div');
      header.className = 'long-dialog__header';
      const title = document.createElement('h2');
      title.className = 'long-dialog__title';
      title.id = titleId;
      title.textContent = typeof settings.title === 'string' && settings.title.trim()
        ? settings.title : 'Confirm';
      header.appendChild(title);
      dialog.appendChild(header);

      const body = document.createElement('div');
      body.className = 'long-dialog__body';
      body.textContent = settings.message;
      dialog.appendChild(body);

      const actions = document.createElement('div');
      actions.className = 'long-dialog__actions';
      const cancel = document.createElement('button');
      cancel.type = 'button';
      cancel.className = 'long-button long-button--secondary';
      cancel.textContent = settings.cancelLabel || 'Cancel';
      cancel.addEventListener('click', function () { dialog.close('cancel'); });
      const confirm = document.createElement('button');
      confirm.type = 'button';
      confirm.className = settings.danger
        ? 'long-button long-button--danger' : 'long-button long-button--primary';
      confirm.textContent = settings.confirmLabel || 'Confirm';
      confirm.addEventListener('click', function () { dialog.close('confirm'); });
      actions.append(cancel, confirm);
      dialog.appendChild(actions);

      dialog.addEventListener('cancel', function (event) {
        event.preventDefault();
        dialog.close('cancel');
      });
      dialog.addEventListener('click', function (event) {
        if (event.target === dialog) dialog.close('cancel');
      });
      dialog.addEventListener('close', function () {
        const accepted = dialog.returnValue === 'confirm';
        notifyHostModalState(false);
        dialog.remove();
        if (previousFocus instanceof HTMLElement && previousFocus.isConnected) previousFocus.focus();
        resolve(accepted);
      }, { once: true });

      appendSurface(dialog);
      notifyHostModalState(true);
      try {
        dialog.showModal();
      } catch (error) {
        notifyHostModalState(false);
        dialog.remove();
        resolve(false);
        return;
      }
      cancel.focus();
    });
  }

  LongUI.confirm = function (options) {
    const result = confirmQueue.then(function () { return showConfirm(options); });
    confirmQueue = result.then(function () {}, function () {});
    return result;
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
