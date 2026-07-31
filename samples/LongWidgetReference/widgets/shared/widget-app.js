import { createReferenceWidget } from "./widget-controller.js";

const kind = document.documentElement.dataset.widgetKind;
const value = document.getElementById("value");
const identity = document.getElementById("identity");
const status = document.getElementById("status");
const badge = document.getElementById("lifecycleBadge");
const modeButton = document.getElementById("modeButton");
let strings = {};

function t(key, fallback) {
  return strings[key] ?? fallback;
}

function applyLocalization(message) {
  strings = message?.resources ?? {};
  document.documentElement.lang = message?.resolved_language ?? "zh-CN";
  document.querySelectorAll("[data-i18n]").forEach(element => {
    element.textContent = t(element.dataset.i18n, element.textContent);
  });
  modeButton.textContent = document.body.dataset.mode === "calm"
    ? t("widget.mode.detail", "显示详情")
    : t("widget.mode.calm", "保持简洁");
}

function setStatus(message) {
  status.textContent = message;
  window.LongUI?.announce(message);
}

function render(snapshot) {
  value.textContent = String(snapshot.state.value);
  document.body.dataset.mode = snapshot.state.mode;
  identity.textContent =
    `${snapshot.info.widget_id} · ${snapshot.info.instance_id.slice(0, 8)}`;
  modeButton.textContent = snapshot.state.mode === "calm"
    ? t("widget.mode.detail", "显示详情")
    : t("widget.mode.calm", "保持简洁");
}

function listenLifecycle() {
  const labels = {
    "long.widget-mounted": "已挂载",
    "long.widget-resized": "尺寸已同步",
    "long.widget-visibility-changed": "可见性已同步",
    "long.widget-suspend": "已暂停",
    "long.widget-resume": "运行中",
    "long.widget-unmount": "已卸载"
  };
  for (const [eventName, label] of Object.entries(labels)) {
    window.addEventListener(eventName, event => {
      badge.textContent = t(`widget.lifecycle.${eventName.slice(12)}`, label);
      if (eventName === "long.widget-resized") {
        const payload = event.detail?.payload ?? {};
        setStatus(`${payload.columns ?? "?"}×${payload.rows ?? "?"} · ${payload.width ?? "?"} px`);
      }
    });
  }
}

async function start() {
  listenLifecycle();
  window.chrome?.webview?.addEventListener("message", ({ data }) => {
    if (data?.type === "long.language-changed") applyLocalization(data);
  });
  try {
    const controller = await createReferenceWidget(window.long, {
      initialValue: 0,
      initialMode: kind === "focus" ? "detail" : "calm"
    });
    render(controller.snapshot());
    badge.textContent = t("widget.lifecycle.resume", "运行中");

    document.getElementById("incrementButton").addEventListener("click", async () => {
      render(await controller.increment());
      setStatus(t("widget.status.saved", "实例状态已保存"));
    });
    modeButton.addEventListener("click", async () => {
      render(await controller.toggleMode());
      setStatus(t("widget.status.modeSaved", "视图偏好已保存"));
    });
    document.getElementById("refreshButton").addEventListener("click", async () => {
      await controller.requestRefresh(`${kind}-manual`);
      setStatus(t("widget.status.refresh", "已向宿主请求刷新"));
    });
  } catch (error) {
    badge.textContent = t("widget.unavailable", "不可用");
    setStatus(error instanceof Error
      ? error.message
      : t("widget.error.initialize", "Widget 初始化失败"));
  }
}

start();
