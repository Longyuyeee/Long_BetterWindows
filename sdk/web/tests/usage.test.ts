import type {
  LongApi,
  LongClipboardChangedEvent,
  LongFileOrganizationItem,
  LongHostInfo,
  LongResult,
  LongUiLanguageContext,
  LongUiConfirmOptions,
  LongUiToastOptions,
  LongUiViewport,
  LongUiKitVersion
} from "@long-assistant/plugin-sdk";
import {
  createLongMock,
  type LongMockController
} from "@long-assistant/plugin-sdk/mock";

async function exercise(api: LongApi): Promise<void> {
  const info: LongHostInfo = await api.host.getInfo();
  info.api_version satisfies "1.1.0";
  if (info.surface === "widget") {
    await api.widget.ready();
    await api.widget.setInstanceState({ view: "compact" });
    const widgetState = await api.widget.getInstanceState<{ view: string }>();
    widgetState.data?.view.toUpperCase();
    window.addEventListener("long.widget-resized", event => {
      event.detail.payload.width.toFixed();
      event.detail.sequence.toFixed();
    });
  }

  const clipboard: LongResult<string | null> = await api.clipboard.getText();
  if (clipboard.success && clipboard.data) {
    await api.clipboard.setText(clipboard.data.toUpperCase());
  }

  await api.clipboard.startMonitoring(
    (event: LongClipboardChangedEvent) => {
      event.content_type satisfies "text" | "image" | "files" | "unknown";
    });

  const stored = await api.storage.get<string>("draft");
  await api.storage.compareExchange("draft", stored.data ?? null, "next");

  const items: LongFileOrganizationItem[] = [];
  await api.fileSystem.executeOrganization(
    "C:\\Inbox",
    "ByExtension",
    items);

  const owner = await api.networkPort.findPortOwner(8080, "tcp");
  owner.data?.ProcessName.toLocaleLowerCase();
  if (owner.data?.ProcessIdentity) {
    await api.process.killPortOwnerVerified(owner.data);
    await api.process.killVerified(
      owner.data.ProcessId,
      owner.data.ProcessName,
      owner.data.ProcessIdentity);
  }

  await api.ui.confirm("Continue?", "Long Assistant");
  await api.http.get("https://example.test", { Accept: "application/json" });

  // @ts-expect-error clipboard text must be a string
  await api.clipboard.setText(42);
  // @ts-expect-error protocol is restricted to tcp or udp
  await api.networkPort.findPortOwner(8080, "icmp");
  // @ts-expect-error verified termination requires the identity token
  await api.process.killVerified(42, "demo");
}

const controller: LongMockController = createLongMock({
  clipboardText: "hello",
  storage: { draft: "old" }
});

void exercise(controller.long);
window.long = controller.long;
const uiKitVersion: LongUiKitVersion | undefined = window.LongUI?.version;
uiKitVersion?.toString();
const toastOptions: LongUiToastOptions = {
  message: "Saved",
  kind: "success",
  duration: 3000
};
window.LongUI?.showToast(toastOptions)();
const confirmOptions: LongUiConfirmOptions = {
  title: "Delete item",
  message: "This cannot be undone.",
  danger: true
};
void window.LongUI?.confirm(confirmOptions);
const stopLanguage = window.LongUI?.onLanguageChanged((context: LongUiLanguageContext) => {
  context.resolvedLanguage.toLowerCase();
  Object.keys(context.resources);
});
const stopViewport = window.LongUI?.onViewportChanged((viewport: LongUiViewport) => {
  viewport.width.toFixed();
  viewport.height.toFixed();
});
stopLanguage?.();
stopViewport?.();
long.app.log("type contract ready");
