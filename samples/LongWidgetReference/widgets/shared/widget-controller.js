function unwrap(result, operation) {
  if (result && typeof result === "object" && "success" in result) {
    if (!result.success) {
      throw new Error(result.error || `${operation} failed`);
    }
    return result.data ?? null;
  }
  return result ?? null;
}

function normalizeState(value, defaults) {
  if (!value || typeof value !== "object" || Array.isArray(value)) {
    return { ...defaults };
  }
  return {
    value: Number.isSafeInteger(value.value) && value.value >= 0
      ? Math.min(value.value, 9999)
      : defaults.value,
    mode: value.mode === "detail" ? "detail" : "calm"
  };
}

export async function createReferenceWidget(longApi, options = {}) {
  if (!longApi?.host?.getInfo || !longApi?.widget) {
    throw new Error("Long Widget bridge is unavailable.");
  }
  const info = unwrap(await longApi.host.getInfo(), "host.getInfo");
  if (info?.surface !== "widget"
      || !info.widget_id
      || !info.instance_id) {
    throw new Error("Reference Widget requires a trusted widget context.");
  }

  const defaults = {
    value: Number.isSafeInteger(options.initialValue) ? options.initialValue : 0,
    mode: options.initialMode === "detail" ? "detail" : "calm"
  };
  const persisted = unwrap(
    await longApi.widget.getInstanceState(),
    "widget.getInstanceState");
  let state = normalizeState(persisted, defaults);

  async function persist() {
    unwrap(
      await longApi.widget.setInstanceState({ ...state }),
      "widget.setInstanceState");
    unwrap(
      await longApi.widget.setBadge({
        text: state.value > 0 ? String(state.value) : "",
        tone: "accent"
      }),
      "widget.setBadge");
    return snapshot();
  }

  function snapshot() {
    return Object.freeze({
      info,
      state: Object.freeze({ ...state })
    });
  }

  unwrap(await longApi.widget.ready(1), "widget.ready");

  return Object.freeze({
    snapshot,
    async increment() {
      state = { ...state, value: Math.min(9999, state.value + 1) };
      return await persist();
    },
    async toggleMode() {
      state = {
        ...state,
        mode: state.mode === "calm" ? "detail" : "calm"
      };
      return await persist();
    },
    async requestRefresh(reason = "reference-widget") {
      unwrap(await longApi.widget.invalidate(reason), "widget.invalidate");
      return snapshot();
    }
  });
}
