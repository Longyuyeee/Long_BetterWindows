import assert from "node:assert/strict";
import test from "node:test";
import {
  BRIDGE_METHODS,
  createLongMock,
  fail,
  installLongMock,
  ok
} from "../mock/index.js";

test("exposes the complete bridge method ledger without duplicates", () => {
  assert.ok(BRIDGE_METHODS.length > 100);
  assert.equal(new Set(BRIDGE_METHODS).size, BRIDGE_METHODS.length);
  assert.ok(BRIDGE_METHODS.includes("clipboard.getText"));
  assert.ok(BRIDGE_METHODS.includes("host.getInfo"));
  assert.ok(BRIDGE_METHODS.includes("widget.setInstanceState"));
  assert.ok(BRIDGE_METHODS.includes("fileSystem.executeOrganization"));
  assert.ok(BRIDGE_METHODS.includes("process.killVerified"));
  assert.ok(BRIDGE_METHODS.includes("window.getVisible"));
});

test("provides deterministic clipboard and atomic storage behavior", async () => {
  const mock = createLongMock({
    clipboardText: "hello",
    storage: { revision: "v1" }
  });

  assert.deepEqual(await mock.long.clipboard.getText(), ok("hello"));
  assert.deepEqual(await mock.long.clipboard.setText("next"), ok());
  assert.deepEqual(await mock.long.clipboard.getText(), ok("next"));

  assert.deepEqual(
    await mock.long.storage.compareExchange("revision", "stale", "v2"),
    ok(false));
  assert.deepEqual(
    await mock.long.storage.compareExchange("revision", "v1", "v2"),
    ok(true));
  assert.deepEqual(await mock.long.storage.get("revision"), ok("v2"));

  assert.equal(mock.getCalls("storage.compareExchange").length, 2);
});

test("exposes host capability negotiation and widget state helpers", async () => {
  const mock = createLongMock({
    pluginId: "com.test.widget",
    surface: "widget",
    widgetId: "system.status",
    instanceId: "instance-1",
    features: ["widget.instance-state", "theme.v1"]
  });

  const host = await mock.long.host.getInfo();
  assert.equal(host.protocol_version, "1.0");
  assert.equal(host.api_version, "1.1.0");
  assert.equal(host.plugin_id, "com.test.widget");
  assert.equal(host.surface, "widget");
  assert.equal(host.widget_id, "system.status");
  assert.ok(host.features.includes("widget.instance-state"));

  assert.deepEqual(await mock.long.widget.ready(), ok());
  assert.deepEqual(
    await mock.long.widget.setInstanceState({ selectedView: "cpu" }),
    ok());
  assert.deepEqual(
    await mock.long.widget.getInstanceState(),
    ok({ selectedView: "cpu" }));
});

test("supports handlers, call inspection, hotkeys, and clipboard events", async () => {
  const mock = createLongMock();
  mock.setHandler("http.get", async url => ok(`fixture:${url}`));

  assert.deepEqual(
    await mock.long.http.get("https://example.test"),
    ok("fixture:https://example.test"));
  assert.deepEqual(mock.getCalls("http.get")[0], {
    method: "http.get",
    args: ["https://example.test"]
  });

  let hotkeyCount = 0;
  await mock.long.hotkey.register("Alt+X", () => hotkeyCount++);
  assert.equal(mock.emitHotkey("Alt+X"), true);
  assert.equal(hotkeyCount, 1);
  await mock.long.hotkey.unregister("Alt+X");
  assert.equal(mock.emitHotkey("Alt+X"), false);

  let clipboardEvent = null;
  await mock.long.clipboard.startMonitoring(event => {
    clipboardEvent = event;
  });
  assert.equal(mock.emitClipboardChanged({ text: "changed" }), true);
  assert.equal(clipboardEvent.text, "changed");
  await mock.long.clipboard.stopMonitoring();
  assert.equal(mock.emitClipboardChanged(), false);
});

test("keeps API aliases mapped to the production bridge method", async () => {
  const mock = createLongMock();
  await mock.long.networkPort.findPortOwner(443, "tcp");
  await mock.long.networkPort.isPortInUse(53, "udp");
  await mock.long.power.getBatteryStatus();

  assert.deepEqual(mock.calls.map(call => call.method), [
    "networkPort.findOwner",
    "networkPort.isInUse",
    "power.getStatus"
  ]);
});

test("records the complete identity for verified process termination", async () => {
  const mock = createLongMock();
  await mock.long.process.killVerified(42, "demo", "identity-token");

  assert.deepEqual(mock.getCalls("process.killVerified")[0], {
    method: "process.killVerified",
    args: [42, "demo", "identity-token"]
  });
});

test("installs without browser globals and rejects unknown handlers", () => {
  const target = {};
  const mock = installLongMock({ version: "1.2.3" }, target);
  assert.equal(target.long, mock.long);
  assert.throws(
    () => mock.setHandler("unknown.method", () => fail("no")),
    /Unknown Long bridge method/);
});
